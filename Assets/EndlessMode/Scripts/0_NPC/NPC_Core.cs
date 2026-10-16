using System;
using System.Collections.Generic;
using Server;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// NPC ally core. Friendly unit that:
///   - FOLLOWS the player (Follow target). Once close, it WANDERS around the
///     player; if the player gets farther than safeRound it follows again.
///     Its move speed is kept equal to the player's,
///   - HUNTS: actively walks toward the nearest zombie (within huntSearchRadius and
///     within huntLeash of the player) until it is in shooting range. When no
///     zombie is left it goes back to following / wandering around the player,
///   - ATTACKS the nearest zombie within its SIGHT (Target), using the same enemy
///     mask the player uses (PlayerManager.EnemyMask). Fire rate comes from ASPD.
///     Shots look like the player's weapon: muzzle flash, travelling Bullet,
///     hit effect and damage popup,
///   - HAS A MAGAZINE: each shot uses ammo. When empty it RELOADS (reloadTime seconds,
///     reload sound) and cannot shoot meanwhile. When idle (no target) it also
///     tops up the magazine if it is below tacticalReloadThreshold,
///   - FLEES when a zombie gets too close (highest priority). Threats are found
///     with scanRadius. It scores 16 escape directions and runs the best one.
///     It only goes back to normal behaviour once nothing has been near for calmTime,
///   - DODGES: an attacker calls NotifyIncomingAttack() right before it strikes.
///     The NPC instantly dashes sideways and is invincible for the dash.
///     Costs Energy, has its own cooldown,
///   - GETS STAGGERED (temporary slow) whenever it takes a hit,
///   - MATCHES THE PLAYER'S STATS (HP, ATK, DEF and speed),
///   - is removed on game restart (EventBus.OnGameRestart) and on death.
///
/// Mode priority:  Fleeing  >  Hunting  >  Following (+ wander)
///
/// Searches:
///   FindTarget()  - radius = SIGHT_Current  -> who to shoot
///   FindThreats() - radius = scanRadius     -> who to run from / who to hunt
///
/// All active NPCs are listed in NpcBrain.Active so zombies can hunt them
/// without any physics query.
///
/// Inheritance: derive a character (e.g. Kafka) from this class. Override
/// OnSpawned / OnRemoved / OnNewWave / OnDeath / OnDodged / OnReloadStarted /
/// OnReloadFinished for character-specific behaviour (always call base where noted).
///
/// Prefab setup:
///   - Put it on a layer that Zombrain's hb.enemyMask includes (the player's layer),
///     with a Collider2D, otherwise zombies can't see or hit it.
///   - PlayerManager.EnemyMask must contain the zombie layer.
///   - Assign a muzzle Transform and a Bullet prefab. ShootEffect is optional.
///   - Assign Reload Sound (optional).
///   - Animator (optional): bool IsWalking, bool IsReloading, trigger Attack, trigger Reload.
///
/// Spawn with NpcBrain.Spawn(prefab, package, position), or let NpcSpawner do it.
/// </summary>
public class NpcBrain : HurtBox, ITick
{
    public enum Mode { Following, Hunting, Fleeing }

    /// <summary>Every NPC between Init() and removal. Zombies read this to hunt NPCs first.</summary>
    public static readonly List<NpcBrain> Active = new List<NpcBrain>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active.Clear();

    /// <summary>True if any living NPC was spawned from this package asset.</summary>
    public static bool IsAlive(NpcPackage source)
    {
        for (int i = 0; i < Active.Count; i++)
            if (Active[i] != null && Active[i].SourcePackage == source) return true;
        return false;
    }

    protected PlayerManager pm => PlayerManager.instance;
    protected DomainManager dm => DomainManager.instance;

    #region Data
    public NpcPackage package;
    public UnitAttribute attribute;
    public override UnitPackage Access() => package;

    /// <summary>The original (asset) package this NPC was spawned from.</summary>
    public NpcPackage SourcePackage { get; private set; }

    public Animator anim;
    public bool CanAttack = true;
    public bool CanMove = true;

    [Header("Targets")]
    [Tooltip("Attack target: nearest zombie within Sight.")]
    public Transform Target;
    [Tooltip("Follow target. Defaults to the player while followPlayer is on.")]
    public Transform Follow;
    public bool followPlayer = true;

    public Mode CurrentMode { get; private set; } = Mode.Following;

    /// <summary>True between Init() and removal (death / restart).</summary>
    public bool IsActive => isActive;

    /// <summary>True while the dodge dash is running (invincible).</summary>
    public bool IsDodging => isDashing;

    /// <summary>The zombie the NPC is currently walking toward (null when not hunting).</summary>
    public Transform HuntTarget => huntTarget;
    #endregion

    #region Inspector: Magazine / Reload
    [Header("Magazine / Reload")]
    [Tooltip("Turn the magazine system off: the NPC never runs out of ammo.")]
    [SerializeField] private bool infiniteAmmo = false;
    [Tooltip("Rounds in a full magazine.")]
    [SerializeField, Min(1)] private int maxMagazine = 60;
    [Tooltip("Rounds used by one shot.")]
    [SerializeField, Min(1)] private int ammoPerShot = 1;
    [Tooltip("Seconds a reload takes.")]
    [SerializeField, Min(0.05f)] private float reloadTime = 2f;
    [Tooltip("Reload automatically when the magazine is empty.")]
    [SerializeField] private bool autoReload = true;
    [Tooltip("Also reload while idle (no target, not fleeing) when the magazine is below the threshold.")]
    [SerializeField] private bool tacticalReload = true;
    [Tooltip("Idle reload starts when ammo is at or below this fraction of the magazine.")]
    [SerializeField, Range(0f, 1f)] private float tacticalReloadThreshold = 0.5f;
    [Tooltip("Played when a reload starts.")]
    [SerializeField] private AudioClip reloadSound;
    [Tooltip("Optional effect spawned at the muzzle when a reload starts.")]
    [SerializeField] private GameObject reloadEffect;
    #endregion

    #region Inspector: Energy / Dodge
    [Header("Energy")]
    public EnergyPool Energy = new EnergyPool { Max = 10f, RegenPerSecond = 1f };

    [Header("Dodge")]
    [SerializeField] private bool canDodge = true;
    [Tooltip("Energy spent per dodge.")]
    [SerializeField] private float dodgeEnergyCost = 10f;
    [Tooltip("Seconds between dodges (counted from the start of a dodge).")]
    [SerializeField] private float dodgeCooldown = 2f;
    [Tooltip("Seconds of dash (= seconds of invincibility).")]
    [SerializeField] private float dodgeDuration = 0.25f;
    [Tooltip("Total distance covered by the dash (world units).")]
    [SerializeField] private float dodgeDistance = 4f;
    [Tooltip("0 = pure sidestep, 1 = equal sidestep and backwards push.")]
    [SerializeField, Range(0f, 1f)] private float dodgeAwayBias = 0.35f;
    [Tooltip("Optional effect spawned at the start of a dodge.")]
    [SerializeField] private GameObject dodgeEffect;
    #endregion

    #region Inspector: Sense
    [Header("Sense - Attack Target (uses Sight)")]
    [Tooltip("How often the NPC looks for a target within its Sight (SIGHT_Current).")]
    [SerializeField] private float targetScanInterval = 0.2f;

    [Header("Sense - Threats (uses Scan Radius)")]
    [Tooltip("How often the NPC looks for dangerous zombies to flee from / hunt.")]
    [SerializeField] private float threatScanInterval = 0.2f;
    [Tooltip("Radius used to find threats. Independent from Sight.")]
    [SerializeField] private float scanRadius = 14f;

    [Header("Hunt (actively search for zombies)")]
    [SerializeField] private bool huntEnabled = true;
    [Tooltip("Zombies farther than this from the NPC are ignored for hunting (capped by Scan Radius).")]
    [SerializeField] private float huntSearchRadius = 14f;
    [Tooltip("Max distance from the PLAYER the NPC may go while hunting. Only zombies within this of the player are hunted.")]
    [SerializeField] private float huntLeash = 12f;
    [Tooltip("The NPC stops walking and shoots at Sight x this distance from its prey.")]
    [SerializeField, Range(0.3f, 1f)] private float huntStopRatio = 0.8f;

    [Header("Follow")]
    [Tooltip("Stops following and starts wandering once this close to the player.")]
    [SerializeField] private float followStopDistance = 2.5f;
    [Tooltip("Safe Round: while within this distance of the player the NPC wanders. Beyond it, the NPC follows again.")]
    [SerializeField] private float safeRound = 7f;

    [Header("Wander (near the player)")]
    [Tooltip("The NPC roams to random points within this radius around the player.")]
    [SerializeField] private float wanderRadius = 4f;
    [Tooltip("Counts as arrived when this close to the wander point.")]
    [SerializeField] private float wanderArriveDistance = 0.4f;
    [Tooltip("Random rest time between wander moves.")]
    [SerializeField] private float wanderPauseMin = 0.6f;
    [SerializeField] private float wanderPauseMax = 2f;

    [Header("Move Speed")]
    [Tooltip("Keeps the NPC's move speed equal to the player's current move speed.")]
    [SerializeField] private bool matchPlayerSpeed = true;

    [Header("Follow - Catch Up")]
    [Tooltip("Gets a speed boost when farther than this (catching up).")]
    [SerializeField] private float catchUpDistance = 18f;
    [SerializeField] private float catchUpSpeedBonus = 3f;

    [Header("Danger / Flee")]
    [Tooltip("A zombie closer than this (surface distance) makes the NPC flee.")]
    [SerializeField] private float dangerRadius = 3.5f;
    [Tooltip("Keeps fleeing until nothing has been within this for calmTime.")]
    [SerializeField] private float safeRadius = 5.5f;
    [SerializeField] private float calmTime = 0.8f;
    [SerializeField] private int fleeDirections = 16;
    [SerializeField] private float fleeLookAhead = 3.5f;
    [SerializeField] private float fleeSpeedBonus = 3f;
    [Tooltip("Bonus for keeping the previous flee direction (less jitter).")]
    [SerializeField] private float continuityBonus = 0.3f;
    [SerializeField] private float wallPenalty = 5f;
    [SerializeField] private float edgeMargin = 3f;
    [SerializeField] private float edgePenalty = 0.5f;

    [Header("Hit Stagger")]
    [Tooltip("Speed change (TotalBonus) applied while staggered. -0.4 = 40% slower.")]
    [SerializeField] private float hitStaggerSlowPercent = -0.4f;
    [Tooltip("Seconds the stagger lasts. Refreshed on every hit.")]
    [SerializeField] private float hitStaggerDuration = 1.2f;

    [Header("Attack")]
    [SerializeField] private bool attackWhileFleeing = true;
    [SerializeField] private float minAttackCooldown = 0.1f;
    [SerializeField] private float maxAttackCooldown = 3f;
    [Tooltip("Random aim error in degrees (+/-). 0 = perfect aim.")]
    [SerializeField] private float spreadAngle = 2f;

    [Header("Attack - Visual (same as the Player's Weapon)")]
    [Tooltip("Where shots and the muzzle flash come from. Falls back to the NPC's position.")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private GameObject shootEffect;
    [Tooltip("Same Bullet prefabs the player's Weapon uses.")]
    [SerializeField] private Bullet bulletPrefab;
    [SerializeField] private float bulletSpeed = 125f;
    [SerializeField] private float bulletLifetime = 1f;

    [Header("Aim Indicator (same as the Player's)")]
    [Tooltip("Marker shown on the current attack target. Leave empty for no indicator.")]
    [SerializeField] private GameObject indicatorPrefab;
    [Tooltip("On every shot the marker pops to normal scale x this, then eases back.")]
    [SerializeField] private float indicatorPulseMultiplier = 1.5f;
    [Tooltip("How fast the marker eases back to normal. 1 = same feel as the player's.")]
    [SerializeField] private float indicatorReturnSpeed = 1f;
    #endregion

    #region Runtime

    // A current target is kept until it is farther than Sight * this (stops flicker at the edge).
    private const float TargetKeepMargin = 1.15f;

    // The NPC counts as "too far from the player" at huntLeash * this.
    private const float LeashBreakMargin = 1.15f;

    protected Transform _t;
    private bool isActive;

    private float targetScanTimer;
    private float threatScanTimer;
    private float calmTimer;
    private float attackTimer;
    private float appliedSpeed;

    private bool followMoving;

    private Vector2 wanderTarget;
    private bool hasWanderTarget;
    private float wanderPause;
    private float appliedSync;
    private float lastFollowDist;
    private Vector2 fleeDir;

    // Hunt
    private Transform huntTarget;
    private bool returningToPlayer;

    // Magazine
    private int currentAmmo;
    private bool isReloading;
    private float reloadTimer;

    // Hit stagger
    private float hitStaggerTimer;
    private bool staggerApplied;
    private float appliedStaggerPercent;

    // Dodge
    private bool isDashing;
    private float dashTimeLeft;
    private float dodgeCooldownTimer;
    private Vector2 dashDir;
    private bool dashInvincibleApplied;

    private UnitAttribute targetUnit;
    private GameObject indicator;

    // Target search (Sight)
    private Collider2D[] targetBuffer = new Collider2D[16];

    // Threat search (scanRadius)
    private Collider2D[] threatBuffer = new Collider2D[32];
    private Vector2[] threatPts = new Vector2[32];
    private int threatCount;
    private float nearestThreat = float.MaxValue;
    #endregion

    #region Magazine API
    /// <summary>Fired whenever ammo changes: (current, max). Cleared when the NPC is removed.</summary>
    public event Action<int, int> OnAmmoChanged;

    /// <summary>Fired when a reload starts. Cleared when the NPC is removed.</summary>
    public event Action OnReloadStartedEvent;

    /// <summary>Fired when a reload completes. Cleared when the NPC is removed.</summary>
    public event Action OnReloadFinishedEvent;

    public int CurrentAmmo => currentAmmo;
    public int MaxAmmo => maxMagazine;
    public bool InfiniteAmmo { get => infiniteAmmo; set => infiniteAmmo = value; }
    public bool IsReloading => isReloading;
    public bool IsMagazineFull => infiniteAmmo || currentAmmo >= maxMagazine;
    public bool IsMagazineEmpty => !infiniteAmmo && currentAmmo < ammoPerShot;

    /// <summary>Reload progress 0..1 (0 when not reloading). Handy for a reload bar.</summary>
    public float ReloadProgress => isReloading ? 1f - Mathf.Clamp01(reloadTimer / reloadTime) : 0f;

    /// <summary>True if the magazine has enough rounds and no reload is running.</summary>
    public bool CanShoot => !isReloading && (infiniteAmmo || currentAmmo >= ammoPerShot);

    /// <summary>Starts a reload. Returns false if already reloading, full, or not allowed.</summary>
    public bool StartReload()
    {
        if (!isActive || attribute == null || attribute.IsDead) return false;
        if (infiniteAmmo || isReloading || currentAmmo >= maxMagazine) return false;

        isReloading = true;
        reloadTimer = reloadTime;

        Play(reloadSound);
        PlayReloadEffect();

        OnReloadStarted();
        OnReloadStartedEvent?.Invoke();
        return true;
    }

    /// <summary>Stops a running reload without giving ammo.</summary>
    public void CancelReload()
    {
        if (!isReloading) return;

        isReloading = false;
        reloadTimer = 0f;
    }

    /// <summary>Instantly fills the magazine (cancels any reload).</summary>
    public void RefillAmmo()
    {
        CancelReload();
        SetAmmo(maxMagazine);
    }

    /// <summary>Adds rounds (clamped to the magazine size). Returns how many were really added.</summary>
    public int AddAmmo(int amount)
    {
        if (amount <= 0) return 0;
        int before = currentAmmo;
        SetAmmo(currentAmmo + amount);
        return currentAmmo - before;
    }

    /// <summary>Changes the magazine size at runtime (e.g. from a card). Optionally refills.</summary>
    public void SetMaxMagazine(int newMax, bool refill = false)
    {
        maxMagazine = Mathf.Max(1, newMax);
        if (refill) RefillAmmo();
        else SetAmmo(Mathf.Min(currentAmmo, maxMagazine));
    }

    /// <summary>Changes the reload time at runtime (does not affect a reload already running).</summary>
    public void SetReloadTime(float seconds) => reloadTime = Mathf.Max(0.05f, seconds);

    private void SetAmmo(int value)
    {
        value = Mathf.Clamp(value, 0, maxMagazine);
        if (value == currentAmmo) return;

        currentAmmo = value;
        OnAmmoChanged?.Invoke(currentAmmo, maxMagazine);
    }

    private void FinishReload()
    {
        isReloading = false;
        reloadTimer = 0f;

        SetAmmo(maxMagazine);

        OnReloadFinished();
        OnReloadFinishedEvent?.Invoke();
    }

    private void TickReload(float dt)
    {
        if (infiniteAmmo) return;

        if (isReloading)
        {
            reloadTimer -= dt;
            if (reloadTimer <= 0f) FinishReload();
            return;
        }

        // Empty: reload right away.
        if (autoReload && IsMagazineEmpty)
        {
            StartReload();
            return;
        }

        // Quiet moment: top up the magazine.
        if (tacticalReload
            && Target == null
            && CurrentMode != Mode.Fleeing
            && !isDashing
            && currentAmmo < maxMagazine
            && currentAmmo <= Mathf.CeilToInt(maxMagazine * tacticalReloadThreshold))
        {
            StartReload();
        }
    }

    private void PlayReloadEffect()
    {
        if (reloadEffect == null || PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(reloadEffect);
        if (o == null) return;

        o.transform.position = MuzzlePos;
        o.transform.rotation = _t.rotation;
    }
    #endregion

    #region Spawn / Lifecycle
    /// <summary>Gets an NPC from the pool, places it and initialises it.</summary>
    public static NpcBrain Spawn(GameObject prefab, NpcPackage pkg, Vector2 position)
    {
        if (prefab == null || pkg == null || PoolingSystem.instance == null) return null;

        GameObject o = PoolingSystem.instance.GetFromPool(prefab);
        if (o == null) return null;

        if (!o.TryGetComponent(out NpcBrain npc))
        {
            Debug.LogWarning($"NpcBrain.Spawn: {o.name} has no NpcBrain component!");
            return null;
        }

        o.transform.position = position;
        return npc.Init(pkg) ? npc : null;
    }

    protected virtual void Awake()
    {
        _t = transform;
    }

    protected virtual void OnEnable()
    {
        EventBus.OnGameRestart += Remove;
        EventBus.OnNewWave += OnNewWave;
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
    }

    protected virtual void OnDisable()
    {
        EventBus.OnGameRestart -= Remove;
        EventBus.OnNewWave -= OnNewWave;
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }

    public virtual bool Init(NpcPackage pkg)
    {
        if (pkg == null || pkg.attribute == null)
        {
            Debug.LogWarning("NpcBrain: package or its attribute is missing.");
            return false;
        }
        if (isActive)
        {
            Debug.LogWarning($"[NpcBrain] Init blocked on {name} - already active.");
            return false;
        }

        SetRigidBody();

        SourcePackage = pkg;
        package = Instantiate(pkg);
        attribute = package.attribute.Clone();
        attribute.Reset();
        attribute.tag = UnitTag.Friendly;
        attribute.OnDeath += OnDeath;
        attribute.OnTakeDamage += OnHit;
        package.attribute = attribute;
        isActive = true;

        if (!Active.Contains(this)) Active.Add(this);

        Target = null;
        targetUnit = null;
        Follow = null;
        huntTarget = null;
        returningToPlayer = false;
        CurrentMode = Mode.Following;
        followMoving = false;
        fleeDir = Vector2.zero;
        threatCount = 0;
        nearestThreat = float.MaxValue;
        lastFollowDist = 0f;

        targetScanTimer = 0f;
        threatScanTimer = 0f;
        calmTimer = 0f;
        attackTimer = 0f;
        appliedSpeed = 0f;

        hasWanderTarget = false;
        wanderPause = 0f;
        appliedSync = 0f;

        // Fresh clone: no stagger applied to it.
        hitStaggerTimer = 0f;
        staggerApplied = false;
        appliedStaggerPercent = 0f;

        // Fresh energy, no dodge running.
        Energy.Refill();
        isDashing = false;
        dashTimeLeft = 0f;
        dodgeCooldownTimer = 0f;
        dashInvincibleApplied = false;

        // Full magazine, no reload running.
        isReloading = false;
        reloadTimer = 0f;
        currentAmmo = maxMagazine;

        SpawnIndicator();

        SyncStatsWithPlayer();
        attribute.HP_Current = attribute.HP_Max; // spawn at full HP

        OnSpawned();
        OnAmmoChanged?.Invoke(currentAmmo, maxMagazine);
        return true;
    }

    /// <summary>Hook: called at the end of a successful Init(). NPC is fully set up.</summary>
    protected virtual void OnSpawned() { }

    /// <summary>Hook: called at the start of Remove(), while attribute/package are still valid.</summary>
    protected virtual void OnRemoved() { }

    /// <summary>Hook: called when the NPC starts a dodge (energy already spent).</summary>
    protected virtual void OnDodged() { }

    /// <summary>Hook: called when a reload starts.</summary>
    protected virtual void OnReloadStarted() { }

    /// <summary>Hook: called when a reload completes (magazine already full).</summary>
    protected virtual void OnReloadFinished() { }

    protected virtual void OnDeath()
    {
        PlayDeathSound();
        Remove();
    }

    /// <summary>Removes the NPC (restart / death). Safe to call repeatedly.</summary>
    protected virtual void Remove()
    {
        if (!isActive) return;
        isActive = false;

        OnRemoved();

        Active.Remove(this);

        EndDodge();
        CancelReload();

        if (attribute != null)
        {
            attribute.OnDeath -= OnDeath;
            attribute.OnTakeDamage -= OnHit;
            RemoveHitStagger();
            if (appliedSpeed != 0f) attribute.SPEED_Ampl.FlatBonus -= appliedSpeed;
            if (appliedSync != 0f) attribute.SPEED_Ampl.FlatBonus -= appliedSync;
        }
        appliedSpeed = 0f;
        hitStaggerTimer = 0f;
        staggerApplied = false;
        appliedStaggerPercent = 0f;

        Target = null;
        targetUnit = null;
        Follow = null;
        huntTarget = null;
        returningToPlayer = false;
        attribute = null;

        // Pooled object: don't keep old subscribers (UI bars etc.) between lives.
        OnAmmoChanged = null;
        OnReloadStartedEvent = null;
        OnReloadFinishedEvent = null;

        DestroyIndicator();

        if (package != null) Destroy(package);
        package = null;
        SourcePackage = null;

        if (PoolingSystem.instance != null) PoolingSystem.instance.RemoveToPool(gameObject);
        else Destroy(gameObject);
    }

    private void SetRigidBody()
    {
        if (!TryGetComponent(out Rigidbody2D rb)) rb = gameObject.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.constraints = RigidbodyConstraints2D.FreezePosition;
        rb.freezeRotation = true;
        if (anim != null)
            anim.cullingMode = AnimatorCullingMode.CullCompletely;
    }
    #endregion

    #region Dodge
    /// <summary>
    /// Called by an attacker right BEFORE its strike resolves (e.g. Zombrain.PerformAttack).
    /// If the NPC can afford it, it dashes away and turns invincible instantly,
    /// so the incoming hit is wasted. Returns true if a dodge started.
    /// </summary>
    public bool NotifyIncomingAttack(Vector2 attackerPos)
    {
        if (!canDodge || !isActive || attribute == null || attribute.IsDead) return false;
        if (isDashing || dodgeCooldownTimer > 0f) return false;
        if (!Energy.TrySpend(dodgeEnergyCost)) return false;

        StartDodge(attackerPos);
        return true;
    }

    private void StartDodge(Vector2 attackerPos)
    {
        Vector2 pos = _t.position;
        Vector2 away = pos - attackerPos;
        if (away.sqrMagnitude < 0.0001f)
        {
            float a = RNG.GetFloat(0f, Mathf.PI * 2f);
            away = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        away.Normalize();

        // Sidestep: pick the side (left/right of the attack line) with more open space.
        Vector2 perp = new Vector2(-away.y, away.x);
        Vector2 dirA = (perp + away * dodgeAwayBias).normalized;
        Vector2 dirB = (-perp + away * dodgeAwayBias).normalized;

        float costA = BoundsPenalty(pos + dirA * dodgeDistance);
        float costB = BoundsPenalty(pos + dirB * dodgeDistance);

        if (Mathf.Approximately(costA, costB)) dashDir = RNG.GetFloat(0f, 1f) < 0.5f ? dirA : dirB;
        else dashDir = costA < costB ? dirA : dirB;

        isDashing = true;
        dashTimeLeft = dodgeDuration;
        dodgeCooldownTimer = dodgeCooldown;

        attribute.IsInvincible = true;
        dashInvincibleApplied = true;

        if (dodgeEffect != null && PoolingSystem.instance != null)
        {
            GameObject o = PoolingSystem.instance.GetFromPool(dodgeEffect);
            if (o != null)
            {
                o.transform.position = pos;
                o.transform.rotation = _t.rotation;
            }
        }

        OnDodged();
    }

    private void TickDodge(float dt)
    {
        float step = Mathf.Min(dt, dashTimeLeft);
        float speed = dodgeDuration > 0.0001f ? dodgeDistance / dodgeDuration : 0f;

        Vector2 p = (Vector2)_t.position + dashDir * speed * step;
        if (dm != null) p = dm.ClampToMap(p);
        _t.position = new Vector3(p.x, p.y, _t.position.z);

        dashTimeLeft -= dt;
        if (dashTimeLeft <= 0f) EndDodge();
    }

    private void EndDodge()
    {
        isDashing = false;
        dashTimeLeft = 0f;

        if (dashInvincibleApplied && attribute != null)
            attribute.IsInvincible = false;
        dashInvincibleApplied = false;
    }
    #endregion

    #region Hit Stagger
    private void OnHit(float damage)
    {
        ApplyHitStagger();
    }

    /// <summary>Slows the NPC for hitStaggerDuration. Re-hits only refresh the timer (no stacking).</summary>
    private void ApplyHitStagger()
    {
        if (!isActive || attribute == null || attribute.IsDead) return;

        hitStaggerTimer = hitStaggerDuration;
        if (staggerApplied) return;

        appliedStaggerPercent = hitStaggerSlowPercent;
        attribute.SPEED_Ampl.TotalBonus += appliedStaggerPercent;
        staggerApplied = true;
    }

    private void TickHitStagger(float dt)
    {
        if (!staggerApplied) return;

        hitStaggerTimer -= dt;
        if (hitStaggerTimer <= 0f) RemoveHitStagger();
    }

    private void RemoveHitStagger()
    {
        if (!staggerApplied) return;
        if (attribute != null)
            attribute.SPEED_Ampl.TotalBonus -= appliedStaggerPercent;

        appliedStaggerPercent = 0f;
        hitStaggerTimer = 0f;
        staggerApplied = false;
    }
    #endregion

    #region Match Player Stats
    /// <summary>Kept so subclasses that override it still compile. Stats now follow the player, not the wave.</summary>
    protected virtual void OnNewWave(int wave) { }

    /// <summary>
    /// Makes this NPC's HP, ATK and DEF equal the player's. Everything else
    /// stays at the NPC's own template values. Speed is handled by SyncSpeedWithPlayer.
    /// </summary>
    private void SyncStatsWithPlayer()
    {
        if (pm == null || pm.attribute == null || attribute == null || attribute.IsDead) return;

        UnitAttribute p = pm.attribute;
        UnitAttribute n = attribute;
        if (p.IsNewUnit || n.IsNewUnit) return;

        float hpBefore = n.HP_Max;
        Match(n, p, a => a.HP_Ampl, a => a.HP_Max);
        float hpDiff = n.HP_Max - hpBefore;
        if (hpDiff > 0f) n.Heal(hpDiff);
        else if (n.HP_Current > n.HP_Max) n.HP_Current = n.HP_Max;

        Match(n, p, a => a.ATK_Ampl, a => a.ATK_Current);
        Match(n, p, a => a.DEF_Ampl, a => a.DEF_Current);
    }

    private static void Match(UnitAttribute n, UnitAttribute p,
        Func<UnitAttribute, Amplification> amp, Func<UnitAttribute, float> val)
    {
        float target = val(p);
        float cur = val(n);
        float err = target - cur;
        if (Mathf.Abs(err) < 0.001f) return;

        Amplification a = amp(n);
        a.FlatBonus += err;

        float after = val(n);
        float gain = (after - cur) / err; // how much 1 flat point really moves this stat
        if (gain > 0.0001f && Mathf.Abs(target - after) > 0.001f)
            a.FlatBonus += (target - after) / gain;
    }
    #endregion

    #region Tick
    public virtual void Tick(float dt)
    {
        if (TickSystem.GameSpeed <= 0) return;
        if (!isActive || attribute == null || attribute.IsDead) return;
        if (pm == null) return;

        Energy.Tick(dt);
        if (dodgeCooldownTimer > 0f) dodgeCooldownTimer -= dt;

        TickHitStagger(dt);
        ResolveFollow();

        // Two independent searches: Sight for targets, scanRadius for threats / hunting.
        targetScanTimer -= dt;
        if (targetScanTimer <= 0f)
        {
            targetScanTimer = targetScanInterval;
            FindTarget();
        }

        threatScanTimer -= dt;
        if (threatScanTimer <= 0f)
        {
            threatScanTimer = threatScanInterval;
            FindThreats();
            EvaluateMode();
            SyncSpeedWithPlayer();
            SyncStatsWithPlayer();
        }

        // Prey died / despawned between scans: drop back to following right away.
        if (CurrentMode == Mode.Hunting && !IsHuntTargetValid())
            EnterFollowing();

        if (CurrentMode == Mode.Fleeing)
        {
            calmTimer -= dt;
            if (calmTimer <= 0f) EnterFollowing();
        }

        TickReload(dt);

        bool hasAim = TickAttack(dt, out Vector2 aimDir);
        HandleIndicator(dt);

        // While dashing, the dash IS the movement.
        Vector2 move = Vector2.zero;
        if (isDashing) TickDodge(dt);
        else if (CanMove) move = GetMoveDirection(dt);

        if (!isDashing) move = ShapeMovement(move, dt);

        if (move != Vector2.zero)
        {
            UnitMotion.MoveThisObject(attribute, gameObject, move);
            if (dm != null)
            {
                Vector3 p = _t.position;
                Vector2 c = dm.ClampToMap(p);
                _t.position = new Vector3(c.x, c.y, p.z);
            }
        }

        // Face the enemy while shooting, otherwise face where we are going.
        Vector2 face = hasAim ? aimDir : (isDashing ? dashDir : move);

        face = ShapeFacing(face, dt);
        if (face != Vector2.zero) UnitMotion.RotateThisObject(gameObject, face);

        UpdateSpeedBonus();

    }
    /// <summary>Hook: lets a subclass smooth the wanted move direction (inertia). Default: unchanged.</summary>
    protected virtual Vector2 ShapeMovement(Vector2 desired, float dt) => desired;

    /// <summary>Hook: lets a subclass limit how fast the facing changes. Default: unchanged.</summary>
    protected virtual Vector2 ShapeFacing(Vector2 desired, float dt) => desired;

    private void ResolveFollow()
    {
        if (followPlayer)
            Follow = pm.Controlling != null ? pm.Controlling.transform : null;

        lastFollowDist = Follow != null
            ? ((Vector2)Follow.position - (Vector2)_t.position).magnitude
            : 0f;
    }
    #endregion

    #region Sense
    /// <summary>A hostile, living, aggressive unit (a zombie / boss) behind this collider.</summary>
    private static bool TryGetHostile(Collider2D col, out UnitAttribute ua)
    {
        ua = null;
        if (col == null) return false;
        if (!col.TryGetComponent(out HurtBox hurt)) return false;

        ua = hurt.Access()?.attribute;
        if (ua == null || ua.IsDead) return false;
        return ua.tag == UnitTag.Aggressive;
    }

    /// <summary>
    /// ATTACK TARGET: the nearest hostile within Sight (SIGHT_Current).
    /// Keeps the current target while it stays within Sight * TargetKeepMargin.
    /// </summary>
    private void FindTarget()
    {
        Vector2 pos = _t.position;
        float sight = attribute.SIGHT_Current;

        int count = Physics2D.OverlapCircleNonAlloc(pos, sight, targetBuffer, pm.EnemyMask);
        if (count >= targetBuffer.Length)
        {
            targetBuffer = new Collider2D[targetBuffer.Length * 2];
            count = Physics2D.OverlapCircleNonAlloc(pos, sight, targetBuffer, pm.EnemyMask);
        }

        // Keep the current target while it is alive and still within sight.
        if (Target != null && targetUnit != null && !targetUnit.IsDead && !targetUnit.IsUntargetable && Target.gameObject.activeInHierarchy)
        {
            float keep = sight * TargetKeepMargin;
            if (((Vector2)Target.position - pos).sqrMagnitude <= keep * keep) return;
        }

        Transform best = null;
        UnitAttribute bestUnit = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = targetBuffer[i];
            if (!TryGetHostile(col, out UnitAttribute ua)) continue;

            float sqr = ((Vector2)col.transform.position - pos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = col.transform;
                bestUnit = ua;
            }
        }

        Target = best;
        targetUnit = bestUnit;
    }

    /// <summary>
    /// THREATS: every hostile within scanRadius (used for fleeing). While scanning it
    /// also picks the nearest valid HUNT target: within huntSearchRadius of the NPC
    /// and within huntLeash of the player.
    /// </summary>
    private void FindThreats()
    {
        Vector2 pos = _t.position;

        int count = Physics2D.OverlapCircleNonAlloc(pos, scanRadius, threatBuffer, pm.EnemyMask);
        if (count >= threatBuffer.Length)
        {
            threatBuffer = new Collider2D[threatBuffer.Length * 2];
            threatPts = new Vector2[threatBuffer.Length];
            count = Physics2D.OverlapCircleNonAlloc(pos, scanRadius, threatBuffer, pm.EnemyMask);
        }

        threatCount = 0;
        nearestThreat = float.MaxValue;

        float searchR = Mathf.Min(huntSearchRadius, scanRadius);
        float leashSqr = huntLeash * huntLeash;
        bool hasPlayer = Follow != null;
        Vector2 playerPos = hasPlayer ? (Vector2)Follow.position : Vector2.zero;

        Transform bestHunt = null;
        float bestHuntDist = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = threatBuffer[i];
            if (!TryGetHostile(col, out UnitAttribute ua)) continue;

            // Surface distance, so a huge boss counts as "close" earlier than its centre says.
            Vector2 closest = col.ClosestPoint(pos);
            float d = (closest - pos).magnitude;

            if (threatCount < threatPts.Length) threatPts[threatCount++] = closest;
            if (d < nearestThreat) nearestThreat = d;

            // Hunt candidate
            if (!huntEnabled || ua.IsUntargetable) continue;
            if (d > searchR || d >= bestHuntDist) continue;
            if (hasPlayer && ((Vector2)col.transform.position - playerPos).sqrMagnitude > leashSqr) continue;

            bestHuntDist = d;
            bestHunt = col.transform;
        }

        huntTarget = bestHunt;
    }

    private bool IsHuntTargetValid()
    {
        return huntTarget != null && huntTarget.gameObject.activeInHierarchy;
    }
    #endregion

    #region Mode: Follow / Hunt / Flee
    private void EvaluateMode()
    {
        // Fleeing has priority and its own exit rules.
        if (CurrentMode == Mode.Fleeing)
        {
            // Any zombie still within safeRadius keeps the panic going.
            if (nearestThreat <= safeRadius) calmTimer = calmTime;
            ChooseFleeDirection();
            return;
        }

        if (nearestThreat <= dangerRadius)
        {
            EnterFleeing();
            return;
        }

        UpdateHuntState();
    }

    /// <summary>Chooses between Hunting and Following (Fleeing is handled in EvaluateMode).</summary>
    private void UpdateHuntState()
    {
        // Leash: if hunting took the NPC too far from the player, walk back first.
        if (Follow != null)
        {
            float d = lastFollowDist;
            if (d > huntLeash * LeashBreakMargin) returningToPlayer = true;
            else if (returningToPlayer && d <= safeRound) returningToPlayer = false;
        }
        else
        {
            returningToPlayer = false;
        }

        bool wantHunt = huntEnabled && !returningToPlayer && IsHuntTargetValid();

        if (wantHunt)
        {
            CurrentMode = Mode.Hunting;
        }
        else if (CurrentMode == Mode.Hunting)
        {
            EnterFollowing();
        }

        // Returning home: chase the player straight away, don't wander yet.
        if (returningToPlayer && CurrentMode == Mode.Following)
        {
            followMoving = true;
            hasWanderTarget = false;
        }
    }

    private void EnterFleeing()
    {
        CurrentMode = Mode.Fleeing;
        calmTimer = calmTime;
        hasWanderTarget = false;
        followMoving = false;
        ChooseFleeDirection();
    }

    private void EnterFollowing()
    {
        CurrentMode = Mode.Following;
        fleeDir = Vector2.zero;
        hasWanderTarget = false;
        wanderPause = 0f;
        // followMoving is re-evaluated from the distance next frame (hysteresis).
    }

    /// <summary>
    /// Scores evenly spaced directions and picks the cheapest. Cost is higher the
    /// closer the path gets to any zombie (sampled half-way and at the end of the
    /// look-ahead), plus penalties for walls/map edges, minus a bonus for keeping
    /// the previous direction.
    /// </summary>
    private void ChooseFleeDirection()
    {
        Vector2 pos = _t.position;
        int n = Mathf.Max(4, fleeDirections);

        float bestCost = float.MaxValue;
        Vector2 bestDir = fleeDir;

        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

            float cost = 0f;
            for (int s = 1; s <= 2; s++)
            {
                Vector2 p = pos + d * (fleeLookAhead * s * 0.5f);

                for (int t = 0; t < threatCount; t++)
                    cost += 1f / ((p - threatPts[t]).sqrMagnitude + 0.25f);

                cost += BoundsPenalty(p);
            }

            if (fleeDir != Vector2.zero)
                cost -= Vector2.Dot(d, fleeDir) * continuityBonus;

            if (cost < bestCost)
            {
                bestCost = cost;
                bestDir = d;
            }
        }

        fleeDir = bestDir;
    }

    private float BoundsPenalty(Vector2 p)
    {
        if (dm == null) return 0f;

        Vector2 min = dm.WalkableMin;
        Vector2 max = dm.WalkableMax;

        Vector2 clamped = new Vector2(Mathf.Clamp(p.x, min.x, max.x), Mathf.Clamp(p.y, min.y, max.y));
        float outside = (p - clamped).magnitude;
        float edge = Mathf.Min(Mathf.Min(p.x - min.x, max.x - p.x), Mathf.Min(p.y - min.y, max.y - p.y));

        float penalty = outside * wallPenalty;
        if (edge < edgeMargin)
            penalty += (edgeMargin - Mathf.Max(edge, 0f)) * edgePenalty;
        return penalty;
    }
    #endregion

    #region Movement
    private Vector2 GetMoveDirection(float dt)
    {
        switch (CurrentMode)
        {
            case Mode.Fleeing: return fleeDir;
            case Mode.Hunting: return GetHuntDirection();
            default: return GetFollowDirection(dt);
        }
    }

    /// <summary>
    /// Walks toward the prey until it is in shooting range, then stands still and
    /// lets TickAttack do the shooting. Never stops closer than the flee trigger.
    /// </summary>
    private Vector2 GetHuntDirection()
    {
        if (!IsHuntTargetValid()) return Vector2.zero;

        Vector2 to = (Vector2)huntTarget.position - (Vector2)_t.position;
        float d = to.magnitude;
        if (d <= HuntStopDistance || d < 0.0001f) return Vector2.zero;

        return to / d;
    }

    /// <summary>Distance at which a hunting NPC stops walking and just shoots.</summary>
    private float HuntStopDistance
    {
        get
        {
            float sight = attribute.SIGHT_Current;
            float stop = Mathf.Max(sight * huntStopRatio, dangerRadius + 0.5f);
            return Mathf.Min(stop, sight * 0.95f); // must still be able to hit
        }
    }

    private Vector2 GetFollowDirection(float dt)
    {
        if (Follow == null) return Vector2.zero;

        Vector2 to = (Vector2)Follow.position - (Vector2)_t.position;
        float d = to.magnitude;

        // Hysteresis: chase the player until close, then wander around them
        // until the player gets farther than safeRound.
        if (followMoving)
        {
            if (d <= followStopDistance)
            {
                followMoving = false;
                hasWanderTarget = false;
                wanderPause = 0f;
            }
        }
        else if (d > safeRound)
        {
            followMoving = true;
        }

        if (followMoving)
            return d > 0.0001f ? to / d : Vector2.zero;

        return GetWanderDirection((Vector2)Follow.position, dt);
    }

    /// <summary>
    /// Roams between random points around the player, resting a random time at
    /// each. The points are re-picked if the player has walked away from them.
    /// </summary>
    private Vector2 GetWanderDirection(Vector2 playerPos, float dt)
    {
        if (wanderPause > 0f)
        {
            wanderPause -= dt;
            return Vector2.zero;
        }

        float stale = wanderRadius * 1.25f;
        if (!hasWanderTarget || (wanderTarget - playerPos).sqrMagnitude > stale * stale)
            PickWanderTarget(playerPos);

        Vector2 to = wanderTarget - (Vector2)_t.position;
        float d = to.magnitude;

        if (d <= wanderArriveDistance)
        {
            hasWanderTarget = false;
            wanderPause = RNG.GetFloat(wanderPauseMin, wanderPauseMax);
            return Vector2.zero;
        }

        return to / d;
    }

    private void PickWanderTarget(Vector2 playerPos)
    {
        float angle = RNG.GetFloat(0f, Mathf.PI * 2f);
        float dist = Mathf.Sqrt(RNG.GetFloat(0f, 1f)) * wanderRadius;
        Vector2 p = playerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

        wanderTarget = dm != null ? dm.ClampToMap(p) : p;
        hasWanderTarget = true;
    }

    /// <summary>
    /// Feedback loop: nudges a flat speed bonus until the NPC's SPEED_Current
    /// equals the player's (plus any flee / catch-up boost on top). Runs every
    /// threat scan, so it follows the player's own slows and buffs too.
    /// Paused while the NPC is staggered, otherwise it would cancel the slow.
    /// </summary>
    private void SyncSpeedWithPlayer()
    {
        if (!matchPlayerSpeed || pm == null || pm.attribute == null || attribute == null) return;
        if (staggerApplied) return;

        float err = pm.attribute.SPEED_Current + appliedSpeed - attribute.SPEED_Current;
        if (Mathf.Abs(err) < 0.05f) return;

        attribute.SPEED_Ampl.FlatBonus += err;
        appliedSync += err;
    }

    private void UpdateSpeedBonus()
    {
        float desired = 0f;
        if (CurrentMode == Mode.Fleeing) desired = fleeSpeedBonus;
        else if (Follow != null && lastFollowDist > catchUpDistance) desired = catchUpSpeedBonus;

        if (Mathf.Approximately(desired, appliedSpeed)) return;
        attribute.SPEED_Ampl.FlatBonus += desired - appliedSpeed;
        appliedSpeed = desired;
    }
    #endregion

    #region Combat
    /// <summary>Seconds between shots, derived from the ASPD (attack speed) stat.</summary>
    private float AttackCooldown
    {
        get
        {
            float cd = 0.1f / attribute.ASPD_Current;
            return Mathf.Clamp(cd, minAttackCooldown, maxAttackCooldown);
        }
    }

    /// <summary>
    /// Returns true while the NPC has a valid enemy in range (so it keeps facing it),
    /// even if it can't fire right now because it is reloading.
    /// </summary>
    private bool TickAttack(float dt, out Vector2 aimDir)
    {
        aimDir = Vector2.zero;
        attackTimer -= dt;

        if (!CanAttack || Target == null) return false;
        if (CurrentMode == Mode.Fleeing && !attackWhileFleeing) return false;
        if (targetUnit == null || targetUnit.IsDead || !Target.gameObject.activeInHierarchy) return false;

        Vector2 to = (Vector2)Target.position - (Vector2)_t.position;
        float range = attribute.SIGHT_Current;
        if (to.sqrMagnitude > range * range || to.sqrMagnitude < 0.0001f) return false;

        aimDir = to.normalized;

        // Reloading or empty: keep aiming, but no shot.
        if (!CanShoot) return true;

        if (attackTimer <= 0f)
        {
            attackTimer = AttackCooldown;
            Shoot(aimDir, range);
        }
        return true;
    }

    /// <summary>One shot, presented like the player's weapon. Uses ammo.</summary>
    private void Shoot(Vector2 aimDir, float range)
    {
        if (!infiniteAmmo) SetAmmo(currentAmmo - ammoPerShot);


        PlayAttackSound();
        PlayMuzzleEffect();

        Vector2 dir = spreadAngle > 0f
            ? RotateDirection(aimDir, RNG.GetFloat(-spreadAngle, spreadAngle))
            : aimDir;

        PulseIndicator();

        AttackResult result = Attack.Shoot(gameObject, attribute, range, dir, pm.EnemyMask);

        PlayBulletVisual(dir, result, range);
        PlayHitEffect(result);
    }

    private Vector2 MuzzlePos => muzzle != null ? (Vector2)muzzle.position : (Vector2)_t.position;

    private void PlayMuzzleEffect()
    {
        if (shootEffect == null || PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(shootEffect);
        if (o == null) return;

        o.transform.position = MuzzlePos;
        o.transform.rotation = _t.rotation;
    }

    private void PlayBulletVisual(Vector2 dir, AttackResult result, float range)
    {
        if (bulletPrefab == null || PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(bulletPrefab.gameObject);
        if (o == null) return;

        o.transform.position = MuzzlePos;
        o.transform.rotation = _t.rotation;

        Vector2 targetPoint = result.DidHit
            ? result.HitPoint
            : MuzzlePos + dir.normalized * range;

        if (o.TryGetComponent(out Bullet b))
            b.Launch(targetPoint, bulletSpeed, bulletLifetime, result.DidHit);
    }

    private void PlayHitEffect(AttackResult result)
    {
        if (!result.DidHit) return;

        DamagePopupManager.Show(result.HitPoint, result.Damage, result.IsCrit);

        GameObject prefab = package?.Media?.HitEffect;
        if (prefab == null || PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(prefab);
        if (o != null) o.transform.position = result.HitPoint;
    }

    private static Vector2 RotateDirection(Vector2 dir, float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(
            dir.x * cos - dir.y * sin,
            dir.x * sin + dir.y * cos
        );
    }
    #endregion

    #region Aim Indicator
    // Mirrors Weapon.HandleIndicator: a pooled marker sits on the attack target,
    // pops bigger on every shot and eases back to its prefab scale.
    private void SpawnIndicator()
    {
        if (indicatorPrefab == null || PoolingSystem.instance == null) return;

        indicator = PoolingSystem.instance.GetFromPool(indicatorPrefab);
        if (indicator != null) indicator.SetActive(false);
    }

    private void DestroyIndicator()
    {
        if (indicator != null && PoolingSystem.instance != null)
            PoolingSystem.instance.DestroyObject(indicator);
        indicator = null;
    }

    private void HandleIndicator(float dt)
    {
        if (indicator == null) return;

        bool show = Target != null
                    && targetUnit != null
                    && !targetUnit.IsDead
                    && Target.gameObject.activeInHierarchy;

        if (indicator.activeSelf != show) indicator.SetActive(show);
        if (!show) return;

        Transform it = indicator.transform;
        it.position = Target.position;

        Vector3 normal = indicatorPrefab.transform.localScale;
        if (it.localScale == normal) return;
        it.localScale = Vector3.LerpUnclamped(it.localScale, normal, dt * indicatorReturnSpeed);
    }

    private void PulseIndicator()
    {
        if (indicator == null || indicatorPrefab == null) return;
        indicator.transform.localScale = indicatorPrefab.transform.localScale * indicatorPulseMultiplier;
    }
    #endregion

    #region Audio
    private void PlayAttackSound() => Play(package?.Media?.Audio?.GetAttackSound());
    private void PlayDeathSound() => Play(package?.Media?.Audio?.GetDeathSound());

    private void Play(AudioClip clip)
    {
        if (AudioManager.instance == null || clip == null) return;
        AudioManager.instance.PlayAudio(clip, _t.position);
    }
    #endregion
}