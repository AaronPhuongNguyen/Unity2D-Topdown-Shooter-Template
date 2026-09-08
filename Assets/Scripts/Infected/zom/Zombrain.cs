using Server;
using System;
using UnityEngine;

public class Zombrain : HurtBox, ITick
{
    public virtual HiveBrain hb => HiveBrain.instance;
    public virtual DomainManager dm => DomainManager.instance;

    #region Properties
    public ZomPackage package;
    public UnitAttribute attribute;
    public override UnitPackage Access() => package;

    public Animator anim;
    public Transform Target;
    public Vector2 Direction;

    public bool CanAttack = true;
    public bool CanMove = true;
    public bool isAttacking => attackStagger > _now;
    public bool isMoving => Target != null && Direction != Vector2.zero;

    [SerializeField] protected float DistanceToTarget;
    [SerializeField] protected float SpeedHelper;
    #endregion

    #region Cache
    protected CorpseEmitter ce;
    protected Rigidbody2D rb;
    protected float rotateInterval;
    protected float attackInterval;
    protected float attackStagger;
    protected float findPreyInterval;
    protected PlayerManager pm => PlayerManager.instance;

    // True while this instance is spawned/active and subscribed to events.
    // Prevents Despawn()/Reboot() from running their cleanup twice on the
    // same object (which was causing HiveBrain to fail removing an already
    // -removed Zombrain after a restart).
    protected bool isActive;

    [Header("Separation")]
    [SerializeField] protected float separationRadius = 1f;
    [SerializeField] protected float separationStrength = 1.5f;
    protected float separationInterval;
    protected Vector2 cachedSeparation;

    private static readonly int AttackHash = Animator.StringToHash("IsAttacking");
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    private float lastHPBonus, lastATKBonus, lastDEFBonus, lastAPBonus, lastSPEEDBonus;

    // Tracks the chase-speed boost separately from lastSPEEDBonus (the
    // spawn-time random bonus) - Move() adds/removes only its own delta
    // instead of overwriting FlatBonus wholesale, which used to erase
    // lastSPEEDBonus's contribution.
    private float appliedChaseBonus;

    // --- Perf caches -----------------------------------------------------
    // Cached once per Tick() so we don't hit Time.time (a property call)
    // repeatedly across the same frame's worth of sub-methods.
    private float _now;

    // Cached squared sight radius, refreshed only when rotateInterval
    // refreshes (every 0.1-0.3s) instead of every single TryAttack() call.
    // Lets TargetInRange() avoid a sqrt via Vector2.Distance.
    private float _sqrSight;

    // Cached transform to avoid repeated native transform property hops.
    protected Transform _t;
    #endregion

    #region Lifecycle
    protected virtual void Awake()
    {
        _t = transform;
    }

    public virtual void Tick(float dt)
    {
        _now = Time.time;

        FindPrey();
        DefineDirection();
        Move();
        TryAttack();
        UpdateAnimator();
    }

    /// <returns>True if this instance was newly spawned/counted; false if it
    /// was rejected (bad package) or was already active. HiveBrain uses this
    /// to decide whether to start tracking the object in its `zoms` list -
    /// tracking it after a failed/duplicate spawn would let it be ticked
    /// and eventually despawned without ever having been counted.</returns>
    public virtual bool SpawnObject(ZomPackage zp)
    {
        if (!CanSpawn(zp)) return false;
        if (isActive)
        {
            Debug.LogWarning($"[Zombrain] SpawnObject blocked on {gameObject.name} - isActive was already true (this instance was never rebooted/despawned).");
            return false;
        }

        SetRigidBody();

        if (package == null) package = Instantiate(zp);

        attribute = package.attribute.Clone();
        attribute.Reset();
        attribute.OnDeath += Despawn;
        attribute.OnTakeDamage += OnHit;
        isActive = true;

        package.attribute = attribute;

        GetBonus();
        ApplyBonus();

        Target = pm.Controlling == null ? null : pm.Controlling.transform;

        // Reset perf caches on respawn so pooled instances don't reuse stale state.
        rotateInterval = 0f;
        attackInterval = 0f;
        attackStagger = 0f;
        findPreyInterval = 0f;
        separationInterval = 0f;
        _sqrSight = attribute.SIGHT_Current * attribute.SIGHT_Current;
        appliedChaseBonus = 0f;
        SpeedHelper = 0f;

        if (dm != null) dm.HandleSpawn(package);
        if (ce == null && gameObject.TryGetComponent(out CorpseEmitter cet))
        {
            ce = cet;
            ce.CreateEmitter(this);
        }

        return true;
    }

    protected virtual void Despawn()
    {
        if (!Unsubscribe()) return;

        PlayDeathSound();
        ce?.SpawnCorpse(_t.position, Direction);

        if (dm != null) dm.HandleKill(package);

        RemoveBonus();
        CleanUpAndReturnToHive();
    }
    protected virtual void GetBonus()
    {
        if (RNG.GetPercent() < 0.25f) lastHPBonus = Mathf.Pow(dm.CurrentDifficulty, 2.75f);
        else lastHPBonus = Mathf.Pow(dm.CurrentDifficulty, 2.35f);

        if (RNG.GetPercent() < 0.25f) lastATKBonus = Mathf.Pow(dm.CurrentDifficulty, 2.0f);
        else lastATKBonus = Mathf.Pow(dm.CurrentDifficulty, 1.8f);

        lastAPBonus = RNG.GetPercent();

        lastDEFBonus = 500 * RNG.GetPercent();

        lastSPEEDBonus = RNG.GetPercent() < 0.25f
    ? 5f * RNG.GetInt(-1, 4)
        : 2.5f * RNG.GetInt(-2, 3);
        lastSPEEDBonus = Mathf.Min(lastSPEEDBonus, pm.attribute.SPEED_Current + 3f);
    }
    protected virtual void ApplyBonus()
    {
        attribute.HP_Ampl.TotalBonus += lastHPBonus;
        attribute.ATK_Ampl.TotalBonus += lastATKBonus;
        attribute.DEF_Ampl.FlatBonus += lastDEFBonus;
        attribute.SPEED_Ampl.FlatBonus += lastSPEEDBonus;
        attribute.ArmourPenetration_Perc += lastAPBonus;
    }
    protected virtual void RemoveBonus()
    {
        attribute.HP_Ampl.TotalBonus -= lastHPBonus;
        attribute.ATK_Ampl.TotalBonus -= lastATKBonus;
        attribute.DEF_Ampl.FlatBonus -= lastDEFBonus;
        attribute.SPEED_Ampl.FlatBonus -= lastSPEEDBonus;
        attribute.ArmourPenetration_Perc -= lastAPBonus;

        if (appliedChaseBonus != 0f)
        {
            attribute.SPEED_Ampl.FlatBonus -= appliedChaseBonus;
            appliedChaseBonus = 0f;
        }
    }

    public virtual void Reboot()
    {
        if (!Unsubscribe()) return;

        Target = null;
        package = null;
        attribute = null;
        ce = null;
    }

    private bool Unsubscribe()
    {
        if (!isActive) return false;
        isActive = false;

        attribute.OnDeath -= Despawn;
        attribute.OnTakeDamage -= OnHit;
        return true;
    }

    private void CleanUpAndReturnToHive()
    {
        Target = null;
        package = null;
        attribute = null;
        ce = null;

        hb.DespawnZom(gameObject);
    }
    #endregion

    #region Setup
    protected virtual bool CanSpawn(ZomPackage zp)
    {
        if (zp == null)
        {
            Debug.LogWarning("No Package available, check before spawning this Zom");
            return false;
        }
        if (zp.attribute == null)
        {
            Debug.LogWarning("No Valid Attribute, check");
            return false;
        }
        return true;
    }

    protected virtual void SetRigidBody()
    {
        if (!TryGetComponent(out rb)) rb = gameObject.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.constraints = RigidbodyConstraints2D.FreezePosition;
        rb.freezeRotation = true;
        if (anim != null)
            anim.cullingMode = AnimatorCullingMode.CullCompletely;
    }
    #endregion

    #region Movement
    protected virtual void DefineDirection()
    {
        if (Target == null) return;
        if (_now < rotateInterval) return;

        Direction = ((Vector2)Target.position - (Vector2)_t.position).normalized;
        Rotate();
        DefineDistance();

        rotateInterval = _now + RNG.GetFloat(0.1f, 0.3f);
        _sqrSight = attribute.SIGHT_Current * attribute.SIGHT_Current;
    }

    protected virtual void DefineDistance()
    {
        if (Target == null) return;
        Vector2 delta = (Vector2)Target.position - (Vector2)_t.position;
        DistanceToTarget = Mathf.Sqrt(delta.sqrMagnitude);
    }

    protected virtual void Rotate()
    {
        if (Direction == Vector2.zero) return;
        if (isAttacking) return;

        UnitMotion.RotateThisObject(gameObject, Direction);
    }

    protected virtual void Move()
    {
        if (!CanMove) return;
        if (Target == null) return;
        if (Direction == Vector2.zero) return;
        if (isAttacking) return;

        if (_now >= separationInterval)
        {
            cachedSeparation = UnitMotion.GetSeparationForce(
                _t.position,
                hb.GetNearbyZoms(this, separationRadius),
                separationRadius,
                separationStrength
            );
            separationInterval = _now + 0.2f;
        }
        SpeedHelper = (DistanceToTarget > pm.attribute.SIGHT_Current * 2f) ? 50f : 0f;
        if (!Mathf.Approximately(SpeedHelper, appliedChaseBonus))
        {
            attribute.SPEED_Ampl.FlatBonus += SpeedHelper - appliedChaseBonus;
            appliedChaseBonus = SpeedHelper;
        }

        Vector2 finalDir = (Direction + cachedSeparation).normalized;
        UnitMotion.MoveThisObject(attribute, gameObject, finalDir);
    }

    protected virtual void UpdateAnimator()
    {
        if (anim == null) return;
        anim.SetBool(IsWalkingHash, CanMove && isMoving && !isAttacking);
    }
    #endregion

    #region Combat
    protected virtual bool TargetInRange()
    {
        return DistanceToTarget * DistanceToTarget <= _sqrSight;
    }

    protected virtual void TryAttack()
    {
        if (Target == null) return;
        if (!CanAttack) return;
        if (_now < attackInterval) return;

        attackInterval = _now + RNG.GetFloat(attribute.ASPD_Current, 1f);

        if (_now < attackStagger) return;
        if (!TargetInRange()) return;

        PerformAttack();
    }

    protected virtual void PerformAttack()
    {
        if (package == null) return;

        PlayAttackSound();

        if (RNG.GetPercent() < package.BiteAccuracy)
            Attack.Shoot(gameObject, attribute, attribute.SIGHT_Current, Direction, hb.enemyMask);

        if (anim != null) anim.SetBool(AttackHash, CanAttack && isAttacking);

        attackStagger = _now + RNG.GetFloat(attribute.ASPD_Current, 2f);
    }

    protected virtual void OnHit(float damage)
    {
        PlayHitSound();
    }
    #endregion

    #region Sense
    protected virtual void FindPrey()
    {
        if (Target != null) return;
        if (_now < findPreyInterval) return;
        findPreyInterval = _now + RNG.GetFloat(0.3f, 1f);

        if (pm.Controlling == null) return;
        Target = pm.Controlling.transform;
    }

    protected virtual void PlayAttackSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetAttackSound(), _t.position);
    }

    protected virtual void PlayHitSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetHitSound(), _t.position);
    }

    protected virtual void PlayDeathSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetDeathSound(), _t.position);
    }

    protected virtual void PlayMiscSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetMiscSound(), _t.position);
    }
    #endregion
}