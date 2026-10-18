using System;
using Server;
using UnityEngine;

/// <summary>
/// Rifle zombie. Instead of biting, it shoots bursts at its target:
///  - Every 2~4s it starts a burst. Burst size is 4~8 bullets, with a chance to be DOUBLED.
///  - Wind-up with a CONE aim line (like SoldierZom): the line widens toward the target to show the
///    real bullet spread, fades start->end colour, narrows as the aim focuses, and BLINKS at the end.
///  - Aim LOCKS during the last lockAimTime seconds so the player can dodge.
///  - Spread = focus + target speed + distance + flinch + recoil per bullet (same value for line and bullets).
///  - Range = this zombie's SIGHT_Current. Optional walls (sightBlockMask) stop shots and need line of sight.
///  - Repositions: backs away while strafing when the target is close, walks closer when too far.
///  - Can hit FLYING targets (enemyMask + flyingMask) and picks flying friendlies as prey.
/// </summary>
public class Rifle_Zom : Zombrain
{
    public enum LineLengthMode { ToTarget, FullRange, ToTargetPlusOvershoot }

    #region Inspector
    [Header("Burst")]
    [SerializeField] private float burstCooldownMin = 2f;
    [SerializeField] private float burstCooldownMax = 4f;
    [SerializeField, Min(1)] private int burstMin = 4;
    [SerializeField, Min(1)] private int burstMax = 8;
    [Tooltip("Chance (0..1) that a burst has DOUBLE the bullets.")]
    [SerializeField, Range(0f, 1f)] private float doubleBurstChance = 0.3f;
    [Tooltip("Seconds between bullets inside a burst.")]
    [SerializeField] private float shotInterval = 0.09f;

    [Header("Wind-up")]
    [Tooltip("Total aiming time before the first bullet. The line is visible for all of it.")]
    [SerializeField] private float windUp = 0.6f;
    [Tooltip("Aim is FROZEN during the last X seconds of the wind-up (0 = always tracking).")]
    [SerializeField] private float lockAimTime = 0.25f;
    [Tooltip("ON: every bullet re-aims at the target. OFF: the whole burst uses the locked direction.")]
    [SerializeField] private bool trackWhileFiring = false;
    [Tooltip("Aim at the center of the target's collider instead of its pivot.")]
    [SerializeField] private bool aimAtColliderCenter = true;
    [Tooltip("NPCs get warned this many seconds before the first bullet so they can dodge.")]
    [SerializeField] private float npcWarnTime = 0.3f;

    [Header("Warning Line - General")]
    [Tooltip("Your own LineRenderer (world space). If empty and Auto Create is ON, one is built.")]
    [SerializeField] private LineRenderer warningLine;
    [SerializeField] private bool autoCreateLine = true;
    [Tooltip("Apply the colour/width/material settings below to a LineRenderer you assigned yourself.")]
    [SerializeField] private bool styleAssignedLine = true;
    [Tooltip("Optional. Empty = built-in Sprites/Default shader.")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private int lineSortingOrder = 50;

    [Header("Warning Line - Look")]
    [SerializeField] private Color aimColorStart = new Color(1f, 0.2f, 0.2f, 0.15f);
    [SerializeField] private Color aimColorEnd = new Color(1f, 0.1f, 0.1f, 0.9f);
    [Tooltip("Width at the zombie's muzzle.")]
    [SerializeField] private float lineStartWidth = 0.04f;
    [Tooltip("Minimum width at the far end (the cone never gets thinner than this).")]
    [SerializeField] private float lineEndMinWidth = 0.04f;
    [SerializeField] private LineLengthMode lineLength = LineLengthMode.ToTargetPlusOvershoot;
    [Tooltip("Extra length past the target (ToTargetPlusOvershoot).")]
    [SerializeField] private float lineOvershoot = 3f;

    [Header("Warning Line - Cone (shows spread)")]
    [SerializeField] private bool useCone = true;
    [Tooltip("The far end of the line never gets wider than this.")]
    [SerializeField] private float coneMaxWidth = 3f;
    [Tooltip("Scales the cone width (1 = the true spread).")]
    [SerializeField] private float coneWidthMultiplier = 1f;

    [Header("Warning Line - Blink")]
    [Tooltip("The line BLINKS during the last X seconds of the wind-up.")]
    [SerializeField] private float blinkDuration = 0.3f;
    [Tooltip("Blinks (on+off cycles) per second.")]
    [SerializeField] private float blinkRate = 10f;
    [Tooltip("ON: blink by hiding the line. OFF: blink by swapping to the blink colour.")]
    [SerializeField] private bool blinkHidesLine = true;
    [SerializeField] private Color blinkColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Line colour while the aim is frozen (ignored if 0 alpha).")]
    [SerializeField] private Color lockedColor = new Color(1f, 0f, 0f, 1f);

    [Header("Tracer Line (after each shot)")]
    [SerializeField] private bool showTracer = false;
    [SerializeField] private Color tracerColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private float tracerWidth = 0.08f;
    [SerializeField] private float tracerDuration = 0.06f;

    [Header("Weapon (range = SIGHT_Current)")]
    [Tooltip("Damage per bullet as a fraction of ATK.")]
    [SerializeField] private float damageMultiplier = 0.35f;
    [Tooltip("Spread (+/- degrees) at the very start of the wind-up.")]
    [SerializeField] private float maxSpread = 9f;
    [Tooltip("Spread once fully focused (end of the wind-up and during the burst).")]
    [SerializeField] private float minSpread = 3f;
    [Tooltip("Extra degrees per unit/second of target speed.")]
    [SerializeField] private float spreadPerTargetSpeed = 0.35f;
    [Tooltip("Extra degrees per unit of distance.")]
    [SerializeField] private float spreadPerDistance = 0.1f;
    [Tooltip("Extra degrees added by every bullet fired in the burst (recoil).")]
    [SerializeField] private float recoilPerShot = 0.4f;
    [SerializeField] private float spreadHardCap = 20f;
    [Tooltip("ON: bullets cluster near the centre. OFF: uniform inside the cone.")]
    [SerializeField] private bool centerBiasedSpread = true;
    [Tooltip("Being hit during the wind-up adds this much spread (stacks up to x3) ...")]
    [SerializeField] private float flinchExtraSpread = 2f;
    [Tooltip("... and delays the first bullet by this many seconds.")]
    [SerializeField] private float flinchAimDelay = 0.2f;

    [Header("Walls / Line of sight")]
    [Tooltip("Optional: walls/obstacles. If set, a burst needs a clear line and shots + line stop at walls.")]
    [SerializeField] private LayerMask sightBlockMask;

    [Header("Flying Targets")]
    [SerializeField] private bool targetFlying = true;
    [Tooltip("Layers of flying units. Added to the shot mask and used to find flying prey.")]
    [SerializeField] private LayerMask flyingMask;

    [Header("Reposition (ratios of SIGHT_Current)")]
    [Tooltip("Walks closer when the target is farther than Range x this.")]
    [SerializeField, Range(0.3f, 1f)] private float approachRatio = 0.9f;
    [Tooltip("Backs away when the target is closer than Range x this.")]
    [SerializeField, Range(0.1f, 0.9f)] private float retreatRatio = 0.45f;
    [Tooltip("Seconds it may back away in one go.")]
    [SerializeField] private float retreatDuration = 1.2f;
    [Tooltip("After a retreat it must stand its ground for this long.")]
    [SerializeField] private float retreatCooldown = 2.5f;
    [Tooltip("Sideways share of the retreat (0 = straight back).")]
    [SerializeField, Range(0f, 1.5f)] private float strafeWeight = 0.6f;
    [Tooltip("Allow moving while a burst is being fired / aimed.")]
    [SerializeField] private bool moveWhileBursting = false;

    [Header("Visual / Audio")]
    [Tooltip("Where bullets come from. Falls back to the zombie's position.")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private Bullet bulletPrefab;
    [SerializeField] private float bulletSpeed = 90f;
    [SerializeField] private GameObject muzzleFlash;
    [Tooltip("Per-bullet sound. If empty, the package attack sound plays once per burst.")]
    [SerializeField] private AudioClip fireSound;
    #endregion

    #region Public API
    public bool IsBursting => bursting;
    public bool IsWindingUp => bursting && clock < nextShotTime;
    /// <summary>0..1 during the wind-up.</summary>
    public float WindUpProgress => IsWindingUp ? Mathf.Clamp01(1f - (nextShotTime - clock) / Mathf.Max(0.0001f, windUpTotal)) : 0f;
    public int ShotsLeft => shotsLeft;

    public event Action OnBurstStart;
    public event Action<AttackResult> OnShoot;
    public event Action OnBurstEnd;

    /// <summary>Cancels the current burst (stun, knockback, etc).</summary>
    public void InterruptBurst()
    {
        if (bursting) StopBurst();
    }
    #endregion

    #region Runtime
    private static readonly int WalkHash = Animator.StringToHash("IsWalking");
    private static readonly int ShootHash = Animator.StringToHash("IsAttacking");

    private float clock;   // own clock, advanced by Tick(dt)
    private float tickDt;

    // Burst
    private bool bursting;
    private int shotsLeft;
    private float nextShotTime;
    private float nextBurstTime;
    private float windStart;
    private float windUpTotal;
    private bool npcWarned;
    private Vector2 lockedDir;
    private Collider2D aimCollider;
    private Transform aimColliderOwner;

    // Accuracy
    private float flinchSpread;
    private float recoil;
    private float targetSpeed;
    private Vector2 lastAimPoint;
    private float lastAimTime;

    // Line of sight
    private bool hasLineOfSight = true;
    private float nextLosCheck;

    // Reposition
    private float retreatLeft;
    private float retreatReadyAt;
    private float strafeSign = 1f;
    private float strafeFlipAt;
    private float sepTimer;
    private Vector2 sepForce;
    private bool movedThisTick;

    // Lines
    private LineRenderer tracerLine;
    private float tracerHideAt;

    private readonly Collider2D[] flyBuf = new Collider2D[16];

    private float Range => attribute != null ? attribute.SIGHT_Current : 0f;
    private LayerMask ShootMask => hb.enemyMask | flyingMask;
    private Vector2 MuzzlePos => muzzle != null ? (Vector2)muzzle.position : (Vector2)_t.position;
    #endregion

    #region Lifecycle
    protected override void Awake()
    {
        base.Awake();

        if (warningLine == null && autoCreateLine)
        {
            warningLine = BuildLine("WarningLine", lineStartWidth, lineSortingOrder);
        }
        else if (warningLine != null && styleAssignedLine)
        {
            warningLine.useWorldSpace = true;
            warningLine.positionCount = 2;
            warningLine.sortingOrder = lineSortingOrder;
            if (lineMaterial != null) warningLine.material = lineMaterial;
        }
        if (warningLine != null) warningLine.enabled = false;

        if (showTracer)
        {
            tracerLine = BuildLine("TracerLine", tracerWidth, lineSortingOrder + 1);
            tracerLine.startColor = tracerLine.endColor = tracerColor;
        }
    }

    public override void Tick(float dt)
    {
        clock += dt;
        tickDt = dt;
        base.Tick(dt);

        if (tracerLine != null && tracerLine.enabled && clock >= tracerHideAt)
            tracerLine.enabled = false;
    }

    public override bool SpawnObject(ZomPackage zp)
    {
        if (!base.SpawnObject(zp)) return false;

        ClearBurstState();
        nextBurstTime = clock + RollCooldown();
        retreatLeft = 0f;
        retreatReadyAt = 0f;
        strafeSign = RNG.GetPercent() < 0.5f ? 1f : -1f;
        strafeFlipAt = 0f;
        sepTimer = 0f;
        sepForce = Vector2.zero;
        movedThisTick = false;
        return true;
    }

    protected override void Despawn()
    {
        HideWarning();
        HideTracer();
        base.Despawn();
    }

    public override void Reboot()
    {
        ClearBurstState();
        HideTracer();
        base.Reboot();
    }

    private void ClearBurstState()
    {
        bursting = false;
        shotsLeft = 0;
        nextShotTime = 0f;
        npcWarned = false;
        flinchSpread = 0f;
        recoil = 0f;
        targetSpeed = 0f;
        hasLineOfSight = true;
        nextLosCheck = 0f;
        aimCollider = null;
        aimColliderOwner = null;
        HideWarning();
    }
    #endregion

    #region Burst
    private float RollCooldown() => RNG.GetFloat(burstCooldownMin, Mathf.Max(burstCooldownMin, burstCooldownMax));

    protected override void TryAttack()
    {
        if (!CanAttack || Target == null || attribute == null)
        {
            if (bursting) StopBurst();
            return;
        }

        UpdateLineOfSight();

        if (bursting)
        {
            TickBurst();
            return;
        }

        if (clock < nextBurstTime) return;
        if (LiveDistance() > Range) return;
        if (!hasLineOfSight) return;

        StartBurst();
    }

    private void StartBurst()
    {
        bursting = true;

        int count = RNG.GetInt(burstMin, Mathf.Max(burstMin, burstMax) + 1); // max is exclusive
        if (RNG.GetPercent() < doubleBurstChance) count *= 2;
        shotsLeft = count;

        windUpTotal = Mathf.Max(windUp, blinkDuration, 0.01f);
        windStart = clock;
        nextShotTime = clock + windUpTotal;

        npcWarned = false;
        flinchSpread = 0f;
        recoil = 0f;
        targetSpeed = 0f;

        CacheAimCollider();
        lastAimPoint = AimPoint();
        lastAimTime = clock;
        lockedDir = AimDir(MuzzlePos);

        if (fireSound == null) PlayAttackSound();
        OnBurstStart?.Invoke();
    }

    private void StopBurst()
    {
        bool wasBursting = bursting;
        HideWarning();
        bursting = false;
        shotsLeft = 0;
        recoil = 0f;
        flinchSpread = 0f;
        nextBurstTime = clock + RollCooldown();
        if (wasBursting) OnBurstEnd?.Invoke();
    }

    private void TickBurst()
    {
        if (Target == null || !Target.gameObject.activeInHierarchy || !IsValidPrey(Target))
        {
            StopBurst();
            return;
        }

        // Wind-up: face the target and show the cone warning line.
        if (clock < nextShotTime)
        {
            if (!hasLineOfSight || LiveDistance() > Range * 1.3f)
            {
                StopBurst();
                return;
            }

            float left = nextShotTime - clock;
            bool locked = left <= lockAimTime;

            UpdateTargetSpeed();

            if (!locked) lockedDir = AimDir(MuzzlePos);
            FaceDir(lockedDir);

            if (!npcWarned && left <= npcWarnTime)
            {
                npcWarned = true;
                if (Target.TryGetComponent(out NpcBrain npc)) npc.NotifyIncomingAttack(_t.position);
            }

            UpdateWarning(left, locked);
            return;
        }

        HideWarning();
        FireShot();

        if (--shotsLeft <= 0) StopBurst();
        else nextShotTime = clock + shotInterval;
    }

    private void FireShot()
    {
        Vector2 from = MuzzlePos;
        Vector2 aim = trackWhileFiring || lockedDir == Vector2.zero ? AimDir(from) : lockedDir;
        FaceDir(aim);

        float dist = Vector2.Distance(from, AimPoint());

        // Imperfect aim: random angle inside the current spread (same value the cone showed).
        float spread = CurrentSpread(dist, clock - windStart);
        float rnd = centerBiasedSpread
            ? (RNG.GetFloat(-1f, 1f) + RNG.GetFloat(-1f, 1f)) * 0.5f
            : RNG.GetFloat(-1f, 1f);
        Vector2 dir = spread > 0f ? RotateVec(aim, rnd * spread) : aim;

        // Walls stop the bullet.
        float range = Range;
        bool wallHit = false;
        Vector2 wallPoint = Vector2.zero;
        if (sightBlockMask.value != 0)
        {
            RaycastHit2D wall = Physics2D.Raycast(from, dir, range, sightBlockMask);
            if (wall.collider != null)
            {
                wallHit = true;
                wallPoint = wall.point;
                range = wall.distance;
            }
        }

        GameObject origin = muzzle != null ? muzzle.gameObject : gameObject;
        float damage = attribute.ATK_Current * damageMultiplier;

        AttackResult rs = default;
        if (range > 0.05f)
            rs = Attack.Shoot(origin, damage, attribute, range, dir, ShootMask);

        Vector2 end;
        if (rs.DidHit) end = rs.HitPoint;
        else if (wallHit) end = wallPoint;
        else end = from + dir * range;

        SpawnBullet(from, dir, end, rs.DidHit);
        SpawnFx(muzzleFlash, from, dir);
        DrawTracer(from, end);

        if (fireSound != null && AudioManager.instance != null)
            AudioManager.instance.PlayAudio(fireSound, from);

        recoil += recoilPerShot;

        HandleHitEffect(rs);
        CountTargetHit(rs);
        OnShoot?.Invoke(rs);
    }

    /// <summary>Spread shown by the cone and used by the bullets.</summary>
    private float CurrentSpread(float dist, float aimElapsed)
    {
        float focus = windUpTotal > 0f ? Mathf.Clamp01(aimElapsed / windUpTotal) : 1f;
        float s = Mathf.Lerp(maxSpread, minSpread, focus);
        s += targetSpeed * spreadPerTargetSpeed;
        s += dist * spreadPerDistance;
        s += flinchSpread;
        s += recoil;
        return Mathf.Clamp(s, 0f, spreadHardCap);
    }

    /// <summary>Getting hit during the wind-up ruins the aim a bit.</summary>
    protected override void OnHit(float damage)
    {
        base.OnHit(damage);

        if (!bursting || clock >= nextShotTime) return;
        flinchSpread = Mathf.Min(flinchSpread + flinchExtraSpread, flinchExtraSpread * 3f);
        nextShotTime += flinchAimDelay;
        windUpTotal += flinchAimDelay;
    }

    private void UpdateTargetSpeed()
    {
        float dt = clock - lastAimTime;
        if (dt <= 0.0001f) return;

        Vector2 aim = AimPoint();
        float inst = (aim - lastAimPoint).magnitude / dt;
        targetSpeed = Mathf.Lerp(targetSpeed, inst, 1f - Mathf.Exp(-8f * dt));
        lastAimPoint = aim;
        lastAimTime = clock;
    }

    private Vector2 AimDir(Vector2 from)
    {
        Vector2 to = AimPoint() - from;
        if (to.sqrMagnitude > 0.0001f) return to.normalized;
        return Direction != Vector2.zero ? Direction : Vector2.right;
    }

    private void FaceDir(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Direction = dir;
        UnitMotion.RotateThisObject(gameObject, dir);
    }

    private void CacheAimCollider()
    {
        if (aimColliderOwner == Target && aimCollider != null) return;
        aimColliderOwner = Target;
        aimCollider = FindAimCollider(Target);
    }

    private Vector2 AimPoint()
    {
        if (Target == null) return _t.position;
        if (!aimAtColliderCenter) return Target.position;

        if (aimColliderOwner != Target) CacheAimCollider();
        if (aimCollider != null && aimCollider.enabled) return aimCollider.bounds.center;
        return Target.position;
    }
    #endregion

    #region Warning line (cone)
    /// <summary>
    /// Visible for the whole wind-up. Colour fades start->end, the far end widens to the real spread
    /// (cone), and during the last blinkDuration seconds it blinks.
    /// </summary>
    private void UpdateWarning(float timeLeft, bool locked)
    {
        if (warningLine == null || Target == null) return;

        // Blink (hide, or swap to blinkColor).
        bool blinkOn = false;
        if (timeLeft <= blinkDuration)
        {
            int phase = Mathf.FloorToInt((blinkDuration - timeLeft) * blinkRate * 2f);
            blinkOn = (phase & 1) == 0;
        }
        bool inBlink = timeLeft <= blinkDuration;

        bool visible = !(inBlink && blinkHidesLine && !blinkOn);
        if (warningLine.enabled != visible) warningLine.enabled = visible;
        if (!visible) return;

        Vector2 from = MuzzlePos;
        Vector2 dir = lockedDir != Vector2.zero ? lockedDir : AimDir(from);
        float dist = Vector2.Distance(from, AimPoint());

        float len;
        switch (lineLength)
        {
            case LineLengthMode.FullRange: len = Range; break;
            case LineLengthMode.ToTarget: len = dist; break;
            default: len = dist + lineOvershoot; break;
        }
        len = Mathf.Min(len, Range + lineOvershoot);

        if (sightBlockMask.value != 0)
        {
            RaycastHit2D wall = Physics2D.Raycast(from, dir, len, sightBlockMask);
            if (wall.collider != null) len = wall.distance;
        }

        Vector2 end = from + dir * len;
        warningLine.useWorldSpace = true;
        warningLine.positionCount = 2;
        warningLine.SetPosition(0, new Vector3(from.x, from.y, 0f));
        warningLine.SetPosition(1, new Vector3(end.x, end.y, 0f));

        // Cone width = real spread at that distance.
        float endWidth = lineEndMinWidth;
        if (useCone)
        {
            float spread = CurrentSpread(dist, clock - windStart);
            float cone = 2f * len * Mathf.Tan(spread * Mathf.Deg2Rad) * coneWidthMultiplier;
            endWidth = Mathf.Clamp(cone, lineEndMinWidth, Mathf.Max(lineEndMinWidth, coneMaxWidth));
        }
        warningLine.startWidth = lineStartWidth;
        warningLine.endWidth = endWidth;

        // Colour.
        float t = 1f - Mathf.Clamp01(timeLeft / Mathf.Max(0.0001f, windUpTotal));
        Color c = Color.Lerp(aimColorStart, aimColorEnd, t);
        if (locked && lockedColor.a > 0f) c = lockedColor;
        if (inBlink && !blinkHidesLine && blinkOn) c = blinkColor;
        warningLine.startColor = warningLine.endColor = c;
    }

    private void HideWarning()
    {
        if (warningLine != null && warningLine.enabled) warningLine.enabled = false;
    }

    private void DrawTracer(Vector2 from, Vector2 to)
    {
        if (tracerLine == null) return;
        tracerLine.SetPosition(0, new Vector3(from.x, from.y, 0f));
        tracerLine.SetPosition(1, new Vector3(to.x, to.y, 0f));
        tracerLine.startColor = tracerLine.endColor = tracerColor;
        tracerLine.enabled = true;
        tracerHideAt = clock + tracerDuration;
    }

    private void HideTracer()
    {
        if (tracerLine != null) tracerLine.enabled = false;
    }

    private LineRenderer BuildLine(string lineName, float width, int sortingOrder)
    {
        var go = new GameObject(lineName);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = width;
        lr.sortingOrder = sortingOrder;
        lr.numCapVertices = 2;
        lr.material = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
        lr.enabled = false;
        return lr;
    }
    #endregion

    #region Line of sight
    private void UpdateLineOfSight()
    {
        if (sightBlockMask.value == 0 || Target == null)
        {
            hasLineOfSight = true;
            return;
        }
        if (clock < nextLosCheck) return;
        nextLosCheck = clock + 0.15f;

        Vector2 from = MuzzlePos;
        Vector2 to = AimPoint() - from;
        float dist = to.magnitude;
        hasLineOfSight = dist < 0.01f
            || Physics2D.Raycast(from, to / dist, dist, sightBlockMask).collider == null;
    }
    #endregion

    #region Bullet visuals
    private void SpawnBullet(Vector2 from, Vector2 dir, Vector2 end, bool didHit)
    {
        if (bulletPrefab == null || PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(bulletPrefab.gameObject);
        if (o == null) return;

        o.transform.position = from;
        o.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

        float life = Vector2.Distance(from, end) / Mathf.Max(1f, bulletSpeed) + 0.1f;
        if (o.TryGetComponent(out Bullet b)) b.Launch(end, bulletSpeed, life, didHit);
    }

    private static void SpawnFx(GameObject prefab, Vector2 pos, Vector2 dir)
    {
        if (prefab == null || PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(prefab);
        if (o == null) return;

        o.transform.position = pos;
        o.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }
    #endregion

    #region Movement (approach / retreat)
    protected override void Move()
    {
        movedThisTick = false;

        if (!CanMove || Target == null || attribute == null) return;
        if (bursting && !moveWhileBursting) return;

        float range = Range;
        float dist = LiveDistance();

        // Too far (or no line of sight): walk closer with the normal zombie movement.
        if (dist > range * approachRatio || (!hasLineOfSight && !bursting))
        {
            base.Move();
            movedThisTick = true;
            return;
        }

        // Target is coming close: back away (limited by the retreat window).
        if (dist < range * retreatRatio)
        {
            if (retreatLeft <= 0f && clock >= retreatReadyAt)
                retreatLeft = retreatDuration;

            if (retreatLeft > 0f)
            {
                retreatLeft -= tickDt;
                if (retreatLeft <= 0f)
                {
                    retreatLeft = 0f;
                    retreatReadyAt = clock + retreatCooldown;
                }
                Retreat();
            }
        }
        // Otherwise: comfortable distance, stand and shoot.
    }

    private void Retreat()
    {
        Vector2 pos = _t.position;
        Vector2 away = pos - (Vector2)Target.position;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : -Direction;
        if (away == Vector2.zero) return;

        if (clock >= strafeFlipAt)
        {
            strafeSign = -strafeSign;
            strafeFlipAt = clock + RNG.GetFloat(0.8f, 1.6f);
        }

        Vector2 perp = new Vector2(-away.y, away.x);
        Vector2 dir = (away + perp * strafeSign * strafeWeight).normalized;

        // Map edge behind us: slide sideways instead of pushing into the wall.
        if (dm != null)
        {
            Vector2 probe = pos + dir * 1.5f;
            if ((dm.ClampToMap(probe) - probe).sqrMagnitude > 0.01f)
            {
                strafeSign = -strafeSign;
                dir = perp * strafeSign;
            }
        }

        if (clock >= sepTimer)
        {
            sepForce = UnitMotion.GetSeparationForce(
                _t.position,
                hb.GetNearbyZoms(this, separationRadius),
                separationRadius,
                separationStrength);
            sepTimer = clock + 0.2f;
        }

        dir = (dir + sepForce).normalized;
        UnitMotion.MoveThisObject(attribute, gameObject, dir);

        if (dm != null)
        {
            Vector3 p = _t.position;
            Vector2 c = dm.ClampToMap(p);
            _t.position = new Vector3(c.x, c.y, p.z);
        }

        movedThisTick = true;
    }

    protected override void UpdateAnimator()
    {
        if (anim == null) return;
        anim.SetBool(WalkHash, movedThisTick);
        anim.SetBool(ShootHash, bursting);
    }
    #endregion

    #region Prey (adds flying friendlies)
    protected override Transform SearchForPrey(out NpcBrain npc)
    {
        Transform best = base.SearchForPrey(out npc);
        if (!targetFlying || flyingMask.value == 0) return best;

        Vector2 pos = _t.position;
        float bestSqr = best != null
            ? ((Vector2)best.position - pos).sqrMagnitude
            : float.MaxValue;

        int count = Physics2D.OverlapCircleNonAlloc(pos, preySearchRadius, flyBuf, flyingMask);
        int ownLayer = gameObject.layer;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = flyBuf[i];
            if (col == null || col.gameObject.layer == ownLayer) continue;
            if (!col.TryGetComponent(out HurtBox hurt)) continue;

            UnitPackage pkg = hurt.Access();
            if (pkg?.attribute == null) continue;
            if (pkg.attribute.tag != UnitTag.Friendly) continue;
            if (pkg.attribute.IsDead || pkg.attribute.IsUntargetable) continue;

            float sqr = ((Vector2)col.transform.position - pos).sqrMagnitude;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            best = col.transform;
            col.TryGetComponent(out NpcBrain flyNpc); // null if it is not an NPC
            npc = flyNpc;
        }

        return best;
    }
    #endregion

    #region Helpers
    private float LiveDistance()
        => ((Vector2)Target.position - (Vector2)_t.position).magnitude;

    private static bool IsValidPrey(Transform t)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return false;

        if (t.TryGetComponent(out HurtBox hurt))
        {
            UnitAttribute ua = hurt.Access()?.attribute;
            if (ua == null || ua.IsDead || ua.IsUntargetable) return false;
        }
        return true;
    }

    private static Collider2D FindAimCollider(Transform t)
    {
        if (t == null) return null;
        Collider2D c = t.GetComponent<Collider2D>();
        if (c != null) return c;

        HurtBox h = t.GetComponentInChildren<HurtBox>();
        return h != null ? h.GetComponent<Collider2D>() : null;
    }

    private static Vector2 RotateVec(Vector2 d, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
    }
    #endregion

    #region Debug
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || attribute == null) return;

        float range = Range;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, range);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, range * approachRatio);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, range * retreatRatio);

        if (bursting && Target != null)
        {
            Vector2 from = MuzzlePos;
            Vector2 dir = lockedDir != Vector2.zero ? lockedDir : AimDir(from);
            float spread = CurrentSpread(Vector2.Distance(from, AimPoint()), clock - windStart);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(from, from + RotateVec(dir, spread) * range);
            Gizmos.DrawLine(from, from + RotateVec(dir, -spread) * range);
        }
    }
    #endregion
}