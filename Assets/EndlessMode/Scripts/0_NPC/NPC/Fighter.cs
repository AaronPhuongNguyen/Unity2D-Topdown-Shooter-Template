using System.Collections.Generic;
using Server;
using UnityEngine;

/// <summary>Marker interface: zombies never chase NPCs that implement it.</summary>
public interface IZombieIgnored { }

/// <summary>
/// NPC bomber. It never follows, hunts, flees or shoots anyone (NpcBrain's Tick is
/// fully replaced). Its only job is the "Drop The Bomb" run:
///
///   1. Waiting   - parked out of sight (hidden, no collider). First cast right after
///                  spawn, then every castCooldown seconds. The cooldown starts only
///                  once ALL bombs of a run have been dropped.
///   2. Telegraph - a 100-unit line indicator shrinks in over telegraphTime seconds,
///                  with a circle marker at every bomb position.
///   3. Pass      - the Fighter swoops in from outside the map along the line and
///                  drops one Bomb at each marked position as it flies over it.
///   4. Exit      - keeps flying out of the map, parks, and returns after the cooldown.
///
/// Target priority for the line:  dense zombie area  >  player  >  another NPC  >  random.
///
/// Dynamic extras:
///   - Line angle is chosen from several candidates: the one that crosses the most zombies.
///   - Bombs are only placed on the part of the line that is inside the walkable area.
///   - Each bomb position is nudged toward nearby zombies (snap) and gets a little jitter.
///   - Two patterns, picked at random per run: Carpet (straight) and Zig-Zag (alternating sides).
///   - Bomb count grows with the wave number.
///   - Bomb damage = baseBombDamage + atkMultiplier x ATK (1000 + 2000% ATK by default).
///   - Warning sound, fly-by sound and a small camera shake when the run starts.
///   - The Fighter is invincible, has no collider and is ignored by zombies.
///
/// Prefab setup: an NPC prefab (NpcBrain replaced by Fighter) with a sprite pointing RIGHT (+X).
/// Assign Bomb Prefab (has a Bomb component), and optionally the Line Indicator / Marker prefabs
/// (SpriteRenderer with a simple square / circle sprite).
/// </summary>
public class Fighter : NpcBrain, IZombieIgnored
{
    private enum Phase { Waiting, Telegraph, Pass, Exit }

    private class Slot
    {
        public Vector2 pos;
        public float dist;       // distance along the flight line where the bomb is released
        public GameObject marker;
    }

    #region Inspector
    [Header("Drop The Bomb")]
    [Tooltip("Seconds between runs, counted from the moment the LAST bomb is dropped.")]
    [SerializeField] private float castCooldown = 30f;
    [Tooltip("Seconds after spawn before the first run.")]
    [SerializeField] private float firstCastDelay = 1f;
    [SerializeField] private float lineLength = 100f;
    [Tooltip("Seconds the line indicator takes to shrink in before the Fighter arrives.")]
    [SerializeField] private float telegraphTime = 2f;
    [Tooltip("Flight speed during the run (units/second).")]
    [SerializeField] private float passSpeed = 70f;

    [Header("Bombs")]
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private int baseBombCount = 14;
    [SerializeField] private int maxBombCount = 30;
    [Tooltip("+1 bomb every this many waves.")]
    [SerializeField] private int wavesPerExtraBomb = 5;
    [SerializeField] private float bombRadius = 5f;
    [Tooltip("Damage multiplier at the very edge of the blast (1 = no falloff).")]
    [SerializeField, Range(0f, 1f)] private float bombMinDamageMul = 0.6f;
    [Tooltip("Flat base damage of every bomb.")]
    [SerializeField] private float baseBombDamage = 1000f;
    [Tooltip("ATK multiplier of every bomb (20 = 2000% ATK).")]
    [SerializeField] private float atkMultiplier = 20f;

    [Header("Targeting")]
    [Tooltip("Min zombies in a cluster to count as a dense area.")]
    [SerializeField] private int denseMinCount = 6;
    [SerializeField] private float clusterRadius = 8f;
    [SerializeField] private int maxClusterCandidates = 64;
    [Tooltip("How many line angles are compared to find the one crossing most zombies.")]
    [SerializeField] private int angleSamples = 12;
    [Tooltip("A bomb position is pulled toward the nearest zombie within this range.")]
    [SerializeField] private float snapRange = 6f;
    [SerializeField, Range(0f, 1f)] private float snapStrength = 0.6f;
    [Tooltip("Random sideways offset (+/-) of every bomb.")]
    [SerializeField] private float lateralJitter = 1.5f;
    [Tooltip("Chance a run uses the Zig-Zag pattern instead of a straight carpet.")]
    [SerializeField, Range(0f, 1f)] private float zigzagChance = 0.4f;
    [SerializeField] private float zigzagOffset = 3f;

    [Header("Indicators")]
    [Tooltip("Pooled prefab with a SpriteRenderer (plain square). Drawn as the long line.")]
    [SerializeField] private GameObject lineIndicatorPrefab;
    [Tooltip("Pooled prefab with a SpriteRenderer (plain circle). One per bomb position.")]
    [SerializeField] private GameObject markerPrefab;
    [SerializeField] private float lineStartWidth = 12f;
    [SerializeField] private float lineEndWidth = 0.5f;

    [Header("Flight")]
    [Tooltip("The Fighter enters/leaves this far outside the map edge.")]
    [SerializeField] private float mapMargin = 10f;
    [SerializeField] private float maxApproach = 300f;

    [Header("Feedback")]
    [SerializeField] private AudioClip warningSound;
    [SerializeField] private AudioClip flybySound;
    [SerializeField] private float flybyShake = 0.6f;
    #endregion

    #region Runtime
    private Phase phase = Phase.Waiting;
    private float cooldownTimer;
    private float telegraphTimer;

    // Plan of the current run
    private Vector2 lineStart, lineDir, lineCenter;
    private readonly List<Slot> slots = new List<Slot>();
    private int nextBomb;

    // Indicators
    private GameObject lineIndicator;
    private Vector2 lineNative = Vector2.one;
    private Vector2 markerNative = Vector2.one;

    // Zombie scan
    private Collider2D[] scanBuffer = new Collider2D[256];
    private readonly List<Vector2> zomPts = new List<Vector2>(256);

    // Visibility
    private Renderer[] renderers;
    private Collider2D[] colliders;
    private bool visible = true;
    #endregion

    #region Lifecycle hooks
    protected override void OnSpawned()
    {
        base.OnSpawned();

        attribute.IsInvincible = true;

        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider2D>(true);

        Park();
        phase = Phase.Waiting;
        cooldownTimer = firstCastDelay;
        nextBomb = 0;
        slots.Clear();
    }

    protected override void OnRemoved()
    {
        ClearIndicators();
        SetVisible(true);
        SetColliders(true);
        phase = Phase.Waiting;
        slots.Clear();

        base.OnRemoved();
    }
    #endregion

    #region Tick
    public override void Tick(float dt)
    {
        if (TickSystem.GameSpeed <= 0) return;
        if (!IsActive || attribute == null || attribute.IsDead) return;
        if (pm == null) return;

        switch (phase)
        {
            case Phase.Waiting:
                cooldownTimer -= dt;
                if (cooldownTimer <= 0f) BeginTelegraph();
                break;

            case Phase.Telegraph:
                TickTelegraph(dt);
                break;

            case Phase.Pass:
                cooldownTimer -= dt; // (not started yet; stays at its full value until the last bomb)
                TickFlight(dt, true);
                break;

            case Phase.Exit:
                cooldownTimer -= dt;
                TickFlight(dt, false);
                break;
        }
    }
    #endregion

    #region Planning
    private Rect WalkRect => dm != null
        ? new Rect(dm.WalkableMin, dm.WalkableMax - dm.WalkableMin)
        : new Rect(-50f, -50f, 100f, 100f);

    private Rect ExpandedMap
    {
        get
        {
            Rect r = dm != null ? dm.MapBounds : new Rect(-50f, -50f, 100f, 100f);
            r.xMin -= mapMargin;
            r.yMin -= mapMargin;
            r.xMax += mapMargin;
            r.yMax += mapMargin;
            return r;
        }
    }

    private float BombDamage => baseBombDamage + atkMultiplier * pm.attribute.ATK_Current;

    private void PlanRun()
    {
        CollectZombies();

        float half = lineLength * 0.5f;
        lineCenter = PickCenter();
        lineDir = PickDirection(lineCenter, half);
        lineStart = lineCenter - lineDir * half;

        BuildSlots(half);
    }

    private void CollectZombies()
    {
        zomPts.Clear();

        Rect w = WalkRect;
        Vector2 a = new Vector2(w.xMin, w.yMin);
        Vector2 b = new Vector2(w.xMax, w.yMax);

        int count = Physics2D.OverlapAreaNonAlloc(a, b, scanBuffer, pm.EnemyMask);
        while (count >= scanBuffer.Length)
        {
            scanBuffer = new Collider2D[scanBuffer.Length * 2];
            count = Physics2D.OverlapAreaNonAlloc(a, b, scanBuffer, pm.EnemyMask);
        }

        for (int i = 0; i < count; i++)
        {
            Collider2D col = scanBuffer[i];
            if (col == null) continue;
            if (!col.TryGetComponent(out HurtBox hurt)) continue;

            UnitAttribute ua = hurt.Access()?.attribute;
            if (ua == null || ua.IsDead || ua.tag != UnitTag.Aggressive) continue;

            zomPts.Add(col.transform.position);
        }
    }

    /// <summary>Dense zombie area > player > another NPC > random point.</summary>
    private Vector2 PickCenter()
    {
        if (zomPts.Count >= denseMinCount && TryFindDenseCenter(out Vector2 dense))
            return dense;

        if (pm.Controlling != null)
            return pm.Controlling.transform.position;

        Vector2 me = _t.position;
        float bestSqr = float.MaxValue;
        Vector2 other = Vector2.zero;
        bool found = false;
        for (int i = 0; i < Active.Count; i++)
        {
            NpcBrain n = Active[i];
            if (n == null || n == this || !n.IsActive || n is IZombieIgnored) continue;

            float sqr = ((Vector2)n.transform.position - me).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                other = n.transform.position;
                found = true;
            }
        }
        if (found) return other;

        Rect w = WalkRect;
        return new Vector2(RNG.GetFloat(w.xMin, w.xMax), RNG.GetFloat(w.yMin, w.yMax));
    }

    private bool TryFindDenseCenter(out Vector2 center)
    {
        center = Vector2.zero;

        float rSqr = clusterRadius * clusterRadius;
        int stride = Mathf.Max(1, zomPts.Count / Mathf.Max(1, maxClusterCandidates));

        int bestCount = 0;
        int bestIndex = -1;
        for (int i = 0; i < zomPts.Count; i += stride)
        {
            int c = 0;
            for (int j = 0; j < zomPts.Count; j++)
                if ((zomPts[j] - zomPts[i]).sqrMagnitude <= rSqr) c++;

            if (c > bestCount)
            {
                bestCount = c;
                bestIndex = i;
            }
        }

        if (bestIndex < 0 || bestCount < denseMinCount) return false;

        Vector2 sum = Vector2.zero;
        int n = 0;
        for (int j = 0; j < zomPts.Count; j++)
        {
            if ((zomPts[j] - zomPts[bestIndex]).sqrMagnitude > rSqr) continue;
            sum += zomPts[j];
            n++;
        }

        center = sum / Mathf.Max(1, n);
        return true;
    }

    /// <summary>Tries several angles through the center and keeps the one crossing the most zombies.</summary>
    private Vector2 PickDirection(Vector2 center, float half)
    {
        Vector2 best = Vector2.right;
        int bestCount = -1;

        int samples = Mathf.Max(1, angleSamples);
        float step = Mathf.PI / samples;
        float offset = RNG.GetFloat(0f, step);

        for (int k = 0; k < samples; k++)
        {
            float a = offset + k * step;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 perp = new Vector2(-d.y, d.x);

            int c = 0;
            for (int i = 0; i < zomPts.Count; i++)
            {
                Vector2 rel = zomPts[i] - center;
                if (Mathf.Abs(Vector2.Dot(rel, d)) > half) continue;
                if (Mathf.Abs(Vector2.Dot(rel, perp)) > bombRadius) continue;
                c++;
            }

            if (c > bestCount)
            {
                bestCount = c;
                best = d;
            }
        }

        // The line has no "front": fly it from either end.
        return RNG.GetFloat(0f, 1f) < 0.5f ? best : -best;
    }

    private void BuildSlots(float half)
    {
        ClearSlots();

        // Only the part of the line inside the (padded) walkable area gets bombs.
        Rect w = WalkRect;
        float pad = bombRadius * 0.5f;
        w.xMin -= pad;
        w.yMin -= pad;
        w.xMax += pad;
        w.yMax += pad;

        bool found = false;
        float tMin = -half, tMax = half;
        for (float t = -half; t <= half; t += 1f)
        {
            if (!w.Contains(lineCenter + lineDir * t)) continue;
            if (!found) { tMin = t; found = true; }
            tMax = t;
        }
        if (!found)
        {
            tMin = -half;
            tMax = half;
        }

        int wave = dm != null ? dm.CurrentWave : 0;
        int count = baseBombCount + (wavesPerExtraBomb > 0 ? wave / wavesPerExtraBomb : 0);
        count = Mathf.Clamp(count, 1, Mathf.Max(1, maxBombCount));

        bool zigzag = RNG.GetFloat(0f, 1f) < zigzagChance;
        Vector2 perp = new Vector2(-lineDir.y, lineDir.x);

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? (tMin + tMax) * 0.5f : Mathf.Lerp(tMin, tMax, i / (float)(count - 1));
            float lateral = RNG.GetFloat(-lateralJitter, lateralJitter);
            if (zigzag) lateral += (i % 2 == 0 ? 1f : -1f) * zigzagOffset;

            Vector2 p = lineCenter + lineDir * t + perp * lateral;
            p = SnapToZombie(p);

            slots.Add(new Slot { pos = p, dist = Vector2.Dot(p - lineStart, lineDir) });
        }

        // Snapping can shuffle the order: bombs are dropped in the order the plane reaches them.
        slots.Sort((a, b) => a.dist.CompareTo(b.dist));
        nextBomb = 0;
    }

    private Vector2 SnapToZombie(Vector2 p)
    {
        float bestSqr = snapRange * snapRange;
        bool found = false;
        Vector2 target = p;

        for (int i = 0; i < zomPts.Count; i++)
        {
            float sqr = (zomPts[i] - p).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                target = zomPts[i];
                found = true;
            }
        }

        return found ? Vector2.Lerp(p, target, snapStrength) : p;
    }
    #endregion

    #region Telegraph
    private void BeginTelegraph()
    {
        PlanRun();

        phase = Phase.Telegraph;
        telegraphTimer = 0f;

        SpawnLineIndicator();
        SpawnMarkers();
        UpdateLineIndicator(0f);

        PlayAt(warningSound, lineCenter);
    }

    private void TickTelegraph(float dt)
    {
        telegraphTimer += dt;
        float p = telegraphTime > 0.001f ? Mathf.Clamp01(telegraphTimer / telegraphTime) : 1f;
        UpdateLineIndicator(p);

        if (p >= 1f) BeginPass();
    }

    private static Vector2 NativeSize(GameObject o)
    {
        if (o != null && o.TryGetComponent(out SpriteRenderer sr) && sr.sprite != null)
        {
            Vector2 s = sr.sprite.bounds.size;
            if (s.x > 0.0001f && s.y > 0.0001f) return s;
        }
        return Vector2.one;
    }

    private void SpawnLineIndicator()
    {
        if (lineIndicatorPrefab == null || PoolingSystem.instance == null) return;

        lineIndicator = PoolingSystem.instance.GetFromPool(lineIndicatorPrefab);
        if (lineIndicator == null) return;

        lineNative = NativeSize(lineIndicator);

        float ang = Mathf.Atan2(lineDir.y, lineDir.x) * Mathf.Rad2Deg;
        Transform t = lineIndicator.transform;
        t.position = new Vector3(lineCenter.x, lineCenter.y, t.position.z);
        t.rotation = Quaternion.Euler(0f, 0f, ang);
    }

    /// <summary>The line gets thinner as p goes 0 -> 1.</summary>
    private void UpdateLineIndicator(float p)
    {
        if (lineIndicator == null) return;

        float width = Mathf.Lerp(lineStartWidth, lineEndWidth, p);
        lineIndicator.transform.localScale = new Vector3(
            lineLength / lineNative.x,
            width / lineNative.y,
            1f);
    }

    private void SpawnMarkers()
    {
        if (markerPrefab == null || PoolingSystem.instance == null) return;

        for (int i = 0; i < slots.Count; i++)
        {
            GameObject m = PoolingSystem.instance.GetFromPool(markerPrefab);
            if (m == null) continue;

            if (i == 0) markerNative = NativeSize(m);

            Transform t = m.transform;
            t.position = new Vector3(slots[i].pos.x, slots[i].pos.y, t.position.z);
            t.rotation = Quaternion.identity;

            float d = bombRadius * 2f;
            t.localScale = new Vector3(d / markerNative.x, d / markerNative.y, 1f);

            slots[i].marker = m;
        }
    }

    private void ReleaseObject(ref GameObject o)
    {
        if (o == null) return;
        if (PoolingSystem.instance != null) PoolingSystem.instance.RemoveToPool(o);
        o = null;
    }

    private void ClearSlots()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            GameObject m = slots[i].marker;
            ReleaseObject(ref m);
            slots[i].marker = null;
        }
        slots.Clear();
    }

    private void ClearIndicators()
    {
        ReleaseObject(ref lineIndicator);
        ClearSlots();
    }
    #endregion

    #region Flight
    private void BeginPass()
    {
        ReleaseObject(ref lineIndicator);

        // Start outside the map so the plane never pops in on screen.
        Vector2 enter = EdgePoint(lineStart, -lineDir);
        _t.position = new Vector3(enter.x, enter.y, _t.position.z);
        UnitMotion.RotateThisObject(gameObject, lineDir);

        SetVisible(true);
        phase = Phase.Pass;
        nextBomb = 0;

        PlayAt(flybySound, lineCenter);
        if (flybyShake > 0f) CamManager.instance?.Shake(flybyShake);
    }

    private void TickFlight(float dt, bool dropping)
    {
        Vector2 pos = (Vector2)_t.position + lineDir * passSpeed * dt;
        _t.position = new Vector3(pos.x, pos.y, _t.position.z);

        if (dropping)
        {
            float progress = Vector2.Dot(pos - lineStart, lineDir);
            while (nextBomb < slots.Count && progress >= slots[nextBomb].dist)
            {
                DropBomb(slots[nextBomb]);
                nextBomb++;
            }

            if (nextBomb >= slots.Count)
            {
                // Every bomb is gone: NOW the cooldown starts, and the plane flies away.
                cooldownTimer = castCooldown;
                phase = Phase.Exit;
            }
            return;
        }

        if (!ExpandedMap.Contains(pos))
        {
            Park();
            phase = Phase.Waiting;
        }
    }

    private void DropBomb(Slot slot)
    {
        GameObject m = slot.marker;
        ReleaseObject(ref m);
        slot.marker = null;

        Bomb.Launch(
            bombPrefab,
            _t.position,
            slot.pos,
            attribute,
            BombDamage,
            bombRadius,
            bombMinDamageMul,
            pm.EnemyMask
        );
    }

    /// <summary>First point outside the (expanded) map when walking from "from" along "dir".</summary>
    private Vector2 EdgePoint(Vector2 from, Vector2 dir)
    {
        Rect r = ExpandedMap;
        Vector2 p = from;
        for (float t = 0f; t < maxApproach && r.Contains(p); t += 2f)
            p = from + dir * t;
        return p + dir * 2f;
    }

    private void Park()
    {
        ClearIndicators();
        SetVisible(false);

        Vector2 c = dm != null ? dm.mapCenter : Vector2.zero;
        Vector2 half = dm != null ? dm.mapSize * 0.5f : new Vector2(50f, 50f);
        Vector2 p = c - new Vector2(half.x + mapMargin * 3f, 0f);
        _t.position = new Vector3(p.x, p.y, _t.position.z);
    }
    #endregion

    #region Helpers
    private void SetVisible(bool value)
    {
        visible = value;

        if (renderers != null)
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = value;

        // Colliders stay off the whole time: nothing can hit or target the plane.
        SetColliders(false);
    }

    private void SetColliders(bool value)
    {
        if (colliders == null) return;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = value;
    }

    private static void PlayAt(AudioClip clip, Vector2 pos)
    {
        if (clip == null || AudioManager.instance == null) return;
        AudioManager.instance.PlayAudio(clip, pos);
    }
    #endregion
}