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
    public static AttackResult Shoot(GameObject o, UnitAttribute ua, float range, Vector2 dir)
    {
        return Shoot(o, ua, range, dir, LayerMask.GetMask("Default"));
    }

    public static AttackResult Shoot(GameObject o, UnitAttribute ua, float range, Vector2 dir, LayerMask enemymask)
    {
        if (o == null || ua == null || range == 0) return default;

        RaycastHit2D hit = Physics2D.Raycast(o.transform.position, dir, range, enemymask);
        if (hit.collider == null) return default;

        if (!hit.collider.TryGetComponent<HurtBox>(out HurtBox target)) return default;

        UnitPackage targetPackage = target.Access();
        UnitAttribute hitUnit = targetPackage?.attribute;

        if (hitUnit == null) return default;

        float damage = ua.ATK_Current;
        bool isCrit = RNG.GetPercent() <= ua.CritRate;
        if (isCrit) damage *= ua.CritDamage;

        Combat.DealDamage(damage,out float finalDamage, ua, hitUnit);

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
    public static AttackResult[] ShootPierce(
        GameObject o,
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

            UnitPackage targetPackage = target.Access();
            UnitAttribute hitUnit = targetPackage?.attribute;
            if (hitUnit == null) continue;

            if (!alreadyHit.Add(hitUnit)) continue;

            float damage = ua.ATK_Current * currentDamageMul;
            bool isCrit = RNG.GetPercent() <= ua.CritRate;
            if (isCrit) damage *= ua.CritDamage;

            Combat.DealDamage(damage, out float finalDamage, ua, hitUnit);

            results.Add(new AttackResult
            {
                DidHit = true,
                IsCrit = isCrit,
                Target = hit.collider.gameObject,
                HitPoint = hit.point,
                HitUnit = hitUnit,
                Damage = finalDamage
            });

            currentDamageMul *= damageFalloff;

            if (maxTargets > 0 && results.Count >= maxTargets)
                break;
        }

        return results.ToArray();
    }
    public static AttackResult[] ShootPierce(
        GameObject o,
        UnitAttribute ua,
        float range,
        Vector2 dir,
        int maxTargets = 0,
        float damageFalloff = 1f)
    {
        return ShootPierce(o, ua, range, dir, LayerMask.GetMask("Default"), maxTargets, damageFalloff);
    }
}