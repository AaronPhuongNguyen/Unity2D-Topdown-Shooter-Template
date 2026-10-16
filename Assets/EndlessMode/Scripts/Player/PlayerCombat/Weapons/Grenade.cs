using Server;
using UnityEngine;

/// <summary>
/// Pooled grenade. Flies from A to B (fake arc via scale), then explodes and deals
/// circle AoE damage through Attack.ShootCircle. Put it on a prefab with a sprite.
/// Launch with Grenade.Throw(...).
/// </summary>
public class Grenade : MonoBehaviour, ITick
{
    [Header("Flight")]
    [Tooltip("Extra scale at the top of the arc (0.6 = +60%). 0 = flat.")]
    [SerializeField] private float arcScale = 0.6f;
    [Tooltip("Degrees per second. 0 = no spin.")]
    [SerializeField] private float spinSpeed = 360f;

    [Header("Explosion")]
    [SerializeField] private GameObject explosionEffect;
    [SerializeField] private AudioClip explosionSound;

    private Vector3 baseScale;
    private Vector2 from, to;
    private float duration, elapsed;
    private bool active;

    private UnitAttribute owner;
    private float damage, radius, minDamageMul;
    private LayerMask mask;

    private void Awake() => baseScale = transform.localScale;

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

    /// <summary>Gets a grenade from the pool and throws it. Returns null if it can't.</summary>
    public static Grenade Throw(GameObject prefab, Vector2 from, Vector2 to, UnitAttribute owner,
        float damage, float radius, float minDamageMul, LayerMask mask, float flightTime)
    {
        if (prefab == null || PoolingSystem.instance == null) return null;

        GameObject o = PoolingSystem.instance.GetFromPool(prefab);
        if (o == null || !o.TryGetComponent(out Grenade g)) return null;

        g.Launch(from, to, owner, damage, radius, minDamageMul, mask, flightTime);
        return g;
    }

    private void Launch(Vector2 from, Vector2 to, UnitAttribute owner,
        float damage, float radius, float minDamageMul, LayerMask mask, float flightTime)
    {
        this.from = from;
        this.to = to;
        this.owner = owner;
        this.damage = damage;
        this.radius = radius;
        this.minDamageMul = minDamageMul;
        this.mask = mask;

        duration = Mathf.Max(0.05f, flightTime);
        elapsed = 0f;
        active = true;

        transform.position = new Vector3(from.x, from.y, transform.position.z);
        transform.localScale = baseScale;
    }

    public void Tick(float dt)
    {
        if (!active) return;

        elapsed += dt;
        float k = Mathf.Clamp01(elapsed / duration);

        Vector2 p = Vector2.Lerp(from, to, k);
        transform.position = new Vector3(p.x, p.y, transform.position.z);
        transform.localScale = baseScale * (1f + arcScale * Mathf.Sin(k * Mathf.PI));
        if (spinSpeed != 0f) transform.Rotate(0f, 0f, spinSpeed * dt);

        if (k >= 1f) Explode();
    }

    private void Explode()
    {
        active = false;
        Vector2 pos = transform.position;

        AttackResult[] hits = Attack.ShootCircle(pos, damage, owner, radius, mask, 0, minDamageMul);
        for (int i = 0; i < hits.Length; i++)
            DamagePopupManager.Show(hits[i].HitPoint, hits[i].Damage, hits[i].IsCrit);

        if (explosionEffect != null && PoolingSystem.instance != null)
        {
            GameObject fx = PoolingSystem.instance.GetFromPool(explosionEffect);
            if (fx != null) fx.transform.position = pos;
        }

        if (explosionSound != null && AudioManager.instance != null)
            AudioManager.instance.PlayDirectAudio(explosionSound);
        if (CamManager.instance != null) CamManager.instance.Shake(5f);

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
        if (PoolingSystem.instance != null) PoolingSystem.instance.RemoveToPool(gameObject);
        else Destroy(gameObject);
    }
}