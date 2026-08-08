using Server;
using UnityEngine;

public class Weapon : MonoBehaviour, ITick
{
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
    public Vector3 normalScale=>pm.IndicatorPrefab.transform.localScale;

    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    #endregion

    #region Runtime
    private float ShootInterval;
    #endregion

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
        Vector3 scale = Vector3.LerpUnclamped(pm.AimIndicator.transform.localScale,normalScale,Time.deltaTime);
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

        ShootInterval = Time.time + pm.attribute.ASPD_Current;

        float precision = 1 - Mathf.Clamp01(pm.package.ShootAccuracy);
        Vector2 offset = Vector2.zero;
        if(precision > 0) offset = RNG.GetVector2(-precision,precision);
        Vector2 dir = pm.Direction + offset;

        AttackResult result = Attack.Shoot(gameObject, pm.attribute, pm.attribute.SIGHT_Current, dir, pm.EnemyMask);

        if (!result.DidHit) pm.PlayMiscSound();

        PlayMuzzleEffect();
        PlayBulletVisual(result);
        PlayHitEffect(result);
    }

    private void PlayMuzzleEffect()
    {
        if (muzzle == null || ShootEffect == null) return;

        GameObject o = PoolingSystem.instance.GetFromPool(ShootEffect);
        if (o == null) return;

        o.transform.position = muzzle.position;
        o.transform.rotation = pm.Controlling.transform.rotation;
    }

    private void PlayBulletVisual(AttackResult result)
    {
        if (muzzle == null || BulletPrefab == null) return;
        if (RNG.GetFloat(0, 1) > rateToSpawn) return;

        GameObject o = PoolingSystem.instance.GetFromPool(BulletPrefab.gameObject);
        if (o == null) return;

        o.transform.position = muzzle.position;
        o.transform.rotation = pm.Controlling.transform.rotation;

        Vector2 targetPoint = result.DidHit ? result.HitPoint : muzzle.position + (Vector3)(pm.Direction * pm.attribute.SIGHT_Current);

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