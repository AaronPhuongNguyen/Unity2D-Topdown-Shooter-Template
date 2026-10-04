using Server;
using UnityEngine;

[DefaultExecutionOrder(-5)]
public class PlayerManager : MonoBehaviour, ITick
{
    [SerializeField] bool DebugMode;
    public PlayerPackage package;
    public UnitAttribute attribute => clonedPack?.attribute;

    #region Inspector Debugger
    public LayerMask EnemyMask;
    public GameObject IndicatorPrefab;
    public float CurrentHP;
    #endregion

    #region Runtime Status
    [HideInInspector] public bool AutoAttack = false;
    [HideInInspector] public Transform Target;

    // Manual attack-joystick state - only meaningful while AutoAttack is
    // false. ManualAttacking is the "is the stick currently held/aimed"
    // flag; ManualAttackDirection is the last nonzero aim direction while held.
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

    // Manual input always takes priority when held, whether AutoAttack is on
    // or off - covers both playstyles (pure manual, and "lazy with manual
    // override") with one condition. Falls back to auto-target only when the
    // stick isn't held AND AutoAttack is enabled.
    public bool IsAttacking => CurrentHP > 0 && (ManualAttacking || (AutoAttack && Target != null));
    public bool IsMoving => MoveInput != Vector2.zero && !IsAttacking && CurrentHP > 0;
    public bool IsIdle => !IsAttacking && !IsMoving && CurrentHP > 0;

    [HideInInspector] public float combatDuration;
    private CorpseEmitter ce;
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
        attribute.OnTakeDamage += SetCombat;
        attribute.OnDeath += PlayDeathSound;
        attribute.OnDeath += AfterDeath;

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

    public void SetCombat(float v) => combatDuration = 0.5f;

    private void AfterDeath()
    {
        Debug.Log("Death!");

        LastDeathAtSpot = Controlling.transform.position;

        attribute.OnDeath -= AfterDeath;
        attribute.OnDeath -= PlayDeathSound;
        attribute.OnTakeDamage -= SetCombat;

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