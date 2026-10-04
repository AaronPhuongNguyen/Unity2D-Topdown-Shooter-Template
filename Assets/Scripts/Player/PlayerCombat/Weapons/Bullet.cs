using UnityEngine;

/// <summary>
/// Pooled bullet with an optional TrailRenderer. Mobile-conscious:
/// - Trail uses Time-based (not Distance-based) renderer settings kept
///   light (short time, few vertices) so it stays cheap per-instance.
/// - Trail is explicitly Clear()'d on every (re)launch, since pooled
///   objects get teleported to a new muzzle position - without clearing,
///   the trail would draw one long streak from its old despawn position
///   to the new spawn position the instant it's reactivated.
/// - Single shared Material reference (assign the same TrailRenderer
///   material across all bullet prefab variants) so trails from many
///   simultaneous bullets batch together instead of each being a unique
///   draw call - the actual mobile-perf-relevant part.
/// </summary>
public class Bullet : MonoBehaviour, ITick
{
    #region Inspector
    [SerializeField] private TrailRenderer trail; // optional - leave null for no trail
    #endregion

    #region Cache
    private Vector2 targetPoint;
    private float speed;
    private float lifeRemaining;
    private bool isLaunched;
    #endregion

    #region Lifecycle
    private void Reset()
    {
        if (trail == null) trail = GetComponent<TrailRenderer>();
    }

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }
    #endregion

    #region Public API
    public void Launch(Vector2 target, float bulletSpeed, float lifetime)
    {
        targetPoint = target;
        speed = bulletSpeed;
        lifeRemaining = lifetime;
        isLaunched = true;

        Vector2 dir = (targetPoint - (Vector2)transform.position).normalized;
        if (dir != Vector2.zero)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        // Prevents a pooled bullet's reactivation from drawing a stray
        // trail segment connecting its old despawn point to this new
        // muzzle position - this is the #1 visual bug with pooled trails.
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }
    }
    #endregion

    #region Tick
    public void Tick(float delta)
    {
        if (!isLaunched) return;

        transform.position = Vector2.MoveTowards(transform.position, targetPoint, speed * delta);

        lifeRemaining -= delta;

        bool reachedTarget = Vector2.Distance(transform.position, targetPoint) < 0.05f;

        if (lifeRemaining <= 0 || reachedTarget)
        {
            Despawn();
        }
    }
    #endregion

    #region Despawn
    private void Despawn()
    {
        isLaunched = false;

        // Stop emitting before returning to pool - Clear() on next Launch
        // handles the rest, but stopping emission now prevents one extra
        // stray segment if something inspects the object between despawn
        // and reuse.
        if (trail != null)
            trail.emitting = false;

        if (PoolingSystem.instance != null)
            PoolingSystem.instance.RemoveToPool(gameObject);
    }
    #endregion
}