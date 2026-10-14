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

    /// <summary>True while the current target is an NPC.</summary>
    public bool IsChasingNpc => targetNpc != null;

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

    protected float lastHPBonus, lastATKBonus, lastDEFBonus, lastAPBonus, lastSPEEDBonus;
    protected float lastHPP, lastATKK;
    protected Vector3 lastScale, lastCachedScaled;

    private float appliedChaseBonus;

    private float _now;
    private float _sqrSight;

    protected Transform _t;
    #endregion

    #region Hit Stagger
    [Header("Hit Stagger")]
    private const float HitStaggerSlowPercent = -0.4f;
    private const float HitStaggerDuration = 1.5f;

    private float hitStaggerTimer;
    #endregion

    #region Hit Target Sound
    [Header("Hit Target Sound")]
    [Tooltip("Played when this zombie has landed more than 'Hits Before Sound' hits on its current target.")]
    [SerializeField] protected AudioClip hitTargetSound;
    [Tooltip("Sound plays on the hit AFTER this many hits (3 = plays on the 4th hit).")]
    [SerializeField] protected int hitsBeforeSound = 3;

    private int targetHitCount;
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
        targetNpc = null;
        targetHitCount = 0;

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

        float diffScale = diff * (1 + Mathf.Log(diff) * 0.35f);

        bool isAlpha = RNG.GetPercent() < 0.1f;

        lastHPBonus = RNG.GetInt(250, 750) * diffScale;
        lastHPP = 0.6f * diffScale;

        lastATKBonus = RNG.GetInt(10, 30) * diffScale;
        lastATKK = 0.20f * diffScale;

        lastAPBonus = RNG.GetPercent() * Mathf.Clamp01(diffScale);
        lastDEFBonus = RNG.GetInt(150, 600) * Mathf.Clamp01(diffScale);
        lastSPEEDBonus = RNG.GetInt(3, 6) * Mathf.Clamp01(diffScale);
        lastScale = RNG.GetVector2(0, 0.5f) * Mathf.Clamp01(diffScale);

        if (isAlpha)
        {
            float alphaMul = RNG.GetFloat(2f, 4f) * Mathf.Clamp01(diffScale);
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
        attribute.ATK_Ampl.TotalBonus += lastATKK;
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
        attribute.ATK_Ampl.TotalBonus -= lastATKK;
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
        targetNpc = null;
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
        targetNpc = null;
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

        // Catch-up boost when far away, plus a small extra push while hunting an NPC
        // so zombies can actually run down a fleeing NPC.
        float farDistance = pm != null && pm.attribute != null ? pm.attribute.SIGHT_Current * 3f : float.MaxValue;
        float desired = DistanceToTarget > farDistance ? 60f : 0f;
        if (targetNpc != null) desired += npcChaseSpeedBonus;
        SpeedHelper = desired;

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
            // Warn the NPC just before the bite lands so it can dodge.
            if (targetNpc != null) targetNpc.NotifyIncomingAttack(_t.position);

            AttackResult rs = Attack.Shoot(gameObject, attribute, attribute.SIGHT_Current, Direction, hb.enemyMask);
            HandleHitEffect(rs);
            CountTargetHit(rs);
        }

        if (anim != null) anim.SetBool(AttackHash, CanAttack && isAttacking);

        attackStagger = _now + RNG.GetFloat(attribute.ASPD_Current, 2f);
    }

    /// <summary>Counts landed hits on the current target; plays hitTargetSound once it goes over the threshold.</summary>
    protected virtual void CountTargetHit(AttackResult result)
    {
        if (!result.DidHit) return;

        targetHitCount++;
        if (targetHitCount <= hitsBeforeSound) return;

        targetHitCount = 0;
        PlayHitTargetSound();
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

        DamagePopupManager.Show(result.HitPoint, result.Damage, Color.yellow);
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
    [Tooltip("Radius used to look for friendlies (NPCs, player...) with physics.")]
    [SerializeField] protected float preySearchRadius = 15f;
    [Tooltip("Switch target only if the new one is at least this much closer (0.8 = 20% closer). Prevents flip-flopping.")]
    [SerializeField, Range(0.1f, 1f)] protected float retargetThreshold = 0.8f;

    [Header("NPC Chase")]
    [Tooltip("NPCs farther than this are ignored. 0 = unlimited. The nearest target wins, NPC or player.")]
    [SerializeField] protected float npcChaseRadius = 0f;
    [Tooltip("Small flat speed bonus while chasing an NPC, so fleeing NPCs can be caught. 0 = off.")]
    [SerializeField] protected float npcChaseSpeedBonus = 1.5f;

    private Collider2D[] preyBuffer = new Collider2D[8];
    private NpcBrain targetNpc;

    protected virtual void FindPrey()
    {
        // Drop dead / destroyed / pooled-away targets immediately
        if (Target != null && !Target.gameObject.activeInHierarchy)
            ClearTarget();
        if (targetNpc != null && (!targetNpc.IsActive || targetNpc.attribute == null || targetNpc.attribute.IsDead))
            ClearTarget();

        if (_now < findPreyInterval) return;

        // Re-scan less often when we already have a target
        findPreyInterval = _now + (Target == null
            ? RNG.GetFloat(0.3f, 1f)
            : RNG.GetFloat(0.5f, 1.2f));

        Transform best = SearchForPrey(out NpcBrain bestNpc);
        if (best == null || best == Target) return;

        if (Target != null)
        {
            // Only switch if the new one is clearly closer than the current one
            float curSqr = ((Vector2)Target.position - (Vector2)_t.position).sqrMagnitude;
            float newSqr = ((Vector2)best.position - (Vector2)_t.position).sqrMagnitude;
            if (newSqr > curSqr * retargetThreshold * retargetThreshold) return;
        }

        Target = best;
        targetNpc = bestNpc;
        targetHitCount = 0;
        rotateInterval = 0f; // force direction/distance refresh right away
    }

    private void ClearTarget()
    {
        Target = null;
        targetNpc = null;
        targetHitCount = 0;
    }

    /// <summary>
    /// Nearest valid target overall: NPCs (registry), other friendlies (physics),
    /// and the player. No priority, whoever is closest wins.
    /// </summary>
    protected virtual Transform SearchForPrey(out NpcBrain npc)
    {
        npc = null;

        Vector2 pos = _t.position;
        float bestSqr = float.MaxValue;
        Transform best = null;
        NpcBrain bestNpc = null;

        // 1) NPC registry (no physics, no radius limit unless npcChaseRadius is set).
        float npcLimitSqr = npcChaseRadius > 0f ? npcChaseRadius * npcChaseRadius : float.MaxValue;
        var list = NpcBrain.Active;
        for (int i = 0; i < list.Count; i++)
        {
            NpcBrain n = list[i];
            if (n == null || !n.IsActive) continue;

            UnitAttribute ua = n.attribute;
            if (ua == null || ua.IsDead) continue;
            if (!n.gameObject.activeInHierarchy) continue;

            float sqr = ((Vector2)n.transform.position - pos).sqrMagnitude;
            if (sqr > npcLimitSqr) continue;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = n.transform;
                bestNpc = n;
            }
        }

        // 2) Other friendlies nearby (physics).
        int count = Physics2D.OverlapCircleNonAlloc(pos, preySearchRadius, preyBuffer, hb.enemyMask);
        if (count >= preyBuffer.Length)
        {
            preyBuffer = new Collider2D[preyBuffer.Length * 2];
            count = Physics2D.OverlapCircleNonAlloc(pos, preySearchRadius, preyBuffer, hb.enemyMask);
        }

        int ownLayer = gameObject.layer;
        for (int i = 0; i < count; i++)
        {
            Collider2D col = preyBuffer[i];
            if (col == null) continue;
            if (col.gameObject.layer == ownLayer) continue;

            if (!col.TryGetComponent(out HurtBox hurtBox)) continue;

            UnitPackage pkg = hurtBox.Access();
            if (pkg?.attribute == null) continue;
            if (pkg.attribute.tag != UnitTag.Friendly) continue;
            if (pkg.attribute.IsDead) continue;

            float sqr = ((Vector2)col.transform.position - pos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = col.transform;
                col.TryGetComponent(out bestNpc); // null if it is not an NPC
            }
        }

        // 3) The player, wherever he is.
        if (pm != null && pm.Controlling != null)
        {
            float sqr = ((Vector2)pm.Controlling.transform.position - pos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = pm.Controlling.transform;
                bestNpc = null;
            }
        }

        npc = bestNpc;
        return best;
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

    protected virtual void PlayHitTargetSound()
    {
        if (AudioManager.instance == null || hitTargetSound == null) return;
        AudioManager.instance.PlayAudio(hitTargetSound, _t.position);
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