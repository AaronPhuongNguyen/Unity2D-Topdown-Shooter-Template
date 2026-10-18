using Server;
using UnityEngine;

/// <summary>
/// Pooled missile. After launch it wanders for a random time, then locks onto the
/// target's POSITION (a snapshot, it does not follow the target afterwards) and
/// flies to that point. Explodes on arrival, or when its lifetime ends, and deals
/// circle AoE damage through Attack.ShootCircle.
/// Launch with Missile.Launch(...). Sprite should point RIGHT (+X); use
/// spriteAngleOffset if it doesn't.
/// </summary>
public class Missile : MonoBehaviour, ITick
{
    [Header("Flight")]
    [SerializeField] private float speed = 60f;
    [Tooltip("Degrees per second the missile can turn while attacking the locked position.")]
    [SerializeField] private float turnSpeed = 540f;
    [Tooltip("Speed grows from this fraction to full over accelTime.")]
    [SerializeField, Range(0.1f, 1f)] private float startSpeedRatio = 0.4f;
    [SerializeField] private float accelTime = 0.3f;
    [Tooltip("Total life. Must be longer than max wander time + travel time.")]
    [SerializeField] private float lifetime = 6f;
    [Tooltip("Explodes when this close to the locked position.")]
    [SerializeField] private float proximity = 0.8f;
    [Tooltip("Rotation added to the sprite (degrees) if it doesn't point right.")]
    [SerializeField] private float spriteAngleOffset = 0f;

    [Header("Wander")]
    [Tooltip("Wander time is random between Min and Max seconds.")]
    [SerializeField] private float wanderTimeMin = 0.5f;
    [SerializeField] private float wanderTimeMax = 1.5f;
    [Tooltip("Degrees per second the missile can turn while wandering.")]
    [SerializeField] private float wanderTurnSpeed = 270f;
    [Tooltip("Each new wander heading is the current one +/- this many degrees.")]
    [SerializeField] private float wanderAngleRange = 120f;
    [Tooltip("A new random heading is picked every random time between X and Y seconds.")]
    [SerializeField] private Vector2 wanderRetargetInterval = new Vector2(0.15f, 0.35f);

    [Header("Explosion")]
    [SerializeField] private GameObject explosionEffect;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField] private float cameraShake = 1.5f;

    private Transform target;
    private UnitAttribute owner;
    private float damage, radius, minDamageMul;
    private LayerMask mask;

    private Vector2 dir;
    private float age;
    private bool active;
    private TrailRenderer trail;

    // Wander / lock-on state
    private float wanderDuration;
    private float wanderAngle;       // heading (degrees) the missile is steering toward while wandering
    private float nextWanderPick;
    private bool locked;
    private Vector2 lockedPos;

    private void Awake() => trail = GetComponentInChildren<TrailRenderer>();

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

    /// <summary>Gets a missile from the pool and fires it. Returns null if it can't.</summary>
    public static Missile Launch(GameObject prefab, Vector2 from, Vector2 direction, Transform target,
        UnitAttribute owner, float damage, float radius, float minDamageMul, LayerMask mask)
    {
        if (prefab == null || PoolingSystem.instance == null) return null;

        GameObject o = PoolingSystem.instance.GetFromPool(prefab);
        if (o == null || !o.TryGetComponent(out Missile m)) return null;

        m.Init(from, direction, target, owner, damage, radius, minDamageMul, mask);
        return m;
    }

    private void Init(Vector2 from, Vector2 direction, Transform target, UnitAttribute owner,
        float damage, float radius, float minDamageMul, LayerMask mask)
    {
        this.target = target;
        this.owner = owner;
        this.damage = damage;
        this.radius = radius;
        this.minDamageMul = minDamageMul;
        this.mask = mask;

        dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        age = 0f;
        active = true;

        // Reset wander / lock state (pooled object, so nothing may carry over)
        locked = false;
        lockedPos = Vector2.zero;
        wanderDuration = RNG.GetFloat(Mathf.Min(wanderTimeMin, wanderTimeMax),
                                      Mathf.Max(wanderTimeMin, wanderTimeMax));
        wanderAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        nextWanderPick = 0f;

        transform.position = new Vector3(from.x, from.y, transform.position.z);
        ApplyRotation();
        if (trail != null) trail.Clear();
    }

    public void Tick(float dt)
    {
        if (!active || TickSystem.GameSpeed <= 0f) return;

        age += dt;
        Vector2 pos = transform.position;

        // Wander time is over: snapshot the target position once.
        // If there is no valid target yet, keep wandering and retry next tick.
        if (!locked && age >= wanderDuration)
            TryLockOn();

        float cur = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (locked)
        {
            // Attack phase: fly to the fixed point
            Vector2 to = lockedPos - pos;
            if (to.sqrMagnitude <= proximity * proximity)
            {
                Explode();
                return;
            }

            float want = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
            float a = Mathf.MoveTowardsAngle(cur, want, turnSpeed * dt) * Mathf.Deg2Rad;
            dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        else
        {
            // Wander phase: steer toward a random heading, pick a new one every so often
            if (age >= nextWanderPick)
            {
                wanderAngle = cur + RNG.GetFloat(-wanderAngleRange, wanderAngleRange);
                nextWanderPick = age + RNG.GetFloat(wanderRetargetInterval.x, wanderRetargetInterval.y);
            }

            float a = Mathf.MoveTowardsAngle(cur, wanderAngle, wanderTurnSpeed * dt) * Mathf.Deg2Rad;
            dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        // Move (with a short acceleration ramp)
        float k = accelTime > 0.001f ? Mathf.Clamp01(age / accelTime) : 1f;
        float v = speed * Mathf.Lerp(startSpeedRatio, 1f, k);
        pos += dir * v * dt;
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        ApplyRotation();

        if (age >= lifetime) Explode();
    }

    private bool TryLockOn()
    {
        if (target == null || !target.gameObject.activeInHierarchy) return false;

        lockedPos = target.position; // Vector2 snapshot, the missile never follows the target after this
        locked = true;
        return true;
    }

    private void ApplyRotation()
    {
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + spriteAngleOffset;
        transform.rotation = Quaternion.Euler(0f, 0f, ang);
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
        target = null;
        if (PoolingSystem.instance != null) PoolingSystem.instance.RemoveToPool(gameObject);
        else Destroy(gameObject);
    }
}