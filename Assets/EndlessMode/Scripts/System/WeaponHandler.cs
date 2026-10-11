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

        return Resolve(damage, ua, hitUnit, hit);
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

            results.Add(Resolve(damage * currentDamageMul, ua, hitUnit, hit));
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

    #region Shared
    // Rolls crit, applies damage and builds the result. Single place for the
    // hit logic so Shoot and ShootPierce can't drift apart.
    private static AttackResult Resolve(float baseDamage, UnitAttribute ua, UnitAttribute hitUnit, RaycastHit2D hit)
    {
        bool isCrit = RNG.GetPercent() <= ua.CritRate;
        float damage = isCrit ? baseDamage * ua.CritDamage : baseDamage;

        Combat.DealDamage(damage, out float finalDamage, ua, hitUnit);

        return new AttackResult
        {
            DidHit = true,
            IsCrit = isCrit,
            Target = hit.collider.gameObject,
            HitPoint = hit.point,
            HitUnit = hitUnit,
            Damage = finalDamage
        };
    }
    #endregion
}