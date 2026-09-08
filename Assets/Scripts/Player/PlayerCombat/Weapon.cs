using Server;
using UnityEngine;

public enum WeaponType
{
    Rifle,
    Shotgun
}

public class Weapon : MonoBehaviour, ITick
{
    [Header("Weapon Type")]
    public WeaponType Type = WeaponType.Rifle;

    private float fireRateMultiplier = 1f;
    private int pelletCount = 1;
    private float baseMaxSpreadAngle = 10f;
    private float spreadMultiplier = 1f;
    private float damageMultiply = 1f;
    private float rangeMultiplier = 1f;

    [Header("Effects")]
    public Transform muzzle;
    public Bullet BulletPrefab;      // visual bullet/trail, spawned toward the hit point (or full range if no hit)
    public GameObject ShootEffect;   // muzzle flash particle system
    public GameObject HitEffect;     // impact particle system
    [Header("Bullet Visual")]
    [SerializeField] private float rateToSpawn = 0.5f;
    [SerializeField] private float bulletSpeed = 40f;
    [SerializeField] private float bulletLifetime = 1f;
    [Header("Indicator Settings")]
    public Vector3 TargetScale;
    public Vector3 normalScale => pm.IndicatorPrefab.transform.localScale;
    #region Cache
    private PlayerManager pm => PlayerManager.instance;

    private float lastDM, lastRange;
    #endregion
    #region Runtime
    private float ShootInterval;
    #endregion

    private void Reset()
    {
        ApplyDefaultsForType();
    }

    private void OnValidate()
    {
        ApplyDefaultsForType();
    }

    public void SetType(WeaponType type)
    {
        if (type == Type) return;
        Type = type;
        ApplyDefaultsForType();
    }

    private void ApplyDefaultsForType()
    {
        if (pm == null) return;
        if (pm.attribute == null) return;

        pm.attribute.DealtDamage_Ampl.TotalBonus -= lastDM;
        pm.attribute.SIGHT_Ampl.TotalBonus -= lastRange;

        if (Type == WeaponType.Shotgun)
        {
            fireRateMultiplier = 7f;
            pelletCount = 14;
            spreadMultiplier = 8f;
            damageMultiply = -0.25f;
            rangeMultiplier = -0.3f;
        }
        else
        {
            fireRateMultiplier = 1f;
            pelletCount = 1;
            spreadMultiplier = 1f;
            damageMultiply = 0;
            rangeMultiplier = 0;
        }
        lastDM = damageMultiply;
        lastRange = rangeMultiplier;

        pm.attribute.DealtDamage_Ampl.TotalBonus += damageMultiply;
        pm.attribute.SIGHT_Ampl.TotalBonus += rangeMultiplier;
    }

    #region Tick
    public void Tick(float dt)
    {
        HandleIndicator();
        Fire();
    }
    #endregion
    #region Functions
    private void HandleIndicator()
    {
        if (pm.Target == null)
        {
            pm.AimIndicator.SetActive(false);
            return;
        }
        else pm.AimIndicator.SetActive(true);
        pm.AimIndicator.transform.position = pm.Target.position;
        if (pm.AimIndicator.transform.localScale == normalScale) return;
        Vector3 scale = Vector3.LerpUnclamped(pm.AimIndicator.transform.localScale, normalScale, Time.deltaTime);
        pm.AimIndicator.transform.localScale = scale;
    }
    private bool CanFire()
    {
        if (pm == null) return false;
        if (!pm.IsAttacking) return false;
        if (Time.time < ShootInterval) return false;
        return true;
    }
    private void Fire()
    {
        if (!CanFire()) return;
        pm.PlayAttackSound();
        if (pm.AimIndicator != null)
        {
            pm.AimIndicator.transform.localScale = TargetScale;
        }
        ShootInterval = Time.time + pm.attribute.ASPD_Current * fireRateMultiplier;

        PlayMuzzleEffect();

        float range = pm.attribute.SIGHT_Current;

        float accuracy = Mathf.Clamp01(pm.package.ShootAccuracy);
        float maxSpreadAngle = baseMaxSpreadAngle * spreadMultiplier * (1f - accuracy);

        bool anyHit = false;

        for (int i = 0; i < pelletCount; i++)
        {
            float angleOffset = RNG.GetFloat(-maxSpreadAngle, maxSpreadAngle);
            Vector2 dir = RotateDirection(pm.Direction, angleOffset);

            AttackResult result = Attack.Shoot(gameObject, pm.attribute, range, dir, pm.EnemyMask);
            if (result.DidHit) anyHit = true;

            PlayBulletVisual(dir, result, range);
            PlayHitEffect(result);
        }

        if (!anyHit) pm.PlayMiscSound();
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

    private void PlayMuzzleEffect()
    {
        if (muzzle == null || ShootEffect == null) return;
        GameObject o = PoolingSystem.instance.GetFromPool(ShootEffect);
        if (o == null) return;
        o.transform.position = muzzle.position;
        o.transform.rotation = pm.Controlling.transform.rotation;
    }
    private void PlayBulletVisual(Vector2 dir, AttackResult result, float range)
    {
        if (muzzle == null || BulletPrefab == null) return;
        if (RNG.GetFloat(0, 1) > rateToSpawn) return;
        GameObject o = PoolingSystem.instance.GetFromPool(BulletPrefab.gameObject);
        if (o == null) return;
        o.transform.position = muzzle.position;
        o.transform.rotation = pm.Controlling.transform.rotation;
        Vector2 targetPoint = result.DidHit ? result.HitPoint : (Vector2)muzzle.position + dir.normalized * range;
        if (o.TryGetComponent<Bullet>(out Bullet b))
        {
            b.Launch(targetPoint, bulletSpeed, bulletLifetime);
        }
    }
    private void PlayHitEffect(AttackResult result)
    {
        if (!result.DidHit) return;
        if (HitEffect == null) return;
        GameObject o = PoolingSystem.instance.GetFromPool(HitEffect);
        if (o == null) return;
        o.transform.position = result.HitPoint;
    }
    #endregion
}