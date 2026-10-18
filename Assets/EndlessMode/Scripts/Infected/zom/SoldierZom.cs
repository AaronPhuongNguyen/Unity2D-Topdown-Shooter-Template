using Server;
using System;
using UnityEngine;

/// <summary>
/// Ranged zombie. Loop:
///   Approach -> Aim (locks target 2~4s, can't move, narrowing cone indicator) -> Shoot (hit-scan, with spread)
///   -> Reposition (walks to a random point on the edge of its shooting range around the target,
///      invincible + untargetable + big speed boost) -> Approach/Aim again.
/// Extras: retreats when the target gets too close, optional line-of-sight check, flinches when hit while aiming,
/// can target flying units (separate layer mask).
/// </summary>
public class SoldierZom : Zombrain
{
    public enum SoldierState { Approach, Aiming, Reposition }
    private enum RepositionKind { AfterShot, Retreat }

    #region Settings
    [Header("Soldier - Weapon")]
    [Tooltip("Aim is dropped if the locked target gets farther than shootRange * this.")]
    [SerializeField] private float aimBreakMultiplier = 1.3f;
    [SerializeField] private Vector2 aimDuration = new Vector2(2f, 4f);
    [Tooltip("NPCs get warned this many seconds before the shot so they can dodge.")]
    [SerializeField] private float npcWarnTime = 0.3f; [Header("Soldier - Muzzle Flash")]
    [Tooltip("Where the flash appears. Falls back to the zombie's position.")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Pooled effect prefab spawned on every shot (same kind you use in Rifle_Zom / NPCs).")]
    [SerializeField] private GameObject muzzleFlash;
    [Tooltip("Optional: start the shot line at the muzzle instead of the zombie's centre.")]
    [SerializeField] private bool shotLineFromMuzzle = true;
    private float shootRange => attribute.SIGHT_Current;

    [Header("Soldier - Too Close (Retreat)")]
    [Tooltip("If the target is closer than shootRange * this, back off instead of aiming. 0 = never retreat.")]
    [SerializeField, Range(0f, 0.9f)] private float minRangeFraction = 0.35f;
    [SerializeField] private float retreatCooldown = 4f;
    [Tooltip("Flat speed added while retreating (no invincibility).")]
    [SerializeField] private float retreatSpeedBonus = 40f;

    [Header("Soldier - Reposition")]
    [SerializeField] private Vector2 repositionDuration = new Vector2(1f, 3f);
    [Tooltip("Flat speed added during reposition after a shot.")]
    [SerializeField] private float repositionSpeedBonus = 80f;
    [Tooltip("Destination distance from the target, as a fraction of shootRange (the 'edge' of its range).")]
    [SerializeField] private Vector2 repositionEdgeFraction = new Vector2(0.8f, 0.95f);
    [Tooltip("Random direction = 'away from target' rotated by +/- this many degrees. 180 = anywhere around the target.")]
    [SerializeField, Range(0f, 180f)] private float repositionHalfAngle = 120f;
    [SerializeField] private float arriveDistance = 0.5f;

    [Header("Soldier - Accuracy (set all to 0 for perfect aim)")]
    [Tooltip("Spread (degrees, +/-) at the start of aiming.")]
    [SerializeField] private float maxSpread = 5f;
    [Tooltip("Spread (degrees) once fully focused.")]
    [SerializeField] private float minSpread = 0.3f;
    [Tooltip("Seconds of aiming needed to be fully focused.")]
    [SerializeField] private float spreadFocusTime = 4f;
    [Tooltip("Extra degrees per unit/second of target speed.")]
    [SerializeField] private float spreadPerTargetSpeed = 0.35f;
    [Tooltip("Extra degrees per unit of distance.")]
    [SerializeField] private float spreadPerDistance = 0.1f;
    [SerializeField] private float spreadHardCap = 20f;
    [Tooltip("Being hit while aiming adds this much spread (stacks up to x3) ...")]
    [SerializeField] private float flinchExtraSpread = 2f;
    [Tooltip("... and delays the shot by this many seconds.")]
    [SerializeField] private float flinchAimDelay = 0.4f;

    [Header("Soldier - Targets")]
    [Tooltip("Can this unit target flying units?")]
    [SerializeField] private bool targetFlying = true;
    [Tooltip("Layers of flying units. They are not in the normal enemy mask.")]
    [SerializeField] private LayerMask flyingMask;
    [Tooltip("Optional: walls/obstacles. If set, it needs a clear line to aim and shots stop at walls. Empty = ignore walls.")]
    [SerializeField] private LayerMask sightBlockMask;

    [Header("Soldier - Visuals")]
    [Tooltip("Optional. Leave empty to use the built-in Sprites/Default shader.")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private Color aimColorStart = new Color(1f, 0.2f, 0.2f, 0.15f);
    [SerializeField] private Color aimColorEnd = new Color(1f, 0.1f, 0.1f, 0.9f);
    [SerializeField] private Color shotColor = new Color(1f, 0.9f, 0.4f, 1f);
    [SerializeField] private float aimLineWidth = 0.04f;
    [Tooltip("The aim line widens toward the target to show current spread. Max visual width.")]
    [SerializeField] private float aimConeMaxWidth = 3f;
    [SerializeField] private float shotLineWidth = 0.12f;
    [SerializeField] private float shotLineDuration = 0.12f;
    [Tooltip("How far a missed shot line continues past the target.")]
    [SerializeField] private float missOvershoot = 3f;
    #endregion

    #region Public API
    public SoldierState CurrentState => state;
    public bool IsAiming => state == SoldierState.Aiming;
    public bool IsRepositioning => state == SoldierState.Reposition;
    public Transform LockedTarget => lockedTarget;
    /// <summary>0..1 while aiming.</summary>
    public float AimProgress => state == SoldierState.Aiming ? Mathf.InverseLerp(aimStart, aimEnd, Time.time) : 0f;

    public event Action<Transform> OnAimStart;
    public event Action<AttackResult> OnShoot;
    public event Action OnRepositionStart;

    /// <summary>Drops the current aim (stun, knockback, etc).</summary>
    public void InterruptAim()
    {
        if (state == SoldierState.Aiming) CancelAim();
    }

    /// <summary>Immediately back off from the target (no invincibility).</summary>
    public void ForceRetreat()
    {
        if (attribute == null || state == SoldierState.Reposition) return;
        if (state == SoldierState.Aiming) CancelAim();
        BeginReposition(Time.time, RepositionKind.Retreat);
    }
    #endregion

    #region Runtime
    private static readonly int SoldierWalkHash = Animator.StringToHash("IsWalking");

    private SoldierState state = SoldierState.Approach;
    private Transform lockedTarget;
    private Collider2D lockedCollider;
    private float aimStart, aimEnd, repositionEnd, shotLineHideAt, nextRetreatTime;
    private bool npcWarned;
    private float appliedRepositionBonus;
    private Vector2 repositionPoint;
    private bool movedThisTick;

    private float flinchSpread;
    private float targetSpeed;
    private Vector2 lastAimPoint;
    private float lastAimTime;

    private bool hasLineOfSight = true;
    private float nextLosCheck;

    private readonly Collider2D[] flyBuffer = new Collider2D[16];

    private LineRenderer aimLine;
    private LineRenderer shotLine;
    #endregion

    #region Lifecycle
    protected override void Awake()
    {
        base.Awake();
        aimLine = BuildLine("AimLine", aimLineWidth, 50);
        shotLine = BuildLine("ShotLine", shotLineWidth, 51);
    }

    public override bool SpawnObject(ZomPackage zp)
    {
        if (!base.SpawnObject(zp)) return false;
        ClearSoldierState();
        return true;
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);

        if (shotLine != null && shotLine.enabled && Time.time >= shotLineHideAt)
            shotLine.enabled = false;
    }

    protected override void RemoveBonus()
    {
        ClearSoldierState(); // must run while attribute is still valid
        base.RemoveBonus();
    }

    public override void Reboot()
    {
        ClearSoldierState();
        base.Reboot();
    }

    private void ClearSoldierState()
    {
        if (attribute != null)
        {
            if (appliedRepositionBonus != 0f)
                attribute.SPEED_Ampl.FlatBonus -= appliedRepositionBonus;
            attribute.IsInvincible = false;
            attribute.IsUntargetable = false;
        }
        appliedRepositionBonus = 0f;

        state = SoldierState.Approach;
        lockedTarget = null;
        lockedCollider = null;
        npcWarned = false;
        flinchSpread = 0f;
        targetSpeed = 0f;
        nextRetreatTime = 0f;
        hasLineOfSight = true;
        nextLosCheck = 0f;
        movedThisTick = false;

        if (aimLine != null) aimLine.enabled = false;
        if (shotLine != null) shotLine.enabled = false;
    }
    #endregion

    #region Overrides
    /// <summary>The target is locked while aiming, so no re-targeting then.</summary>
    protected override void FindPrey()
    {
        if (state == SoldierState.Aiming) return;

        // The base only drops inactive/dead-NPC targets; also drop dead / untargetable ones (e.g. flying units).
        if (Target != null && !IsValidPrey(Target)) Target = null;

        base.FindPrey();
    }

    /// <summary>Base search (NPCs, ground friendlies, player) + flying units. Nearest wins.</summary>
    protected override Transform SearchForPrey(out NpcBrain npc)
    {
        Transform best = base.SearchForPrey(out npc);
        if (!targetFlying || flyingMask.value == 0) return best;

        Vector2 pos = _t.position;
        float bestSqr = best != null ? ((Vector2)best.position - pos).sqrMagnitude : float.MaxValue;

        int count = Physics2D.OverlapCircleNonAlloc(pos, preySearchRadius, flyBuffer, flyingMask);
        int ownLayer = gameObject.layer;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = flyBuffer[i];
            if (col == null || col.gameObject.layer == ownLayer) continue;
            if (!col.TryGetComponent(out HurtBox hurt)) continue;

            UnitAttribute ua = hurt.Access()?.attribute;
            if (ua == null || ua.IsDead || ua.IsUntargetable) continue;
            if (ua.tag != UnitTag.Friendly) continue;

            float sqr = ((Vector2)col.transform.position - pos).sqrMagnitude;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            best = col.transform;
            col.TryGetComponent(out NpcBrain flyNpc); // null if not an NPC
            npc = flyNpc;
        }

        return best;
    }

    protected override void Move()
    {
        movedThisTick = false;

        switch (state)
        {
            case SoldierState.Aiming:
                return; // cannot move while aiming

            case SoldierState.Reposition:
                {
                    Vector2 to = repositionPoint - (Vector2)_t.position;
                    if (to.sqrMagnitude <= arriveDistance * arriveDistance) return;
                    MoveToward(to.normalized);
                    return;
                }

            default:
                if (Target == null || Direction == Vector2.zero) return;
                if (DistanceToTarget <= shootRange && hasLineOfSight) return; // in position: stand and aim
                base.Move();
                movedThisTick = CanMove;
                return;
        }
    }

    /// <summary>Drives the state machine (called every tick by the base Tick).</summary>
    protected override void TryAttack()
    {
        if (attribute == null || package == null) return;
        float now = Time.time;

        UpdateLineOfSight(now);

        switch (state)
        {
            case SoldierState.Approach:
                if (Target == null || !CanAttack || Direction == Vector2.zero) return;

                // Too close for a ranged unit: back off first.
                if (minRangeFraction > 0f && now >= nextRetreatTime
                    && DistanceToTarget < shootRange * minRangeFraction)
                {
                    BeginReposition(now, RepositionKind.Retreat);
                    return;
                }

                if (DistanceToTarget <= shootRange && hasLineOfSight)
                    BeginAim(now);
                break;

            case SoldierState.Aiming:
                UpdateAim(now);
                break;

            case SoldierState.Reposition:
                {
                    bool arrived = ((Vector2)_t.position - repositionPoint).sqrMagnitude <= arriveDistance * arriveDistance;
                    if (now >= repositionEnd || arrived) EndReposition();
                    break;
                }
        }
    }

    protected override void OnHit(float damage)
    {
        base.OnHit(damage);

        // Getting hit while aiming ruins the aim a bit.
        if (state != SoldierState.Aiming) return;
        flinchSpread = Mathf.Min(flinchSpread + flinchExtraSpread, flinchExtraSpread * 3f);
        aimEnd += flinchAimDelay;
    }

    protected override void UpdateAnimator()
    {
        base.UpdateAnimator();
        if (anim == null) return;

        // Only "walking" when we really moved this tick (not while standing in range / aiming).
        anim.SetBool(SoldierWalkHash, movedThisTick);
    }
    #endregion

    #region Movement helper
    private void MoveToward(Vector2 dir)
    {
        if (!CanMove || dir == Vector2.zero) return;

        float now = Time.time;
        if (now >= separationInterval)
        {
            cachedSeparation = UnitMotion.GetSeparationForce(
                _t.position,
                hb.GetNearbyZoms(this, separationRadius),
                separationRadius,
                separationStrength);
            separationInterval = now + 0.2f;
        }

        Vector2 finalDir = (dir + cachedSeparation).normalized;
        UnitMotion.MoveThisObject(attribute, gameObject, finalDir);
        movedThisTick = true;
    }
    #endregion

    #region Aim / Shoot
    private void BeginAim(float now)
    {
        lockedTarget = Target;
        lockedCollider = FindAimCollider(lockedTarget);
        aimStart = now;
        aimEnd = now + RNG.GetFloat(aimDuration.x, aimDuration.y);
        npcWarned = false;
        flinchSpread = 0f;
        targetSpeed = 0f;
        lastAimPoint = AimPoint(lockedTarget, lockedCollider);
        lastAimTime = now;
        state = SoldierState.Aiming;

        aimLine.enabled = true;
        UpdateAimLine(0f, 0f);
        OnAimStart?.Invoke(lockedTarget);
    }

    private void UpdateAim(float now)
    {
        if (!IsValidPrey(lockedTarget))
        {
            CancelAim();
            return;
        }

        Vector2 from = _t.position;
        Vector2 aim = AimPoint(lockedTarget, lockedCollider);
        Vector2 delta = aim - from;
        float dist = delta.magnitude;

        if (dist > shootRange * aimBreakMultiplier || !hasLineOfSight)
        {
            CancelAim();
            return;
        }

        if (dist > 0.01f)
        {
            Direction = delta / dist;
            UnitMotion.RotateThisObject(gameObject, Direction);
        }

        // Estimate how fast the target is moving (harder to hit).
        float dt = now - lastAimTime;
        if (dt > 0.0001f)
        {
            float inst = (aim - lastAimPoint).magnitude / dt;
            targetSpeed = Mathf.Lerp(targetSpeed, inst, 1f - Mathf.Exp(-8f * dt));
            lastAimPoint = aim;
            lastAimTime = now;
        }

        float progress = Mathf.InverseLerp(aimStart, aimEnd, now);
        UpdateAimLine(progress, CurrentSpread(dist, now - aimStart));

        // Warn NPC shortly before the shot so it can try to dodge.
        if (!npcWarned && now >= aimEnd - npcWarnTime)
        {
            npcWarned = true;
            if (lockedTarget.TryGetComponent(out NpcBrain npc)) npc.NotifyIncomingAttack(_t.position);
        }

        if (now >= aimEnd) Fire(now);
    }

    private void Fire(float now)
    {
        Vector2 from = _t.position;
        Vector2 aim = AimPoint(lockedTarget, lockedCollider); // collider center, not the pivot
        Vector2 toAim = aim - from;
        float dist = toAim.magnitude;
        Vector2 dir = dist > 0.01f ? toAim / dist : Direction;

        // Inaccuracy: centre-biased random angle inside the current spread.
        float spread = CurrentSpread(dist, now - aimStart);
        float angle = (RNG.GetFloat(-1f, 1f) + RNG.GetFloat(-1f, 1f)) * 0.5f * spread;
        dir = RotateVec(dir, angle);

        Direction = dir;
        UnitMotion.RotateThisObject(gameObject, dir);

        // The shot must reach the target even if it drifted a bit past shootRange while aiming.
        float len = Mathf.Max(shootRange, dist + 1f);

        // Walls stop the bullet.
        bool wallHit = false;
        Vector2 wallPoint = Vector2.zero;
        if (sightBlockMask.value != 0)
        {
            RaycastHit2D wall = Physics2D.Raycast(from, dir, len, sightBlockMask);
            if (wall.collider != null)
            {
                wallHit = true;
                wallPoint = wall.point;
                len = wall.distance;
            }
        }

        PlayAttackSound();
        SpawnMuzzleFlash(MuzzlePos, dir);

        AttackResult rs = default;
        if (len > 0.05f)
            rs = Attack.Shoot(gameObject, attribute, len, dir, MaskFor(lockedTarget));

        HandleHitEffect(rs);
        CountTargetHit(rs);

        // Shot line: where the bullet really went (hit point, wall, or past the target on a miss).
        Vector2 end;
        if (rs.DidHit) end = rs.HitPoint;
        else if (wallHit) end = wallPoint;
        else end = from + dir * Mathf.Min(len, dist + missOvershoot);

        DrawShotLine(shotLineFromMuzzle ? MuzzlePos : from, end);
        shotLineHideAt = now + shotLineDuration;

        aimLine.enabled = false;
        flinchSpread = 0f;
        OnShoot?.Invoke(rs);

        BeginReposition(now, RepositionKind.AfterShot);
    }

    private void CancelAim()
    {
        aimLine.enabled = false;
        lockedTarget = null;
        lockedCollider = null;
        flinchSpread = 0f;
        state = SoldierState.Approach;
        rotateInterval = 0f;   // refresh direction/distance right away
        findPreyInterval = 0f; // allow a new target search immediately
    }

    private float CurrentSpread(float dist, float aimElapsed)
    {
        float focus = spreadFocusTime > 0f ? Mathf.Clamp01(aimElapsed / spreadFocusTime) : 1f;
        float s = Mathf.Lerp(maxSpread, minSpread, focus);
        s += targetSpeed * spreadPerTargetSpeed;
        s += dist * spreadPerDistance;
        s += flinchSpread;
        return Mathf.Min(s, spreadHardCap);
    }

    /// <summary>Flying units use their own mask; everything else uses the normal enemy mask.</summary>
    private LayerMask MaskFor(Transform t)
    {
        if (t != null && flyingMask.value != 0 && (flyingMask.value & (1 << t.gameObject.layer)) != 0)
            return flyingMask;
        return hb.enemyMask;
    }
    #endregion

    #region Reposition
    private void BeginReposition(float now, RepositionKind kind)
    {
        state = SoldierState.Reposition;
        repositionEnd = now + RNG.GetFloat(repositionDuration.x, repositionDuration.y);
        repositionPoint = PickRepositionPoint();
        lockedTarget = null;
        lockedCollider = null;

        bool afterShot = kind == RepositionKind.AfterShot;
        if (afterShot)
        {
            attribute.IsInvincible = true;
            attribute.IsUntargetable = true;
        }
        else
        {
            nextRetreatTime = now + retreatCooldown;
        }

        if (appliedRepositionBonus == 0f)
        {
            appliedRepositionBonus = afterShot ? repositionSpeedBonus : retreatSpeedBonus;
            attribute.SPEED_Ampl.FlatBonus += appliedRepositionBonus;
        }

        // Pick a fresh target right away; the old one may be far from us now.
        findPreyInterval = 0f;
        rotateInterval = 0f;

        OnRepositionStart?.Invoke();
    }

    /// <summary>
    /// Random point on a ring around the target (the edge of shooting range),
    /// in a direction that is "away from the target" rotated randomly by up to +/- repositionHalfAngle.
    /// </summary>
    private Vector2 PickRepositionPoint()
    {
        Vector2 self = _t.position;
        Vector2 center = self;
        float a = RNG.GetFloat(0f, Mathf.PI * 2f);
        Vector2 away = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); // fallback: any direction

        if (Target != null)
        {
            center = Target.position;
            Vector2 d = self - center;
            if (d.sqrMagnitude > 0.0001f) away = d.normalized;
        }

        float rot = RNG.GetFloat(-repositionHalfAngle, repositionHalfAngle);
        Vector2 dir = RotateVec(away, rot);
        float radius = shootRange * RNG.GetFloat(repositionEdgeFraction.x, repositionEdgeFraction.y);
        return center + dir * radius;
    }

    private void EndReposition()
    {
        if (appliedRepositionBonus != 0f)
        {
            attribute.SPEED_Ampl.FlatBonus -= appliedRepositionBonus;
            appliedRepositionBonus = 0f;
        }
        attribute.IsInvincible = false;
        attribute.IsUntargetable = false;

        state = SoldierState.Approach; // next tick: aim again if in range, otherwise keep approaching
    }
    #endregion

    #region Line of sight / helpers
    private void UpdateLineOfSight(float now)
    {
        if (sightBlockMask.value == 0 || Target == null)
        {
            hasLineOfSight = true;
            return;
        }
        if (now < nextLosCheck) return;
        nextLosCheck = now + 0.15f;

        Vector2 from = _t.position;
        Vector2 to = AimPoint(Target, null) - from;
        float dist = to.magnitude;
        hasLineOfSight = dist < 0.01f || Physics2D.Raycast(from, to / dist, dist, sightBlockMask).collider == null;
    }

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

    /// <summary>Center of the target's collider (a pivot can sit at the feet or outside the collider).</summary>
    private static Vector2 AimPoint(Transform t, Collider2D cached)
    {
        if (t == null) return Vector2.zero;
        Collider2D c = cached != null ? cached : FindAimCollider(t);
        if (c != null && c.enabled) return c.bounds.center;
        return t.position;
    }

    private static Vector2 RotateVec(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
    #endregion

    #region Lines
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

    /// <summary>The line narrows into a thin line as the aim focuses; its width at the target shows the current spread.</summary>
    private void UpdateAimLine(float t, float spreadDegrees)
    {
        if (aimLine == null || lockedTarget == null) return;

        Vector3 a = _t.position;
        Vector3 b = AimPoint(lockedTarget, lockedCollider);
        a.z = b.z = 0f;
        aimLine.SetPosition(0, a);
        aimLine.SetPosition(1, b);

        float dist = Vector2.Distance(a, b);
        float coneWidth = 2f * dist * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
        aimLine.startWidth = aimLineWidth;
        aimLine.endWidth = Mathf.Clamp(coneWidth, aimLineWidth, Mathf.Max(aimLineWidth, aimConeMaxWidth));

        Color c = Color.Lerp(aimColorStart, aimColorEnd, t);
        aimLine.startColor = aimLine.endColor = c;
    }

    private void DrawShotLine(Vector2 from, Vector2 to)
    {
        shotLine.SetPosition(0, new Vector3(from.x, from.y, 0f));
        shotLine.SetPosition(1, new Vector3(to.x, to.y, 0f));
        shotLine.startColor = shotLine.endColor = shotColor;
        shotLine.enabled = true;
    }
    private Vector2 MuzzlePos => muzzle != null ? (Vector2)muzzle.position : (Vector2)_t.position;

    private void SpawnMuzzleFlash(Vector2 pos, Vector2 dir)
    {
        if (muzzleFlash == null || PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(muzzleFlash);
        if (o == null) return;

        o.transform.position = pos;
        o.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }
    #endregion

    protected override void PlayAttackSound()
    {
        if (AudioManager.instance == null || package == null) return;
        AudioManager.instance.PlayDirectAudio(package.Media.Audio.GetAttackSound());
    }

    #region Debug
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || attribute == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, shootRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, shootRange * minRangeFraction);

        if (state == SoldierState.Reposition)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, repositionPoint);
            Gizmos.DrawWireSphere(repositionPoint, 0.3f);
        }
    }
    #endregion
}