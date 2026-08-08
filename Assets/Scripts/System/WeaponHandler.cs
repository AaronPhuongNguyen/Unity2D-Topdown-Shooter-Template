using Server;
using UnityEngine;

public struct AttackResult
{
    public bool DidHit;
    public GameObject Target;
    public Vector2 HitPoint;
    public UnitAttribute HitUnit;
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

        Combat.DealDamage(ua.ATK_Current, ua, hitUnit);

        return new AttackResult
        {
            DidHit = true,
            Target = hit.collider.gameObject,
            HitPoint = hit.point,
            HitUnit = hitUnit
        };
    }
}