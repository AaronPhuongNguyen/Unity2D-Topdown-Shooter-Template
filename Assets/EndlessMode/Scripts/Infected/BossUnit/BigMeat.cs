using Server;
using UnityEngine;

/// <summary>
/// First boss: a huge, tanky brute.
///
/// Motions:
///   1. Walk       - chases the target (inherited Zombrain behaviour).
///   2. Attack     - basic bite at normal range (attribute.SIGHT_Current).
///   3. Charge     - steps backward for 2s, then charges rapidly along the
///                   locked direction for 0.5s and slams on arrival. 3s cooldown.
///
/// Two attack ranges:
///   - Basic range  = SIGHT_Current
///   - Charge range = SIGHT_Current * chargeRangeMultiplier (default 3x).
///     Used both to decide when the charge may start and as the reach of the
///     charge slam.
///
/// Animator parameters (add to the controller):
///   bool    IsWalking      (existing; forced false during the charge skill)
///   bool    IsAttacking    (existing)
///   bool    IsBackStepping (new) - the 2s retreat wind-up
///   bool    IsCharging     (new) - the 0.5s rush
///   trigger ChargeStrike   (new) - fired once when the slam lands
/// </summary>
public class BigMeat : Zombrain
{
    private enum Phase { Normal, BackStep, Charge }

    [Header("BigMeat - Charge Skill")]
    [Tooltip("Seconds between the end of a charge and the next wind-up.")]
    [SerializeField] private float chargeCooldown = 3f;
    [Tooltip("How long it steps backward before charging.")]
    [SerializeField] private float backStepDuration = 2f;
    [Tooltip("How long the rush lasts.")]
    [SerializeField] private float chargeDuration = 0.5f;
    [Tooltip("Flat speed added during the rush.")]
    [SerializeField] private float chargeSpeedBonus = 60f;
    [Tooltip("Charge slam reach = basic range * this.")]
    [SerializeField] private float chargeRangeMultiplier = 3f;
    [Tooltip("Slam damage = ATK * this.")]
    [SerializeField] private float chargeDamageMultiplier = 2f;

    private static readonly int WalkHash = Animator.StringToHash("IsWalking");
    private static readonly int BackStepHash = Animator.StringToHash("IsBackStepping");
    private static readonly int ChargingHash = Animator.StringToHash("IsCharging");
    private static readonly int ChargeStrikeHash = Animator.StringToHash("ChargeStrike");

    private Phase phase = Phase.Normal;
    private float phaseTimer;
    private float cooldownTimer;
    private float appliedCharge;
    private Vector2 chargeDir;

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

        ResetSkill();
        cooldownTimer = chargeCooldown;
        return true;
    }

    public override void Tick(float dt)
    {
        UpdatePhase(dt);
        base.Tick(dt);
    }

    protected override void Despawn()
    {
        bool wasActive = isActive;
        ResetSkill();
        base.Despawn();

        if (wasActive && BossManager.instance != null)
            BossManager.instance.NotifyBossKilled(gameObject);
    }

    public override void Reboot()
    {
        ResetSkill();
        base.Reboot();
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
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                {
                    ChargeStrike();
                    EndSkill();
                }
                break;
        }
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
        if (Direction == Vector2.zero)
        {
            EndSkill();
            return;
        }

        // Lock the rush direction at the end of the wind-up.
        chargeDir = Direction;
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

        // Slam reaches 3x the basic range, piercing everything in line.
        AttackResult[] hits = Attack.ShootPierce(
            gameObject,
            attribute.ATK_Current * chargeDamageMultiplier,
            attribute,
            ChargeRange,
            chargeDir,
            hb.enemyMask);

        for (int i = 0; i < hits.Length; i++)
            HandleHitEffect(hits[i]);
    }

    /// <summary>Back to normal behaviour and start the cooldown.</summary>
    private void EndSkill()
    {
        ResetSkill();
        cooldownTimer = chargeCooldown;
    }

    /// <summary>Clears skill state and any applied speed bonus. Safe to call anytime.</summary>
    private void ResetSkill()
    {
        if (appliedCharge != 0f && attribute != null)
            attribute.SPEED_Ampl.FlatBonus -= appliedCharge;
        appliedCharge = 0f;

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
                UnitMotion.MoveThisObject(attribute, gameObject, chargeDir);
                break;
        }
    }

    // Face the target while backing away, but stay locked while charging.
    protected override void Rotate()
    {
        if (phase == Phase.Charge) return;
        base.Rotate();
    }

    // No basic bites during the skill; the slam is the attack there.
    protected override void TryAttack()
    {
        if (phase != Phase.Normal) return;
        base.TryAttack();
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (anim != null && phase != Phase.Normal)
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