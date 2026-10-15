using Server;
using System.Collections.Generic;
using UnityEngine;

public struct AttackResult
{
    public bool DidHit;
    public bool IsCrit;
    public GameObject Target;
    public Vector2 HitPoint;
    public UnitAttribute HitUnit;
    public float Damage;
}

public static class Attack
{
    private static int DefaultMask => LayerMask.GetMask("Default");

    #region Shoot (single target)
    // --- With explicit base damage (crit is still rolled on top) ---

    public static AttackResult Shoot(GameObject o, float damage, UnitAttribute ua, float range, Vector2 dir)
        => Shoot(o, damage, ua, range, dir, DefaultMask);

    public static AttackResult Shoot(GameObject o, float damage, UnitAttribute ua, float range, Vector2 dir, LayerMask enemymask)
    {
        if (o == null || ua == null || range == 0f) return default;

        RaycastHit2D hit = Physics2D.Raycast(o.transform.position, dir, range, enemymask);
        if (hit.collider == null) return default;

        if (!hit.collider.TryGetComponent<HurtBox>(out HurtBox target)) return default;

        UnitAttribute hitUnit = target.Access()?.attribute;
        if (hitUnit == null) return default;

        return Resolve(damage, ua, hitUnit, hit.collider.gameObject, hit.point);
    }

    // --- Convenience: base damage = attacker's ATK_Current ---

    public static AttackResult Shoot(GameObject o, UnitAttribute ua, float range, Vector2 dir)
        => Shoot(o, ua != null ? ua.ATK_Current : 0f, ua, range, dir, DefaultMask);

    public static AttackResult Shoot(GameObject o, UnitAttribute ua, float range, Vector2 dir, LayerMask enemymask)
        => Shoot(o, ua != null ? ua.ATK_Current : 0f, ua, range, dir, enemymask);
    #endregion

    #region ShootPierce (multi target)
    // --- With explicit base damage ---

    public static AttackResult[] ShootPierce(
        GameObject o,
        float damage,
        UnitAttribute ua,
        float range,
        Vector2 dir,
        int maxTargets = 0,
        float damageFalloff = 1f)
        => ShootPierce(o, damage, ua, range, dir, (LayerMask)DefaultMask, maxTargets, damageFalloff);

    public static AttackResult[] ShootPierce(
        GameObject o,
        float damage,
        UnitAttribute ua,
        float range,
        Vector2 dir,
        LayerMask enemymask,
        int maxTargets = 0,
        float damageFalloff = 1f)
    {
        if (o == null || ua == null || range == 0f) return System.Array.Empty<AttackResult>();
        if (dir.sqrMagnitude < 0.0001f) return System.Array.Empty<AttackResult>();
        dir = dir.normalized;

        Vector2 origin = o.transform.position;
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, range, enemymask);
        if (hits == null || hits.Length == 0) return System.Array.Empty<AttackResult>();

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        List<AttackResult> results = new List<AttackResult>();
        HashSet<UnitAttribute> alreadyHit = new HashSet<UnitAttribute>();
        float currentDamageMul = 1f;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.collider == null) continue;
            if (!hit.collider.TryGetComponent<HurtBox>(out HurtBox target)) continue;

            UnitAttribute hitUnit = target.Access()?.attribute;
            if (hitUnit == null) continue;
            if (!alreadyHit.Add(hitUnit)) continue;

            results.Add(Resolve(damage * currentDamageMul, ua, hitUnit, hit.collider.gameObject, hit.point));
            currentDamageMul *= damageFalloff;

            if (maxTargets > 0 && results.Count >= maxTargets) break;
        }

        return results.ToArray();
    }

    // --- Convenience: base damage = attacker's ATK_Current ---

    public static AttackResult[] ShootPierce(
        GameObject o,
        UnitAttribute ua,
        float range,
        Vector2 dir,
        LayerMask enemymask,
        int maxTargets = 0,
        float damageFalloff = 1f)
        => ShootPierce(o, ua != null ? ua.ATK_Current : 0f, ua, range, dir, enemymask, maxTargets, damageFalloff);

    public static AttackResult[] ShootPierce(
        GameObject o,
        UnitAttribute ua,
        float range,
        Vector2 dir,
        int maxTargets = 0,
        float damageFalloff = 1f)
        => ShootPierce(o, ua != null ? ua.ATK_Current : 0f, ua, range, dir, (LayerMask)DefaultMask, maxTargets, damageFalloff);
    #endregion

    #region Circle AoE (grenade, explosion)
    /// <summary>
    /// Damages every unit within <paramref name="radius"/> of <paramref name="center"/>.
    /// Distance is measured to the collider's surface, so big bosses are hit earlier.
    /// minDamageMul: damage multiplier at the very edge (1 = no falloff, 0.5 = 50% at the edge,
    /// linear from the center). maxTargets 0 = unlimited; if limited, the closest are hit first.
    /// The attacker itself is never hit; use the mask to choose who can be damaged.
    /// </summary>
    public static AttackResult[] ShootCircle(
        Vector2 center,
        float damage,
        UnitAttribute ua,
        float radius,
        LayerMask enemymask,
        int maxTargets = 0,
        float minDamageMul = 1f)
    {
        if (ua == null || radius <= 0f) return System.Array.Empty<AttackResult>();

        GatherInCircle(center, radius, enemymask, ua, Vector2.zero, 360f, false);
        return ResolveCandidates(damage, ua, radius, maxTargets, minDamageMul);
    }

    /// <summary>Circle AoE with base damage = attacker's ATK_Current.</summary>
    public static AttackResult[] ShootCircle(
        Vector2 center,
        UnitAttribute ua,
        float radius,
        LayerMask enemymask,
        int maxTargets = 0,
        float minDamageMul = 1f)
        => ShootCircle(center, ua != null ? ua.ATK_Current : 0f, ua, radius, enemymask, maxTargets, minDamageMul);

    /// <summary>
    /// Scan only, no damage: every living unit within the radius (closest first).
    /// Use it for detection, previews, or your own effects (stun, knockback...).
    /// </summary>
    public static List<UnitAttribute> ScanCircle(
        Vector2 center,
        float radius,
        LayerMask mask,
        UnitAttribute ignore = null)
    {
        var units = new List<UnitAttribute>();
        if (radius <= 0f) return units;

        GatherInCircle(center, radius, mask, ignore, Vector2.zero, 360f, false);
        for (int i = 0; i < _cands.Count; i++) units.Add(_cands[i].unit);
        return units;
    }
    #endregion

    #region Cone AoE (shotgun, flamethrower)
    /// <summary>
    /// Damages every unit inside a cone: origin at <paramref name="origin"/>, facing
    /// <paramref name="dir"/>, reaching <paramref name="range"/>, <paramref name="angle"/> degrees wide
    /// (90 = 45 degrees to each side). Falloff works like ShootCircle (center-to-range).
    /// </summary>
    public static AttackResult[] ShootCone(
        Vector2 origin,
        Vector2 dir,
        float damage,
        UnitAttribute ua,
        float range,
        float angle,
        LayerMask enemymask,
        int maxTargets = 0,
        float minDamageMul = 1f)
    {
        if (ua == null || range <= 0f || angle <= 0f) return System.Array.Empty<AttackResult>();
        if (dir.sqrMagnitude < 0.0001f) return System.Array.Empty<AttackResult>();

        GatherInCircle(origin, range, enemymask, ua, dir.normalized, angle, true);
        return ResolveCandidates(damage, ua, range, maxTargets, minDamageMul);
    }

    /// <summary>Cone AoE with base damage = attacker's ATK_Current.</summary>
    public static AttackResult[] ShootCone(
        Vector2 origin,
        Vector2 dir,
        UnitAttribute ua,
        float range,
        float angle,
        LayerMask enemymask,
        int maxTargets = 0,
        float minDamageMul = 1f)
        => ShootCone(origin, dir, ua != null ? ua.ATK_Current : 0f, ua, range, angle, enemymask, maxTargets, minDamageMul);
    #endregion

    #region Shared
    private struct Candidate
    {
        public UnitAttribute unit;
        public GameObject obj;
        public Vector2 point;
        public float dist;
    }

    // Reused buffers: AoE can fire many times per second (no GC per call except the result array).
    private static Collider2D[] _buffer = new Collider2D[64];
    private static readonly List<Candidate> _cands = new List<Candidate>(32);
    private static readonly HashSet<UnitAttribute> _seen = new HashSet<UnitAttribute>();

    // Fills _cands with the valid units in range (sorted closest first).
    // useCone = false -> full circle. Dead units, the attacker and duplicates are skipped.
    private static void GatherInCircle(Vector2 center, float radius, LayerMask mask,
        UnitAttribute ignore, Vector2 dir, float angle, bool useCone)
    {
        _cands.Clear();
        _seen.Clear();

        int count = Physics2D.OverlapCircleNonAlloc(center, radius, _buffer, mask);
        while (count >= _buffer.Length)
        {
            _buffer = new Collider2D[_buffer.Length * 2];
            count = Physics2D.OverlapCircleNonAlloc(center, radius, _buffer, mask);
        }

        float cosHalf = Mathf.Cos(angle * 0.5f * Mathf.Deg2Rad);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = _buffer[i];
            if (col == null) continue;
            if (!col.TryGetComponent(out HurtBox hurt)) continue;

            UnitAttribute unit = hurt.Access()?.attribute;
            if (unit == null || unit.IsDead || unit == ignore) continue;
            if (_seen.Contains(unit)) continue;

            Vector2 closest = col.ClosestPoint(center);
            Vector2 to = closest - center;
            float dist = to.magnitude;
            if (dist > radius) continue;

            if (useCone && dist > 0.0001f && Vector2.Dot(to / dist, dir) < cosHalf) continue;

            _seen.Add(unit);
            _cands.Add(new Candidate { unit = unit, obj = col.gameObject, point = closest, dist = dist });
        }

        _cands.Sort((a, b) => a.dist.CompareTo(b.dist));
    }

    private static AttackResult[] ResolveCandidates(float damage, UnitAttribute ua, float radius,
        int maxTargets, float minDamageMul)
    {
        int n = _cands.Count;
        if (maxTargets > 0 && n > maxTargets) n = maxTargets;
        if (n == 0) return System.Array.Empty<AttackResult>();

        var results = new AttackResult[n];
        for (int i = 0; i < n; i++)
        {
            Candidate c = _cands[i];
            float mul = Mathf.Lerp(1f, minDamageMul, Mathf.Clamp01(c.dist / radius));
            results[i] = Resolve(damage * mul, ua, c.unit, c.obj, c.point);
        }
        return results;
    }

    // Rolls crit, applies damage and builds the result. Single place for the
    // hit logic so every attack type can't drift apart.
    private static AttackResult Resolve(float baseDamage, UnitAttribute ua, UnitAttribute hitUnit, GameObject target, Vector2 point)
    {
        bool isCrit = RNG.GetPercent() <= ua.CritRate;
        float damage = isCrit ? baseDamage * ua.CritDamage : baseDamage;

        Combat.DealDamage(damage, out float finalDamage, ua, hitUnit);

        return new AttackResult
        {
            DidHit = true,
            IsCrit = isCrit,
            Target = target,
            HitPoint = point,
            HitUnit = hitUnit,
            Damage = finalDamage
        };
    }
    #endregion
}