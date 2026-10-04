using Server;
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

    protected bool isActive;

    [Header("Separation")]
    [SerializeField] protected float separationRadius = 1f;
    [SerializeField] protected float separationStrength = 1.5f;
    protected float separationInterval;
    protected Vector2 cachedSeparation;

    private static readonly int AttackHash = Animator.StringToHash("IsAttacking");
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    private float lastHPBonus, lastATKBonus, lastDEFBonus, lastAPBonus, lastSPEEDBonus;
    private float lastHPP, lastATKK;
    private Vector3 lastScale, lastCachedScaled;

    private float appliedChaseBonus;

    private float _now;
    private float _sqrSight;

    protected Transform _t;
    #endregion

    #region Hit Stagger
    [Header("Hit Stagger")]
    private const float HitStaggerSlowPercent = -0.25f;
    private const float HitStaggerDuration = 1.5f;

    private float hitStaggerTimer;
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
        TickHitStagger(dt);
    }

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
        attribute.tag = UnitTag.Aggressive;
        isActive = true;

        package.attribute = attribute;

        GetBonus();
        ApplyBonus();

        Target = null;

        rotateInterval = 0f;
        attackInterval = 0f;
        attackStagger = 0f;
        findPreyInterval = 0f;
        separationInterval = 0f;
        _sqrSight = attribute.SIGHT_Current * attribute.SIGHT_Current;
        appliedChaseBonus = 0f;
        SpeedHelper = 0f;

        hitStaggerTimer = 0f;

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
        ce?.SpawnCorpse(_t.position, lastCachedScaled, Direction);

        if (dm != null) dm.HandleKill(package);

        RemoveBonus();
        RemoveHitStagger();
        CleanUpAndReturnToHive();
    }
    protected virtual void GetBonus()
    {
        float diff = dm.CurrentDifficulty;

        float diffScale = diff * (1f + Mathf.Log(diff + 1f) * 0.3f);

        bool isAlpha = RNG.GetPercent() < 0.1f;

        lastHPBonus = RNG.GetInt(200, 600) * diffScale;
        lastHPP = 0.8f * diffScale;

        lastATKBonus = RNG.GetInt(10, 30) * diffScale;
        lastATKK = 0.2f * diffScale;

        lastAPBonus = RNG.GetPercent() * Mathf.Clamp01(diffScale);
        lastDEFBonus = RNG.GetInt(200, 600) * Mathf.Clamp01(diffScale);
        lastSPEEDBonus = RNG.GetInt(3, 6) * Mathf.Clamp01(diffScale);
        lastScale = RNG.GetVector2(0, 0.3f) * Mathf.Clamp01(diffScale);

        if (isAlpha)
        {
            float alphaMul = RNG.GetFloat(1.5f, 3f) * Mathf.Clamp01(diffScale);
            lastHPBonus *= alphaMul;
            lastATKBonus *= alphaMul;
            lastDEFBonus *= alphaMul;
            lastSPEEDBonus *= alphaMul;
            lastAPBonus *= alphaMul;
            lastScale *= alphaMul;
        }
    }
    protected virtual void ApplyBonus()
    {
        attribute.HP_Ampl.FlatBonus += lastHPBonus;
        attribute.HP_Ampl.TotalBonus += lastHPP;
        attribute.ATK_Ampl.FlatBonus += lastATKBonus;
        attribute.HP_Ampl.TotalBonus += lastATKK;
        attribute.DEF_Ampl.FlatBonus += lastDEFBonus;
        attribute.SPEED_Ampl.FlatBonus += lastSPEEDBonus;
        attribute.ArmourPenetration_Ampl.FlatBonus += lastAPBonus;

        lastCachedScaled = transform.localScale;
        transform.localScale += lastScale;
    }
    protected virtual void RemoveBonus()
    {
        attribute.HP_Ampl.FlatBonus -= lastHPBonus;
        attribute.HP_Ampl.TotalBonus -= lastHPP;
        attribute.ATK_Ampl.FlatBonus -= lastATKBonus;
        attribute.HP_Ampl.TotalBonus -= lastATKK;
        attribute.DEF_Ampl.FlatBonus -= lastDEFBonus;
        attribute.SPEED_Ampl.FlatBonus -= lastSPEEDBonus;
        attribute.ArmourPenetration_Ampl.FlatBonus -= lastAPBonus;

        transform.localScale -= lastScale;

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
        SpeedHelper = (DistanceToTarget > pm.attribute.SIGHT_Current * 3f) ? 60f : 0f;
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
        {
            AttackResult rs = Attack.Shoot(gameObject, attribute, attribute.SIGHT_Current, Direction, hb.enemyMask);
            HandleHitEffect(rs);
        }

        if (anim != null) anim.SetBool(AttackHash, CanAttack && isAttacking);

        attackStagger = _now + RNG.GetFloat(attribute.ASPD_Current, 2f);
    }

    protected virtual void OnHit(float damage)
    {
        ApplyHitStagger();
    }
    protected virtual void HandleHitEffect(AttackResult result)
    {
        if (!result.DidHit) return;
        if (package.Media.HitEffect == null) return;
        GameObject o = PoolingSystem.instance.GetFromPool(package.Media.HitEffect);
        if (o == null) return;
        o.transform.position = result.HitPoint;

        DamagePopupManager.Show(result.HitPoint, result.Damage, Color.red);
        PlayHitSound();
    }
    #endregion

    #region Hit Stagger
    bool staggerApplied = false;
    protected virtual void ApplyHitStagger()
    {
        if (attribute == null || attribute.IsDead) return;
        hitStaggerTimer = HitStaggerDuration;
        if (staggerApplied) return;

        attribute.SPEED_Ampl.TotalBonus += HitStaggerSlowPercent;
        staggerApplied = true;
    }
    private void TickHitStagger(float delta)
    {
        if (hitStaggerTimer > 0) hitStaggerTimer -= delta;
        else if (hitStaggerTimer <= 0 && staggerApplied) RemoveHitStagger();
    }

    private void RemoveHitStagger()
    {
        if (!staggerApplied) return;
        if (attribute != null)
            attribute.SPEED_Ampl.TotalBonus -= HitStaggerSlowPercent;
        hitStaggerTimer = 0f;
        staggerApplied = false;
    }
    #endregion

    #region Sense
    [Header("Prey Search")]
    [SerializeField] protected float preySearchRadius = 15f;

    private Collider2D[] preyBuffer = new Collider2D[8];

    protected virtual void FindPrey()
    {
        if (Target != null) return;
        if (_now < findPreyInterval) return;
        findPreyInterval = _now + RNG.GetFloat(0.3f, 1f);

        Target = SearchForPrey();
    }

    protected virtual Transform SearchForPrey()
    {
        int count = Physics2D.OverlapCircleNonAlloc(_t.position, preySearchRadius, preyBuffer, hb.enemyMask);

        if (count >= preyBuffer.Length)
        {
            preyBuffer = new Collider2D[preyBuffer.Length * 2];
            return SearchForPrey();
        }

        int ownLayer = gameObject.layer;
        float nearestSqr = float.MaxValue;
        Transform nearest = null;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = preyBuffer[i];
            if (col == null) continue;
            if (col.gameObject.layer == ownLayer) continue;

            if (!col.TryGetComponent(out HurtBox hurtBox)) continue;

            UnitPackage package = hurtBox.Access();
            if (package?.attribute == null) continue;
            if (package.attribute.tag != UnitTag.Friendly) continue;

            Vector2 delta = (Vector2)col.transform.position - (Vector2)_t.position;
            float distSqr = delta.sqrMagnitude;

            if (distSqr < nearestSqr)
            {
                nearestSqr = distSqr;
                nearest = col.transform;
            }
        }
        if (nearest == null && pm.Controlling != null) nearest = pm.Controlling.transform;

        return nearest;
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
        PlayHitSound();
        PlayMiscSound();
        AudioManager.instance.PlayAudio(package.Media.Audio.GetDeathSound(), _t.position);
    }

    protected virtual void PlayMiscSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayAudio(package.Media.Audio.GetMiscSound(), _t.position);
    }
    #endregion
}