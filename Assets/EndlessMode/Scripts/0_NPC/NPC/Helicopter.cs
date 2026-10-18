using System;
using Server;
using UnityEngine;

/// <summary>
/// Helicopter NPC. Inherits everything from NpcBrain (follow, hunt, flee, dodge,
/// stat matching, normal gun attack with its own magazine) and adds:
///   - HEAVY MOVEMENT: inertia. She speeds up slowly, drifts when she stops and
///     turns at a limited rate, so she feels like a heavy machine,
///   - a MISSILE system:
///       - holds up to maxMissiles (default 8),
///       - each launch fires missilesPerLaunch (default 2) at once, with
///         launchCooldown (default 0.5s) between launches,
///       - the normal attack keeps working while missiles fly (separate systems),
///       - missiles only come back by RELOADING, which refills the whole rack:
///           * empty rack      -> reload starts automatically,
///           * TACTICAL reload -> while idle (no target, not fleeing) and the rack is
///                                at or below missileTacticalReloadThreshold.
///       - she can't launch while the rack is reloading.
///
/// Put this on the helicopter prefab INSTEAD of NpcBrain (it is an NpcBrain).
///
/// REQUIRES the ShapeMovement / ShapeFacing hooks in NpcBrain.
///
/// NOTE: every serialized field here has a unique name. Unity flattens the whole
/// inheritance chain, so a field named like a private serialized field in
/// NpcBrain causes a "serialized multiple times" error.
/// </summary>
public class Helicopter : NpcBrain, IZombieIgnored
{
    [Header("Heavy Movement (Inertia)")]
    [SerializeField] private bool useInertia = true;
    [Tooltip("Seconds to reach full speed from a stop. Higher = heavier, slower to get going (and to change direction).")]
    [SerializeField, Min(0f)] private float accelerationTime = 1.2f;
    [Tooltip("Seconds to drift to a stop once she no longer wants to move. Higher = more drift.")]
    [SerializeField, Min(0f)] private float brakeTime = 1.8f;
    [Tooltip("Max rotation speed in degrees per second. Lower = slower to turn.")]
    [SerializeField, Min(1f)] private float turnSpeed = 90f;

    [Header("Missiles - Rack")]
    [SerializeField] private bool useMissiles = true;
    [SerializeField, Min(1)] private int maxMissiles = 8;
    [Tooltip("Missiles fired per launch.")]
    [SerializeField, Min(1)] private int missilesPerLaunch = 2;
    [Tooltip("Seconds between two launches.")]
    [SerializeField, Min(0f)] private float launchCooldown = 0.5f;

    [Header("Missiles - Reload")]
    [Tooltip("Seconds to refill the whole rack.")]
    [SerializeField, Min(0.1f)] private float missileReloadTime = 5f;
    [Tooltip("Reload automatically when the rack is empty.")]
    [SerializeField] private bool missileAutoReload = true;
    [Tooltip("Also reload while idle (no target, not fleeing) when low on missiles.")]
    [SerializeField] private bool missileTacticalReload = true;
    [Tooltip("Idle reload starts at or below this fraction of the rack. 1 = reload any time one is missing.")]
    [SerializeField, Range(0f, 1f)] private float missileTacticalReloadThreshold = 0.5f;
    [SerializeField] private AudioClip missileReloadSound;
    [SerializeField] private GameObject missileReloadEffect;

    [Header("Missiles - Launch")]
    [SerializeField] private GameObject missilePrefab;
    [Tooltip("Fires only when the target is closer than this.")]
    [SerializeField] private float missileRange = 25f;
    [Tooltip("Missile damage = her ATK x this.")]
    [SerializeField] private float missileDamageMultiplier = 2f;
    [SerializeField] private float missileRadius = 3.5f;
    [Tooltip("Damage multiplier at the very edge of the blast (1 = no falloff).")]
    [SerializeField, Range(0f, 1f)] private float missileEdgeDamage = 0.5f;
    [Tooltip("Launch points (e.g. left and right pylon). Missile i uses point i. Falls back to her position.")]
    [SerializeField] private Transform[] missilePoints;
    [Tooltip("Sideways offset used when there are no launch points.")]
    [SerializeField] private float fallbackPointOffset = 0.6f;
    [Tooltip("Missiles in a salvo leave at +/- this angle, then home in.")]
    [SerializeField] private float salvoSpreadAngle = 18f;
    [SerializeField] private AudioClip launchSound;

    // Inertia state
    private Vector2 velocity;   // current movement as a fraction of full speed (0..1 long)
    private Vector2 facing = Vector2.right;

    // Rack state
    private int missileCount;
    private float launchTimer;
    private bool missileReloading;
    private float missileReloadTimer;

    #region Heavy movement
    /// <summary>
    /// Smooths the wanted movement direction. Speeding up uses accelerationTime,
    /// letting go uses brakeTime, so she keeps drifting after she stops "wanting" to move.
    /// </summary>
    protected override Vector2 ShapeMovement(Vector2 desired, float dt)
    {
        if (!useInertia) return desired;

        float time = desired.sqrMagnitude > 0.0001f ? accelerationTime : brakeTime;
        float rate = time > 0.0001f ? 1f / time : 1000f;

        velocity = Vector2.MoveTowards(velocity, desired, rate * dt);

        // Snap to rest so she doesn't creep forever.
        if (desired == Vector2.zero && velocity.sqrMagnitude < 0.0004f)
            velocity = Vector2.zero;

        return velocity;
    }

    /// <summary>Turns toward the wanted facing at a limited speed (turnSpeed deg/s).</summary>
    protected override Vector2 ShapeFacing(Vector2 desired, float dt)
    {
        if (!useInertia) return desired;
        if (desired.sqrMagnitude < 0.0001f) return Vector2.zero;

        float current = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
        float target = Mathf.Atan2(desired.y, desired.x) * Mathf.Rad2Deg;
        float next = Mathf.MoveTowardsAngle(current, target, turnSpeed * dt) * Mathf.Deg2Rad;

        facing = new Vector2(Mathf.Cos(next), Mathf.Sin(next));
        return facing;
    }

    /// <summary>A dodge is an instant burst: drop the drift so it doesn't add on top.</summary>
    protected override void OnDodged()
    {
        base.OnDodged();
        velocity = Vector2.zero;
    }
    #endregion

    #region Missile API
    /// <summary>Fired when the rack changes: (current, max). Cleared when she is removed.</summary>
    public event Action<int, int> OnMissilesChanged;
    /// <summary>Fired when a missile launch happens: number of missiles fired.</summary>
    public event Action<int> OnMissilesLaunched;
    public event Action OnMissileReloadStartedEvent;
    public event Action OnMissileReloadFinishedEvent;

    public int MissileCount => missileCount;
    public int MaxMissiles => maxMissiles;
    public bool IsMissileReloading => missileReloading;
    public bool IsRackEmpty => missileCount <= 0;
    public bool IsRackFull => missileCount >= maxMissiles;
    public bool CanLaunch => useMissiles && !missileReloading && missileCount > 0 && launchTimer <= 0f;

    /// <summary>Reload progress 0..1 (0 when not reloading). Handy for a UI bar.</summary>
    public float MissileReloadProgress =>
        missileReloading ? 1f - Mathf.Clamp01(missileReloadTimer / missileReloadTime) : 0f;

    /// <summary>Starts a reload. Returns false if already reloading, full, or not allowed.</summary>
    public bool StartMissileReload()
    {
        if (!IsActive || attribute == null || attribute.IsDead) return false;
        if (missileReloading || missileCount >= maxMissiles) return false;

        missileReloading = true;
        missileReloadTimer = missileReloadTime;

        if (missileReloadSound != null && AudioManager.instance != null)
            AudioManager.instance.PlayAudio(missileReloadSound, transform.position);
        if (missileReloadEffect != null && PoolingSystem.instance != null)
        {
            GameObject fx = PoolingSystem.instance.GetFromPool(missileReloadEffect);
            if (fx != null) { fx.transform.position = transform.position; fx.transform.rotation = transform.rotation; }
        }

        OnMissileReloadStarted();
        OnMissileReloadStartedEvent?.Invoke();
        return true;
    }

    /// <summary>Stops a running reload without giving missiles.</summary>
    public void CancelMissileReload()
    {
        missileReloading = false;
        missileReloadTimer = 0f;
    }

    /// <summary>Instantly fills the rack (cancels any reload).</summary>
    public void RefillMissiles()
    {
        CancelMissileReload();
        SetMissiles(maxMissiles);
    }

    /// <summary>Adds missiles (clamped to max). Returns how many were really added.</summary>
    public int AddMissiles(int amount)
    {
        if (amount <= 0) return 0;
        int before = missileCount;
        SetMissiles(missileCount + amount);
        return missileCount - before;
    }

    /// <summary>Changes the rack size at runtime (e.g. from a card). Optionally refills.</summary>
    public void SetMaxMissiles(int newMax, bool refill = false)
    {
        maxMissiles = Mathf.Max(1, newMax);
        if (refill) RefillMissiles();
        else SetMissiles(Mathf.Min(missileCount, maxMissiles));
    }

    public void SetMissilesPerLaunch(int n) => missilesPerLaunch = Mathf.Max(1, n);
    public void SetLaunchCooldown(float seconds) => launchCooldown = Mathf.Max(0f, seconds);
    public void SetMissileReloadTime(float seconds) => missileReloadTime = Mathf.Max(0.1f, seconds);

    private void SetMissiles(int value)
    {
        value = Mathf.Clamp(value, 0, maxMissiles);
        if (value == missileCount) return;

        missileCount = value;
        OnMissilesChanged?.Invoke(missileCount, maxMissiles);
    }

    /// <summary>Hook: a missile reload started.</summary>
    protected virtual void OnMissileReloadStarted() { }
    /// <summary>Hook: a missile reload finished (rack already full).</summary>
    protected virtual void OnMissileReloadFinished() { }
    /// <summary>Hook: a salvo was launched.</summary>
    protected virtual void OnMissilesFired(int count) { }
    #endregion

    #region NpcBrain hooks
    protected override void OnSpawned()
    {
        base.OnSpawned();

        velocity = Vector2.zero;
        facing = transform.right;

        launchTimer = 0f;
        missileReloading = false;
        missileReloadTimer = 0f;
        missileCount = maxMissiles; // starts full
        OnMissilesChanged?.Invoke(missileCount, maxMissiles);
    }

    protected override void OnRemoved()
    {
        CancelMissileReload();
        velocity = Vector2.zero;

        // Pooled object: don't keep old subscribers (UI etc.) between lives.
        OnMissilesChanged = null;
        OnMissilesLaunched = null;
        OnMissileReloadStartedEvent = null;
        OnMissileReloadFinishedEvent = null;

        base.OnRemoved();
    }

    public override void Tick(float dt)
    {
        base.Tick(dt); // normal gun attack, movement, etc. continue as usual
        TickMissiles(dt);
    }
    #endregion

    #region Missile logic
    private void TickMissiles(float dt)
    {
        if (!useMissiles || TickSystem.GameSpeed <= 0f) return;
        if (!IsActive || attribute == null || attribute.IsDead) return;

        if (launchTimer > 0f) launchTimer -= dt;

        // Reloading: no launching until the rack is full again.
        if (missileReloading)
        {
            missileReloadTimer -= dt;
            if (missileReloadTimer <= 0f) FinishMissileReload();
            return;
        }

        // Rack is used up: reload.
        if (missileAutoReload && missileCount <= 0)
        {
            StartMissileReload();
            return;
        }

        // Tactical reload: quiet moment and the rack is low.
        if (missileTacticalReload
            && Target == null
            && CurrentMode != Mode.Fleeing
            && !IsDodging
            && missileCount < maxMissiles
            && missileCount <= Mathf.CeilToInt(maxMissiles * missileTacticalReloadThreshold))
        {
            StartMissileReload();
            return;
        }

        // Launch
        if (missileCount <= 0 || launchTimer > 0f) return;
        if (Target == null || !Target.gameObject.activeInHierarchy) return;

        Vector2 origin = transform.position;
        if (((Vector2)Target.position - origin).sqrMagnitude >= missileRange * missileRange) return;

        LaunchSalvo();
    }

    private void FinishMissileReload()
    {
        missileReloading = false;
        missileReloadTimer = 0f;
        SetMissiles(maxMissiles);

        OnMissileReloadFinished();
        OnMissileReloadFinishedEvent?.Invoke();
    }

    /// <summary>Fires one salvo at the current target. Returns the number of missiles launched.</summary>
    public int LaunchSalvo()
    {
        if (!CanLaunch || Target == null) return 0;

        int n = Mathf.Min(missilesPerLaunch, missileCount);
        float dmg = attribute.ATK_Current * missileDamageMultiplier;
        int fired = 0;

        for (int i = 0; i < n; i++)
        {
            Vector2 from = GetLaunchPoint(i, n);
            Vector2 toTarget = (Vector2)Target.position - from;
            Vector2 baseDir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : (Vector2)transform.right;

            // Spread: alternate sides so the pair leaves in a V and then homes in.
            float side = n == 1 ? 0f : (i % 2 == 0 ? -1f : 1f);
            Vector2 dir = Rotate(baseDir, side * salvoSpreadAngle);

            Missile m = Missile.Launch(missilePrefab, from, dir, Target, attribute,
                dmg, missileRadius, missileEdgeDamage, pm.EnemyMask);

            if (m == null) break; // no prefab / pool problem: don't spend more
            fired++;
        }

        if (fired <= 0) return 0;

        SetMissiles(missileCount - fired);
        launchTimer = launchCooldown;

        if (launchSound != null && AudioManager.instance != null)
            AudioManager.instance.PlayAudio(launchSound, transform.position);

        OnMissilesFired(fired);
        OnMissilesLaunched?.Invoke(fired);
        return fired;
    }

    private Vector2 GetLaunchPoint(int index, int total)
    {
        if (missilePoints != null && missilePoints.Length > 0)
        {
            Transform p = missilePoints[index % missilePoints.Length];
            if (p != null) return p.position;
        }

        // Fallback: spread sideways around her position.
        float offset = total <= 1 ? 0f : (index - (total - 1) * 0.5f) * 2f * fallbackPointOffset / Mathf.Max(1, total - 1);
        Vector2 side = new Vector2(-transform.right.y, transform.right.x);
        return (Vector2)transform.position + side * offset;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    [ContextMenu("Test Launch Salvo")]
    private void Test_Launch() => LaunchSalvo();

    [ContextMenu("Test Refill Missiles")]
    private void Test_Refill() => RefillMissiles();
    #endregion
}