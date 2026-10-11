using Server;
using UnityEngine;

/// <summary>
/// First boss: a huge, tanky brute.
///
/// Motions:
///   1. Walk       - chases the target (inherited Zombrain behaviour).
///   2. Attack     - basic bite at normal range (attribute.SIGHT_Current).
///   3. Charge     - steps backward for 2s, then rushes at the target for up to
///                   0.5s, slams on impact, then is stunned/exhausted for
///                   0.5-1.5s (random). 3s cooldown after that.
///
/// Aim: the rush follows Zombrain.Direction / DistanceToTarget, which only
/// refresh every 0.1-0.3s (DefineDirection). That delay is what keeps the
/// charge from being pixel-perfect, so moving players can dodge it.
///
/// Two attack ranges:
///   - Basic range  = SIGHT_Current
///   - Charge range = SIGHT_Current * chargeRangeMultiplier (default 3x).
///
/// Hit stagger: this boss only takes half the slow AND half the duration
/// of a normal zombie (see staggerMultiplier).
///
/// Animator parameters (add to the controller):
///   bool    IsWalking      (existing; forced false during the charge skill)
///   bool    IsAttacking    (existing; refreshed every frame, see UpdateAnimator)
///   bool    IsBackStepping (new) - the 2s retreat wind-up
///   bool    IsCharging     (new) - the rush
///   trigger ChargeStrike   (new) - fired once when the slam lands
/// </summary>
public class BigMeat : Zombrain
{
    private enum Phase { Normal, BackStep, Charge, Recover }

    [Header("BigMeat - Charge Skill")]
    [Tooltip("Seconds between the end of the recovery and the next wind-up.")]
    [SerializeField] private float chargeCooldown = 3f;
    [Tooltip("How long it steps backward before charging.")]
    [SerializeField] private float backStepDuration = 2f;
    [Tooltip("Maximum rush time. The rush ends earlier on impact.")]
    [SerializeField] private float chargeDuration = 0.5f;
    [Tooltip("Flat speed added during the rush.")]
    [SerializeField] private float chargeSpeedBonus = 90f;
    [Tooltip("Charge slam reach = basic range * this.")]
    [SerializeField] private float chargeRangeMultiplier = 3f;
    [Tooltip("Slam damage = ATK * this.")]
    [SerializeField] private float chargeDamageMultiplier = 2f;
    [Tooltip("The rush ends and slams once within basic range * this of the target.")]
    [SerializeField] private float impactRangeMultiplier = 1f;

    [Header("BigMeat - Recovery")]
    [Tooltip("After the slam the boss stands still for a random time in this range.")]
    [SerializeField] private float recoverMin = 0.5f;
    [SerializeField] private float recoverMax = 1.5f;

    [Header("BigMeat - Hit Stagger")]
    [Tooltip("Scales both the slow and the duration of hit stagger. 0.5 = half.")]
    [SerializeField, Range(0f, 1f)] private float staggerMultiplier = 0.5f;

    // Must match Zombrain.HitStaggerSlowPercent / HitStaggerDuration (private consts there).
    private const float BaseStaggerSlow = -0.4f;
    private const float BaseStaggerDuration = 1.5f;

    private static readonly int AttackingHash = Animator.StringToHash("IsAttacking");
    private static readonly int WalkHash = Animator.StringToHash("IsWalking");
    private static readonly int BackStepHash = Animator.StringToHash("IsBackStepping");
    private static readonly int ChargingHash = Animator.StringToHash("IsCharging");
    private static readonly int ChargeStrikeHash = Animator.StringToHash("ChargeStrike");

    private Phase phase = Phase.Normal;
    private float phaseTimer;
    private float cooldownTimer;
    private float appliedCharge;
    private Vector2 chargeDir;
    private float chargeSqrDist;

    // Own stagger bookkeeping (Zombrain's is private).
    private float bossStaggerTimer;
    private float appliedStagger;

    private float BasicRange => attribute != null ? attribute.SIGHT_Current : 0f;
    private float ChargeRange => BasicRange * chargeRangeMultiplier;

    #region Bonus
    protected override void GetBonus()
    {
        base.GetBonus();
    }
    #endregion

    #region Lifecycle
    public override bool SpawnObject(ZomPackage zp)
    {
        if (!base.SpawnObject(zp)) return false;

        // Fresh attribute clone, so just clear the bookkeeping (nothing to subtract).
        bossStaggerTimer = 0f;
        appliedStagger = 0f;
        appliedCharge = 0f;

        ResetSkill();
        cooldownTimer = chargeCooldown;
        return true;
    }

    public override void Tick(float dt)
    {
        UpdatePhase(dt);
        base.Tick(dt);
        TickBossStagger(dt);
    }

    protected override void Despawn()
    {
        bool wasActive = isActive;
        ResetSkill();
        RemoveBossStagger();
        base.Despawn();

        if (wasActive && BossManager.instance != null)
            BossManager.instance.NotifyBossKilled(gameObject);
    }

    public override void Reboot()
    {
        ResetSkill();
        RemoveBossStagger();
        base.Reboot();
    }
    #endregion

    #region Hit Stagger (half value, half duration)
    protected override void ApplyHitStagger()
    {
        if (attribute == null || attribute.IsDead) return;

        bossStaggerTimer = BaseStaggerDuration * staggerMultiplier;
        if (appliedStagger != 0f) return; // already slowed, only refresh the timer

        appliedStagger = BaseStaggerSlow * staggerMultiplier;
        attribute.SPEED_Ampl.TotalBonus += appliedStagger;
    }

    private void TickBossStagger(float dt)
    {
        if (appliedStagger == 0f) return;

        bossStaggerTimer -= dt;
        if (bossStaggerTimer <= 0f) RemoveBossStagger();
    }

    private void RemoveBossStagger()
    {
        if (appliedStagger != 0f && attribute != null)
            attribute.SPEED_Ampl.TotalBonus -= appliedStagger;
        appliedStagger = 0f;
        bossStaggerTimer = 0f;
    }
    #endregion

    #region Skill State Machine
    private void UpdatePhase(float dt)
    {
        if (!isActive || attribute == null) return;

        switch (phase)
        {
            case Phase.Normal:
                if (cooldownTimer > 0f)
                {
                    cooldownTimer -= dt;
                    return;
                }
                if (CanStartBackStep())
                {
                    phaseTimer = backStepDuration;
                    SetPhase(Phase.BackStep);
                }
                break;

            case Phase.BackStep:
                if (Target == null)
                {
                    // Lost the target mid wind-up; abort and retry later.
                    EndSkill();
                    return;
                }
                phaseTimer -= dt;
                if (phaseTimer <= 0f) StartCharge();
                break;

            case Phase.Charge:
                // Uses the delayed Direction/DistanceToTarget from DefineDirection,
                // so the rush is slightly behind a moving target.
                bool aimed = AimFromCache();
                phaseTimer -= dt;

                float impact = BasicRange * impactRangeMultiplier;
                bool hasImpact = aimed && chargeSqrDist <= impact * impact;

                if (hasImpact || phaseTimer <= 0f)
                {
                    ChargeStrike();
                    StartRecover();
                }
                break;

            case Phase.Recover:
                phaseTimer -= dt;
                if (phaseTimer <= 0f) EndSkill();
                break;
        }
    }

    /// <summary>
    /// Takes the aim from Zombrain's cached Direction / DistanceToTarget
    /// (refreshed every 0.1-0.3s by DefineDirection) instead of the live
    /// target position. False if there is no valid cached aim.
    /// </summary>
    private bool AimFromCache()
    {
        if (Target == null || Direction == Vector2.zero) return false;

        chargeDir = Direction;
        chargeSqrDist = DistanceToTarget * DistanceToTarget;
        return true;
    }

    private bool CanStartBackStep()
    {
        return Target != null
            && CanMove
            && !isAttacking
            && Direction != Vector2.zero
            && DistanceToTarget <= ChargeRange;
    }

    private void StartCharge()
    {
        if (!AimFromCache())
        {
            EndSkill();
            return;
        }

        phaseTimer = chargeDuration;

        appliedCharge = chargeSpeedBonus;
        attribute.SPEED_Ampl.FlatBonus += appliedCharge;

        SetPhase(Phase.Charge);
    }

    private void ChargeStrike()
    {
        if (package == null || attribute == null || attribute.IsDead) return;

        PlayAttackSound();
        if (anim != null) anim.SetTrigger(ChargeStrikeHash);

        // Slam reaches 3x the basic range along the last aim, piercing everything in line.
        AttackResult[] hits = Attack.ShootPierce(
            gameObject,
            attribute.ATK_Current * chargeDamageMultiplier,
            attribute,
            ChargeRange,
            chargeDir,
            hb.enemyMask
        );
        if(hits.Length > 0)
            CamManager.instance?.Shake(1f);

        for (int i = 0; i < hits.Length; i++)
            HandleHitEffect(hits[i]);
    }

    /// <summary>Rush is over: drop the speed burst and stand still for a random time.</summary>
    private void StartRecover()
    {
        RemoveChargeSpeed();
        phaseTimer = RNG.GetFloat(recoverMin, recoverMax);
        SetPhase(Phase.Recover);
    }

    /// <summary>Back to normal behaviour and start the cooldown.</summary>
    private void EndSkill()
    {
        ResetSkill();
        cooldownTimer = chargeCooldown;
    }

    private void RemoveChargeSpeed()
    {
        if (appliedCharge != 0f && attribute != null)
            attribute.SPEED_Ampl.FlatBonus -= appliedCharge;
        appliedCharge = 0f;
    }

    /// <summary>Clears skill state and any applied speed bonus. Safe to call anytime.</summary>
    private void ResetSkill()
    {
        RemoveChargeSpeed();

        phaseTimer = 0f;
        cooldownTimer = 0f;
        SetPhase(Phase.Normal);
    }

    private void SetPhase(Phase p)
    {
        phase = p;
        if (anim == null) return;
        anim.SetBool(BackStepHash, p == Phase.BackStep);
        anim.SetBool(ChargingHash, p == Phase.Charge);
    }
    #endregion

    #region Overrides: Movement / Combat / Animation
    protected override void Move()
    {
        switch (phase)
        {
            case Phase.Normal:
                base.Move();
                break;

            case Phase.BackStep:
                if (!CanMove || Direction == Vector2.zero) return;
                UnitMotion.MoveThisObject(attribute, gameObject, -Direction);
                KeepInsideMap();
                break;

            case Phase.Charge:
                // chargeDir was refreshed this tick in UpdatePhase.
                UnitMotion.RotateThisObject(gameObject, chargeDir);
                UnitMotion.MoveThisObject(attribute, gameObject, chargeDir);
                break;

            case Phase.Recover:
                // Standing still after the slam.
                break;
        }
    }

    // Charge rotates itself in Move; recovery keeps the last facing.
    protected override void Rotate()
    {
        if (phase == Phase.Charge || phase == Phase.Recover) return;
        base.Rotate();
    }

    // No basic bites during the skill or recovery.
    protected override void TryAttack()
    {
        if (phase != Phase.Normal) return;
        base.TryAttack();
    }

    // Same logic as Zombrain.PerformAttack, plus diagnostics for every way a
    // bite can end up dealing no damage.
    protected override void PerformAttack()
    {
        if (package == null) return;

        PlayAttackSound();

        float roll = RNG.GetPercent();
        if (roll < package.BiteAccuracy)
        {
            AttackResult rs = Attack.Shoot(gameObject, attribute, attribute.SIGHT_Current, Direction, hb.enemyMask);

            HandleHitEffect(rs);
        }

        attackStagger = Time.time + RNG.GetFloat(attribute.ASPD_Current, 2f);
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (anim == null) return;

        // Zombrain.PerformAttack sets IsAttacking BEFORE it updates attackStagger,
        // so refresh it here, after the stagger is set, and keep it off while
        // the charge skill / recovery is running.
        anim.SetBool(AttackingHash, phase == Phase.Normal && CanAttack && isAttacking);

        if (phase != Phase.Normal)
            anim.SetBool(WalkHash, false);
    }

    private void KeepInsideMap()
    {
        if (dm == null) return;
        Vector3 p = _t.position;
        Vector2 c = dm.ClampToMap(p);
        _t.position = new Vector3(c.x, c.y, p.z);
    }
    #endregion
}