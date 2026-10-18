using Server;
using UnityEngine;

/// <summary>
/// Pooled falling bomb. Travels from the plane to the ground target (scale shrinks
/// from big to normal so it reads as falling), then explodes with circle AoE damage
/// through Attack.ShootCircle - same explosion logic as Missile.
/// Launch with Bomb.Launch(...). Sprite should point RIGHT (+X).
/// </summary>
public class Bomb : MonoBehaviour, ITick
{
    [Header("Fall")]
    [Tooltip("Seconds from release to explosion.")]
    [SerializeField] private float fallTime = 0.6f;
    [Tooltip("Scale multiplier at release (shrinks to 1 when it lands).")]
    [SerializeField] private float startScaleMultiplier = 2.5f;

    [Header("Explosion")]
    [SerializeField] private GameObject[] explosionEffect;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField] private float cameraShake = 0.8f;

    private UnitAttribute owner;
    private float damage, radius, minDamageMul;
    private LayerMask mask;

    private Vector2 from, to;
    private float age;
    private bool active;
    private Vector3 baseScale = Vector3.one;
    private bool baseScaleCached;

    private void Awake() => CacheScale();

    private void CacheScale()
    {
        if (baseScaleCached) return;
        baseScale = transform.localScale;
        baseScaleCached = true;
    }

    private void OnEnable()
    {
        EventBus.OnGameRestart += Cancel;
        if (TickSystem.Instance != null) TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        EventBus.OnGameRestart -= Cancel;
        if (TickSystem.Instance != null) TickSystem.Unregister((ITick)this);
        active = false;
    }

    /// <summary>Gets a bomb from the pool and drops it. Returns null if it can't.</summary>
    public static Bomb Launch(GameObject prefab, Vector2 from, Vector2 to, UnitAttribute owner,
        float damage, float radius, float minDamageMul, LayerMask mask)
    {
        if (prefab == null || PoolingSystem.instance == null) return null;

        GameObject o = PoolingSystem.instance.GetFromPool(prefab);
        if (o == null || !o.TryGetComponent(out Bomb b)) return null;

        b.Init(from, to, owner, damage, radius, minDamageMul, mask);
        return b;
    }

    private void Init(Vector2 from, Vector2 to, UnitAttribute owner,
        float damage, float radius, float minDamageMul, LayerMask mask)
    {
        CacheScale();

        this.from = from;
        this.to = to;
        this.owner = owner;
        this.damage = damage;
        this.radius = radius;
        this.minDamageMul = minDamageMul;
        this.mask = mask;

        age = 0f;
        active = true;

        Vector2 d = to - from;
        float ang = d.sqrMagnitude > 0.0001f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : 0f;
        transform.rotation = Quaternion.Euler(0f, 0f, ang);
        transform.position = new Vector3(from.x, from.y, transform.position.z);
        transform.localScale = baseScale * startScaleMultiplier;
    }

    public void Tick(float dt)
    {
        if (!active || TickSystem.GameSpeed <= 0f) return;

        age += dt;
        float k = fallTime > 0.001f ? Mathf.Clamp01(age / fallTime) : 1f;

        Vector2 pos = Vector2.Lerp(from, to, k);
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);

        // Falls faster near the ground: scale eases down.
        transform.localScale = baseScale * Mathf.Lerp(startScaleMultiplier, 1f, k * k);

        if (k >= 1f) Explode();
    }

    private void Explode()
    {
        active = false;
        Vector2 pos = transform.position;

        AttackResult[] hits = Attack.ShootCircle(pos, damage, owner, radius, mask, 0, minDamageMul);
        for (int i = 0; i < hits.Length; i++)
            DamagePopupManager.Show(hits[i].HitPoint, hits[i].Damage, hits[i].IsCrit);

        for (int i = 0; i <= explosionEffect.Length - 1; i++)
        {
            GameObject fx = PoolingSystem.instance.GetFromPool(explosionEffect[i]);
            if (fx != null) fx.transform.position = pos;
        }

        if (explosionSound != null && AudioManager.instance != null)
            AudioManager.instance.PlayDirectAudio(explosionSound);
        if (cameraShake > 0f && CamManager.instance != null) CamManager.instance.Shake(cameraShake);

        Release();
    }

    private void Cancel()
    {
        if (!active) return;
        active = false;
        Release();
    }

    private void Release()
    {
        owner = null;
        transform.localScale = baseScale;
        if (PoolingSystem.instance != null) PoolingSystem.instance.RemoveToPool(gameObject);
        else Destroy(gameObject);
    }
}