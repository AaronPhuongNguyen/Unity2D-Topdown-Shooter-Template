using Server;
using UnityEngine;

[DefaultExecutionOrder(-5)]
public class PlayerManager : MonoBehaviour, ITick
{
    public bool DebugMode;
    public bool AutoAttack = false;
    public PlayerPackage package;
    public UnitAttribute attribute => clonedPack?.attribute;

    #region Inspector Debugger
    public LayerMask EnemyMask;
    public GameObject IndicatorPrefab;
    public float CurrentHP;
    #endregion

    #region Runtime Status
    [HideInInspector] public Transform Target;

    [HideInInspector] public bool ManualAttacking;
    [HideInInspector] public Vector2 ManualAttackDirection;

    [HideInInspector] public Vector2 MoveInput;
    [HideInInspector] public Vector2 Direction;
    [HideInInspector] public Vector2 LastDeathAtSpot;
    [HideInInspector] public PlayerCombat pc;
    [HideInInspector] public PlayerPackage clonedPack;
    [HideInInspector] public GameObject AimIndicator;
    [HideInInspector] public GameObject Controlling;
    [HideInInspector] public RecoverOverTime rot;
    [HideInInspector] public MoneyOverTime mot;

    public bool IsAttacking => CurrentHP > 0 && (ManualAttacking || (AutoAttack && Target != null));
    public bool IsMoving => MoveInput != Vector2.zero && !IsAttacking && CurrentHP > 0;
    public bool IsIdle => !IsAttacking && !IsMoving && CurrentHP > 0;

    [HideInInspector] public float combatDuration;
    private CorpseEmitter ce;
    #endregion

    #region Attack Move Penalty
    // Continuous (not timed) - applied every tick IsAttacking is true,
    // removed the moment it's false. Tracked via a bool rather than a
    // timer/stack, since this isn't a DoT-style effect, just a state-bound
    // modifier that should always exactly match IsAttacking.
    private const float AttackMoveSlowPercent = -0.15f;
    private bool attackSlowApplied;
    #endregion

    #region Stagger
    [SerializeField, Range(0, 1)] private float StaggerChance = 0.2f;
    private const float StaggerSlowPercent = 0.3f;
    private const float StaggerDuration = 2f;

    private float staggerTimer;
    private float appliedStaggerMultiplier = 1f;
    #endregion

    #region Bleed
    [Header("Bleed")]
    [Tooltip("Chance on taking damage to apply/refresh a Bleed stack.")]
    [Range(0f, 1f)][SerializeField] private float bleedChance = 0.75f;

    private const float BleedDuration = 3f;
    private const float BleedFlatDamagePerStack = 5f;
    private const float BleedPercentMaxHPPerStack = 0.01f;
    private const int BleedMaxStacks = 20;

    private float bleedTimer;
    private float bleedTickInterval = 1f;
    private float bleedTickTimer;
    private int bleedStacks;
    #endregion

    #region Singleton
    public static PlayerManager instance { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }
    private void Start()
    {
        EventBus.RaiseGameStart();
    }

    private void OnEnable()
    {
        EventBus.OnGameStart += OnGameStarted;
        EventBus.OnGameStart += ResetRuntimeStatus;
        EventBus.OnGameRestart += Init;

        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        EventBus.OnGameStart -= OnGameStarted;
        EventBus.OnGameStart -= ResetRuntimeStatus;
        EventBus.OnGameRestart -= Init;

        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }
    #endregion

    #region Lifecycle
    public void Tick(float delta)
    {
        if (combatDuration > 0) combatDuration -= delta;
        if (Controlling == null) return;

        pc?.Tick(delta);
        NaturalHealing(delta);
        TickStagger(delta);
        TickBleed(delta);
        TickAttackMovePenalty();

        if (clonedPack != null && CurrentHP != attribute.HP_Current)
            CurrentHP = attribute.HP_Current;
    }
    #endregion

    #region Setup
    private bool isInitializing;

    public void Init()
    {
        if (isInitializing)
        {
            Debug.LogWarning("PlayerManager: Init() called re-entrantly - ignoring to avoid a stack overflow. Check what is calling Init/Respawn from inside a Player event handler.");
            return;
        }

        isInitializing = true;
        try
        {
            CreateCharacter();
            SetUpPlayer();
        }
        finally
        {
            isInitializing = false;
        }
    }

    private void CreateCharacter()
    {
        if (Controlling != null) PoolingSystem.instance.DestroyObject(Controlling);
        if (AimIndicator != null) PoolingSystem.instance.DestroyObject(AimIndicator);
        if (clonedPack != null) Destroy(clonedPack);

        clonedPack = Instantiate(package);

        Controlling = PoolingSystem.instance.GetFromPool(clonedPack.prefab);
        Controlling.name = clonedPack.packName;
    }

    private void SetUpPlayer()
    {
        Controlling.transform.position = LastDeathAtSpot;
        SetRigidBody();

        if (!Controlling.TryGetComponent(out pc))
            pc = Controlling.AddComponent<PlayerCombat>();
        if (!Controlling.TryGetComponent(out rot))
            rot = Controlling.AddComponent<RecoverOverTime>();
        if (!Controlling.TryGetComponent(out mot))
            mot = Controlling.AddComponent<MoneyOverTime>();

        if (!Controlling.TryGetComponent(out ce))
        {
            ce = Controlling.AddComponent<CorpseEmitter>();
            ce.CreateEmitter(pc);
        }

        if (IndicatorPrefab != null)
        {
            AimIndicator = PoolingSystem.instance.GetFromPool(IndicatorPrefab);
            AimIndicator.SetActive(false);
        }

        clonedPack.attribute = clonedPack.attribute.Clone();
        attribute.Reset();
        attribute.tag = UnitTag.Friendly;

        attribute.OnTakeDamage += OnDamageTaken;
        attribute.OnDeath += PlayDeathSound;
        attribute.OnDeath += AfterDeath;

        staggerTimer = 0f;
        appliedStaggerMultiplier = 1f;
        bleedTimer = 0f;
        bleedTickTimer = 0f;
        bleedStacks = 0;
        attackSlowApplied = false;

        EventBus.RaisePlayerRespawn();
    }

    private void SetRigidBody()
    {
        if (Controlling == null) return;
        if (!Controlling.TryGetComponent(out Rigidbody2D rb))
            rb = Controlling.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.constraints = RigidbodyConstraints2D.FreezePosition;
        rb.freezeRotation = true;
    }
    #endregion

    #region Handle Life
    [ContextMenu("Respawn")]
    public void Respawn() => Init();

    [ContextMenu("Instant Kill")]
    public void InstantKill() => attribute.TakeDamage(attribute.HP_Current);

    [ContextMenu("Set Game Started")]
    public void SetGameStarted() => EventBus.RaiseGameStart();

    public void SetCombat(float v)
    {
        combatDuration = v > 0 ? v : 0.2f; // default
    }

    private void AfterDeath()
    {
        Debug.Log("Death!");

        LastDeathAtSpot = Controlling.transform.position;
        ce.SpawnCorpse(LastDeathAtSpot, Controlling.transform.localScale);

        attribute.OnDeath -= AfterDeath;
        attribute.OnDeath -= PlayDeathSound;
        attribute.OnTakeDamage -= OnDamageTaken;

        RemoveStagger();
        RemoveAttackMovePenalty();
        bleedTimer = 0f;
        bleedStacks = 0;

        PoolingSystem.instance.DestroyObject(Controlling);

        EventBus.RaisePlayerDeath();
        EventBus.RaiseGameOver();
    }

    private float healingTimer;
    private void NaturalHealing(float delta)
    {
        healingTimer -= delta;
        if (healingTimer > 0f) return;
        healingTimer = 10f;

        if (combatDuration > 0) return;
        attribute.Heal(attribute.HP_Max * 0.025f);
    }

    public void PlayAttackSound() => PlaySound(package?.Media?.Audio?.GetAttackSound());
    public void PlayHitSound() => PlaySound(package?.Media?.Audio?.GetHitSound());
    public void PlayDeathSound() => PlaySound(package?.Media?.Audio?.GetDeathSound());
    public void PlayMiscSound() => PlaySound(package?.Media?.Audio?.GetMiscSound());

    private void PlaySound(AudioClip clip)
    {
        if (AudioManager.instance == null || clip == null || Controlling == null) return;
        AudioManager.instance.PlayAudio(clip, Controlling.transform.position);
    }

    private void PlayHitEffect()
    {
        if (Controlling == null) return;
        if (PoolingSystem.instance == null) return;

        GameObject prefab = clonedPack?.Media?.HitEffect;
        if (prefab == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(prefab);
        if (o == null) return;
        o.transform.position = Controlling.transform.position;
    }
    #endregion

    #region Status Effects (on hit)
    private void OnDamageTaken(float damage)
    {
        if (attribute.IsDead) return;

        SetCombat(0.5f);
        PlayHitEffect();

        if (RNG.GetPercent() <= StaggerChance)
            ApplyStagger();

        if (RNG.GetPercent() <= bleedChance)
            ApplyBleed();
    }
    #endregion

    #region Attack Move Penalty
    private void TickAttackMovePenalty()
    {
        bool shouldApply = IsAttacking;

        if (shouldApply == attackSlowApplied) return;

        if (shouldApply)
            ApplyAttackMovePenalty();
        else
            RemoveAttackMovePenalty();
    }

    private void ApplyAttackMovePenalty()
    {
        if (attackSlowApplied) return;

        attribute.SPEED_Ampl.TotalBonus += AttackMoveSlowPercent;
        attackSlowApplied = true;
    }

    private void RemoveAttackMovePenalty()
    {
        if (!attackSlowApplied) return;
        if (attribute != null)
            attribute.SPEED_Ampl.TotalBonus -= AttackMoveSlowPercent;

        attackSlowApplied = false;
    }
    #endregion

    #region Stagger
    private void ApplyStagger()
    {
        float newMultiplier = 1f - StaggerSlowPercent;

        attribute.SPEED_Ampl.TotalBonus /= appliedStaggerMultiplier;
        attribute.SPEED_Ampl.TotalBonus *= newMultiplier;

        appliedStaggerMultiplier = newMultiplier;
        staggerTimer = StaggerDuration;

        CamManager.instance?.Shake(1.6f);
    }

    private void TickStagger(float delta)
    {
        if (staggerTimer <= 0f) return;

        staggerTimer -= delta;
        if (staggerTimer > 0f) return;

        RemoveStagger();
    }

    private void RemoveStagger()
    {
        if (appliedStaggerMultiplier == 1f) return;
        if (attribute != null)
            attribute.SPEED_Ampl.TotalBonus /= appliedStaggerMultiplier;

        appliedStaggerMultiplier = 1f;
        staggerTimer = 0f;
    }
    #endregion

    #region Bleed
    private void ApplyBleed()
    {
        bleedStacks = Mathf.Min(bleedStacks + 1, BleedMaxStacks);

        if (bleedTimer <= 0f)
            bleedTickTimer = bleedTickInterval;

        bleedTimer = BleedDuration;
        CamManager.instance?.Shake(0.8f);
    }

    private void TickBleed(float delta)
    {
        if (bleedTimer <= 0f) return;

        bleedTimer -= delta;
        bleedTickTimer -= delta;

        if (bleedTimer <= 0f)
        {
            bleedTimer = 0f;
            bleedStacks = 0;
            return;
        }

        if (bleedTickTimer > 0f) return;
        bleedTickTimer += bleedTickInterval;

        float perStackDamage = BleedFlatDamagePerStack * DomainManager.instance.CurrentDifficulty + (attribute.HP_Max * BleedPercentMaxHPPerStack);
        float totalBleedDamage = perStackDamage * bleedStacks;

        attribute.TakeDamage(totalBleedDamage);
        SetCombat(0.5f);
        PlayHitEffect();
        PlayDeathSound();
    }
    #endregion

    #region Game Start Handlers
    private void OnGameStarted()
    {
        Init();
        Debug.Log("PlayerManager: Game started.");
    }

    private void ResetRuntimeStatus()
    {
        Target = null;
        ManualAttacking = false;
        ManualAttackDirection = Vector2.zero;
        MoveInput = Vector2.zero;
        Direction = Vector2.zero;
        LastDeathAtSpot = Vector2.zero;
    }
    #endregion
}