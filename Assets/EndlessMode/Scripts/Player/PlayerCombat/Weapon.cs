using Server;
using UnityEngine;

public enum WeaponType
{
    Rifle,
    Shotgun,
    Sniper
}

public class Weapon : MonoBehaviour, ITick
{
    [Header("Weapon Type")]
    public WeaponType Type = WeaponType.Sniper;

    #region Common
    [Header("Common — Effects")]
    public Transform muzzle;
    public GameObject ShootEffect;

    [Header("Common — Indicator")]
    public Vector3 TargetScale;
    public Vector3 normalScale => pm.IndicatorPrefab.transform.localScale;

    [Header("Common — Bullet Visual")]
    [SerializeField] private float bulletSpeed = 40f;
    [SerializeField] private float bulletLifetime = 1f;
    #endregion

    #region Rifle Config
    [Header("Rifle — Stats")]
    [SerializeField] private float rifleFireRate = 1f;
    [SerializeField] private float rifleSpread = 1f;
    [SerializeField] private float rifleDamageBonus = 0f;
    [SerializeField] private float rifleRangeBonus = 0f;

    [Header("Rifle — Bullet")]
    [SerializeField] private Bullet rifleBulletPrefab;

    [Header("Rifle — Sound")]
    [SerializeField] private AudioClip rifleShootSound;
    #endregion

    #region Shotgun Config
    [Header("Shotgun — Stats")]
    [SerializeField] private float shotgunFireRate = 3f;
    [SerializeField] private int shotgunPelletCount = 14;
    [SerializeField] private float shotgunSpread = 5f;
    [SerializeField] private float shotgunDamageBonus = -0.65f;
    [SerializeField] private float shotgunRangeBonus = -0.25f;

    [Header("Shotgun — Bullet")]
    [SerializeField] private Bullet shotgunBulletPrefab;

    [Header("Shotgun — Sound")]
    [SerializeField] private AudioClip shotgunShootSound;
    #endregion

    #region Sniper Config
    [Header("Sniper — Stats")]
    [SerializeField] private float sniperFireRate = 2.5f;
    [SerializeField] private float sniperSpread = 0.15f;
    [SerializeField] private float sniperDamageBonus = 1.5f;
    [SerializeField] private float sniperRangeBonus = 0.8f;
    [SerializeField] private int sniperPierceTargets = 0;
    [SerializeField] private float sniperPierceFalloff = 0.85f;

    [Header("Sniper — Bullet")]
    [SerializeField] private Bullet sniperBulletPrefab;

    [Header("Sniper — Sound")]
    [SerializeField] private AudioClip sniperShootSound;
    #endregion

    #region Cache
    private PlayerManager pm => PlayerManager.instance;

    private float lastDM, lastRange;
    #endregion

    #region Runtime
    private float shootCooldown;
    private const float BaseMaxSpreadAngle = 10f;
    #endregion

    #region Unity
    private void Reset() => ApplyDefaultsForType();
    private void OnValidate() => ApplyDefaultsForType();

    public void SetType(WeaponType type)
    {
        if (type == Type) return;
        Type = type;
        ApplyDefaultsForType();
    }

    private void ApplyDefaultsForType()
    {
        if (pm == null || pm.attribute == null) return;

        pm.attribute.DealtDamage_Ampl.TotalBonus -= lastDM;
        pm.attribute.SIGHT_Ampl.TotalBonus -= lastRange;

        float dm = 0f, rg = 0f;
        switch (Type)
        {
            case WeaponType.Rifle:
                dm = rifleDamageBonus;
                rg = rifleRangeBonus;
                break;
            case WeaponType.Shotgun:
                dm = shotgunDamageBonus;
                rg = shotgunRangeBonus;
                break;
            case WeaponType.Sniper:
                dm = sniperDamageBonus;
                rg = sniperRangeBonus;
                break;
        }

        lastDM = dm;
        lastRange = rg;

        pm.attribute.DealtDamage_Ampl.TotalBonus += dm;
        pm.attribute.SIGHT_Ampl.TotalBonus += rg;
    }
    #endregion

    #region Tick
    public void Tick(float dt)
    {
        if (pm == null) return;

        if (shootCooldown > 0f) shootCooldown -= dt;

        HandleIndicator(dt);
        Fire();
    }
    #endregion

    #region Indicator
    private void HandleIndicator(float dt)
    {
        if (pm.AimIndicator == null) return;

        if (pm.Target == null)
        {
            pm.AimIndicator.SetActive(false);
            return;
        }
        else pm.AimIndicator.SetActive(true);

        pm.AimIndicator.transform.position = pm.Target.position;

        if (pm.AimIndicator.transform.localScale == normalScale) return;
        Vector3 scale = Vector3.LerpUnclamped(pm.AimIndicator.transform.localScale, normalScale, dt);
        pm.AimIndicator.transform.localScale = scale;
    }
    #endregion

    #region Fire Dispatcher
    private bool CanFire()
    {
        if (pm == null) return false;
        if (!pm.IsAttacking) return false;
        if (shootCooldown > 0f) return false;
        return true;
    }

    private void Fire()
    {
        if (!CanFire()) return;

        PlayShootSound();

        if (pm.AimIndicator != null)
            pm.AimIndicator.transform.localScale = TargetScale;

        PlayMuzzleEffect();

        switch (Type)
        {
            case WeaponType.Rifle: FireRifle(); break;
            case WeaponType.Shotgun: FireShotgun(); break;
            case WeaponType.Sniper: FireSniper(); break;
        }

        // Hit/miss feedback sounds are no longer played here - each
        // spawned Bullet now owns playing PlayHitSound (instantly, on
        // launch) or PlayMiscSound (on arrival/expiry), timed per-bullet
        // rather than once per Fire() call. See Bullet.cs.
    }
    #endregion

    #region Rifle Logic
    private void FireRifle()
    {
        shootCooldown = 0.1f * pm.attribute.ASPD_Current * rifleFireRate;

        float range = pm.attribute.SIGHT_Current;
        float maxSpread = ComputeMaxSpread(rifleSpread);

        float angle = RNG.GetFloat(-maxSpread, maxSpread);
        Vector2 dir = RotateDirection(pm.Direction, angle);

        AttackResult result = Attack.Shoot(gameObject, pm.attribute, range, dir, pm.EnemyMask);

        PlayBulletVisual(dir, result, range, rifleBulletPrefab);
        PlayHitEffect(result);
    }
    #endregion

    #region Shotgun Logic
    private void FireShotgun()
    {
        shootCooldown = 0.1f * pm.attribute.ASPD_Current * shotgunFireRate;

        float range = pm.attribute.SIGHT_Current;
        float maxSpread = ComputeMaxSpread(shotgunSpread);

        for (int i = 0; i < shotgunPelletCount; i++)
        {
            float angle = RNG.GetFloat(-maxSpread, maxSpread);
            Vector2 dir = RotateDirection(pm.Direction, angle);

            AttackResult result = Attack.Shoot(gameObject, pm.attribute, range, dir, pm.EnemyMask);

            PlayBulletVisual(dir, result, range, shotgunBulletPrefab);
            PlayHitEffect(result);
        }
    }
    #endregion

    #region Sniper Logic
    private void FireSniper()
    {
        shootCooldown = 0.1f * pm.attribute.ASPD_Current * sniperFireRate;

        float range = pm.attribute.SIGHT_Current;
        float maxSpread = ComputeMaxSpread(sniperSpread);

        float angle = RNG.GetFloat(-maxSpread, maxSpread);
        Vector2 dir = RotateDirection(pm.Direction, angle);

        AttackResult[] results = Attack.ShootPierce(
            gameObject,
            pm.attribute,
            range,
            dir,
            pm.EnemyMask,
            maxTargets: sniperPierceTargets,
            damageFalloff: sniperPierceFalloff
        );

        for (int i = 0; i < results.Length; i++)
            PlayHitEffect(results[i]);

        AttackResult last = results.Length > 0 ? results[results.Length - 1] : default;
        PlayBulletVisual(dir, last, range, sniperBulletPrefab);

        float shakeValue = last.IsCrit ? 1f : 0.2f;
        CamManager.instance?.Shake(shakeValue);
    }
    #endregion

    #region Sound
    private void PlayShootSound()
    {
        AudioClip clip = Type switch
        {
            WeaponType.Rifle => rifleShootSound,
            WeaponType.Shotgun => shotgunShootSound,
            WeaponType.Sniper => sniperShootSound,
            _ => null
        };

        // Fall back to the package's generic attack sound if this weapon
        // doesn't have its own specific shoot clip assigned.
        if (clip == null)
            clip = pm.clonedPack?.Media?.Audio?.GetAttackSound();

        if (clip == null) return;
        if (AudioManager.instance == null) return;

        AudioManager.instance.PlayAudio(clip, pm.Controlling.transform.position);
    }
    #endregion

    #region Shared Helpers
    private float ComputeMaxSpread(float spreadMultiplier)
    {
        float accuracy = Mathf.Clamp01(pm.package.ShootAccuracy);
        return BaseMaxSpreadAngle * spreadMultiplier * (1f - accuracy);
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
        if (PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(ShootEffect);
        if (o == null) return;

        o.transform.position = muzzle.position;
        o.transform.rotation = pm.Controlling.transform.rotation;
    }

    private void PlayBulletVisual(Vector2 dir, AttackResult result, float range, Bullet prefab)
    {
        if (muzzle == null || prefab == null) return;
        if (PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(prefab.gameObject);
        if (o == null) return;

        o.transform.position = muzzle.position;
        o.transform.rotation = pm.Controlling.transform.rotation;

        Vector2 targetPoint = result.DidHit
            ? result.HitPoint
            : (Vector2)muzzle.position + dir.normalized * range;

        if (o.TryGetComponent<Bullet>(out Bullet b))
            b.Launch(targetPoint, bulletSpeed, bulletLifetime, result.DidHit);
    }

    private void PlayHitEffect(AttackResult result)
    {
        if (!result.DidHit) return;
        if (pm.clonedPack.Media.HitEffect == null) return;
        if (PoolingSystem.instance == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(pm.clonedPack.Media.HitEffect);
        if (o == null) return;

        o.transform.position = result.HitPoint;

        DamagePopupManager.Show(result.HitPoint, result.Damage, result.IsCrit);
    }
    #endregion
}