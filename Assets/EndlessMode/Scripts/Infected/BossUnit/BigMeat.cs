using Server;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// First boss: a huge, tanky brute. Hard-hitting version.
///
/// Motions:
///   1. Walk       - chases the target (inherited Zombrain behaviour).
///   2. Attack     - basic bite at normal range (attribute.SIGHT_Current).
///   3. Charge     - short backstep, then a rush that leads (predicts) the
///                   target, ends in an area slam. It can CHAIN into another
///                   charge right away. Only the player's dash (invincibility)
///                   avoids the slam, because it hits everything in a radius.
///                   After the chain it is exhausted for a short random time.
///
/// Two attack ranges:
///   - Basic range  = SIGHT_Current
///   - Charge range = SIGHT_Current * chargeRangeMultiplier (default 3x).
///
/// Animator parameters (add to the controller):
///   bool    IsWalking      (existing; forced false during the charge skill)
///   bool    IsAttacking    (existing; refreshed every frame, see UpdateAnimator)
///   bool    IsBackStepping (new) - the retreat wind-up
///   bool    IsCharging     (new) - the rush
///   trigger ChargeStrike   (new) - fired once when the slam lands
/// </summary>
public class BigMeat : Zombrain
{
    private enum Phase { Normal, BackStep, Charge, Recover }

    [Header("BigMeat - Charge Skill")]
    [Tooltip("Seconds between the end of the recovery and the next wind-up.")]
    [SerializeField] private float chargeCooldown = 1.2f;
    [Tooltip("How long it steps backward before charging.")]
    [SerializeField] private float backStepDuration = 0.8f;
    [Tooltip("Maximum rush time. The rush ends earlier on impact.")]
    [SerializeField] private float chargeDuration = 0.7f;
    [Tooltip("Flat speed added during the rush.")]
    [SerializeField] private float chargeSpeedBonus = 90f;
    [Tooltip("Charge starts when the target is within basic range * this.")]
    [SerializeField] private float chargeRangeMultiplier = 3f;
    [Tooltip("Slam damage = ATK * this.")]
    [SerializeField] private float chargeDamageMultiplier = 4f;
    [Tooltip("The rush ends and slams once within basic range * this of the target.")]
    [SerializeField] private float impactRangeMultiplier = 1.5f;

    [Header("BigMeat - Aim")]
    [Tooltip("0 = uses the delayed Direction (0.1-0.3s old), 1 = fully live aim.")]
    [SerializeField, Range(0f, 1f)] private float aimAccuracy = 1f;
    [Tooltip("The rush aims this many seconds ahead of the target's movement. 0 = no prediction.")]
    [SerializeField] private float leadSeconds = 0.25f;

    [Header("BigMeat - Slam (area, hard to miss)")]
    [Tooltip("The slam hits everything within basic range * this around the boss.")]
    [SerializeField] private float slamRadiusMultiplier = 1.5f;

    [Header("BigMeat - Chain")]
    [Tooltip("Chance to immediately charge again after a slam.")]
    [SerializeField, Range(0f, 1f)] private float chainChance = 0.5f;
    [Tooltip("Total charges in one chain (1 = no chaining).")]
    [SerializeField] private int maxCharges = 3;
    [Tooltip("Short retreat between chained charges.")]
    [SerializeField] private float chainBackStepDuration = 0.35f;

    [Header("BigMeat - Recovery")]
    [Tooltip("After the chain the boss stands still for a random time in this range.")]
    [SerializeField] private float recoverMin = 0.3f;
    [SerializeField] private float recoverMax = 0.6f;

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
    private int chargeCount;

    // Target velocity tracking for lead aiming.
    private Vector2 lastTargetPos;
    private Vector2 targetVel;
    private bool hasLastTargetPos;

    // Slam buffers (no per-hit allocation).
    private readonly Collider2D[] slamBuffer = new Collider2D[16];
    private readonly List<UnitAttribute> slamHit = new List<UnitAttribute>(8);

    // Own stagger bookkeeping (Zombrain's is private).
    private float bossStaggerTimer;
    private float appliedStagger;

    private float BasicRange => attribute != null ? attribute.SIGHT_Current : 0f;
    private float ChargeRange => BasicRange * chargeRangeMultiplier;

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
        IsBoss = true;

        return true;
    }

    protected override void GetBonus()
    {
        base.GetBonus();

        float bossBonus = RNG.GetInt(3, 6);
        lastHPBonus *= bossBonus;
        lastHPP *= bossBonus;
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

        if (phase == Phase.BackStep || phase == Phase.Charge)
            TrackTargetVelocity(dt);

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
                    chargeCount = 0;
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
                bool aimed = AimAtTarget();
                phaseTimer -= dt;

                float impact = BasicRange * impactRangeMultiplier;
                bool hasImpact = aimed && chargeSqrDist <= impact * impact;

                if (hasImpact || phaseTimer <= 0f)
                {
                    ChargeStrike();
                    FinishCharge();
                }
                break;

            case Phase.Recover:
                phaseTimer -= dt;
                if (phaseTimer <= 0f) EndSkill();
                break;
        }
    }

    private void TrackTargetVelocity(float dt)
    {
        if (Target == null || dt <= 0f)
        {
            hasLastTargetPos = false;
            targetVel = Vector2.zero;
            return;
        }

        Vector2 p = Target.position;
        if (hasLastTargetPos)
        {
            Vector2 raw = (p - lastTargetPos) / dt;
            targetVel = Vector2.Lerp(targetVel, raw, 0.3f); // smooth out jitter
        }
        lastTargetPos = p;
        hasLastTargetPos = true;
    }

    /// <summary>
    /// Aims the rush. Blends the delayed Direction (DefineDirection) with the live,
    /// lead-predicted direction by aimAccuracy. Also stores the REAL distance for
    /// impact detection. False if there is no valid target.
    /// </summary>
    private bool AimAtTarget()
    {
        if (Target == null) return false;

        Vector2 pos = _t.position;
        Vector2 targetPos = Target.position;

        Vector2 toLive = targetPos + targetVel * leadSeconds - pos;
        float liveSqr = toLive.sqrMagnitude;
        if (liveSqr < 0.0001f) return false;

        Vector2 liveDir = toLive / Mathf.Sqrt(liveSqr);
        Vector2 dir = Direction != Vector2.zero
            ? Vector2.Lerp(Direction, liveDir, aimAccuracy)
            : liveDir;

        if (dir.sqrMagnitude < 0.0001f) dir = liveDir;

        chargeDir = dir.normalized;
        chargeSqrDist = (targetPos - pos).sqrMagnitude;
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
        if (!AimAtTarget())
        {
            EndSkill();
            return;
        }

        phaseTimer = chargeDuration;

        appliedCharge = chargeSpeedBonus;
        attribute.SPEED_Ampl.FlatBonus += appliedCharge;

        SetPhase(Phase.Charge);
    }

    /// <summary>
    /// Area slam around the boss. Unlike a thin ray it can't slip past a target
    /// that is slightly off-line; only invincibility (the player's dash) avoids it.
    /// </summary>
    private void ChargeStrike()
    {
        if (package == null || attribute == null || attribute.IsDead) return;

        PlayAttackSound();
        if (anim != null) anim.SetTrigger(ChargeStrikeHash);

        float radius = BasicRange * slamRadiusMultiplier;
        float damage = attribute.ATK_Current * chargeDamageMultiplier;
        Vector2 center = _t.position;

        int count = Physics2D.OverlapCircleNonAlloc(center, radius, slamBuffer, hb.enemyMask);
        slamHit.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider2D col = slamBuffer[i];
            if (col == null) continue;
            if (!col.TryGetComponent(out HurtBox hurt)) continue;

            UnitAttribute victim = hurt.Access()?.attribute;
            if (victim == null || victim == attribute || victim.IsDead) continue;
            if (victim.tag != UnitTag.Friendly) continue;
            if (slamHit.Contains(victim)) continue; // one hit per unit, even with several colliders
            slamHit.Add(victim);

            Combat.DealDamage(damage, out float finalDamage, attribute, victim);

            HandleHitEffect(new AttackResult
            {
                DidHit = true,
                IsCrit = false,
                Target = col.gameObject,
                HitPoint = col.ClosestPoint(center),
                HitUnit = victim,
                Damage = finalDamage
            });
        }
    }

    /// <summary>Rush is over: chain into another charge, or stand still for a random time.</summary>
    private void FinishCharge()
    {
        RemoveChargeSpeed();
        chargeCount++;

        bool canChain = chargeCount < maxCharges
                        && Target != null
                        && RNG.GetPercent() < chainChance;

        if (canChain)
        {
            phaseTimer = chainBackStepDuration;
            SetPhase(Phase.BackStep);
            return;
        }

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
        chargeCount = 0;
        hasLastTargetPos = false;
        targetVel = Vector2.zero;
        SetPhase(Phase.Normal);
    }

    private void SetPhase(Phase p)
    {
        phase = p;
        if (anim == null) return;
        anim.SetBool(BackStepHash, p == Phase.BackStep);
        anim.SetBool(ChargingHash, p == Phase.Charge);
    }

    protected override void HandleHitEffect(AttackResult result)
    {
        base.HandleHitEffect(result);
        if(result.Damage > 0) CamManager.instance.Shake(1.5f);
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
                // Standing still after the chain.
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

    // Same logic as Zombrain.PerformAttack, but the IsAttacking bool is
    // handled in UpdateAnimator (see below).
    protected override void PerformAttack()
    {
        if (package == null) return;

        PlayAttackSound();

        if (RNG.GetPercent() < package.BiteAccuracy)
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