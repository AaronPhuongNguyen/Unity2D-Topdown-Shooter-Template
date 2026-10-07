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
///
/// Sound timing: a bullet now knows whether its shot hit or missed.
/// - Hit: the hit sound plays instantly at launch (impact is "confirmed"
///   the moment the shot is fired - matches hit feedback already being
///   immediate elsewhere, e.g. damage popups).
/// - Miss: no sound at launch; the miss sound only plays once the bullet
///   visually arrives at its flight-end point, so "whiff" audio feedback
///   is timed to when the player actually sees the shot land/pass through,
///   not an instant sound at the moment of firing.
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
    private bool didHit;
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
    public void Launch(Vector2 target, float bulletSpeed, float lifetime, bool hit)
    {
        targetPoint = target;
        speed = bulletSpeed;
        lifeRemaining = lifetime;
        isLaunched = true;
        didHit = hit;

        Vector2 dir = (targetPoint - (Vector2)transform.position).normalized;
        if (dir != Vector2.zero)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }

        // Hit sound plays immediately - impact is "decided" the instant
        // the shot is fired, no need to wait for the visual bullet to travel.
        if (didHit && PlayerManager.instance != null)
            PlayerManager.instance.PlayHitSound();
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
            // Miss sound is deferred to here - only plays once the bullet
            // visually reaches the end of its flight path (whether that's
            // because it reached targetPoint, or its lifetime simply ran out).
            if (!didHit && PlayerManager.instance != null)
                PlayerManager.instance.PlayMiscSound();

            Despawn();
        }
    }
    #endregion

    #region Despawn
    private void Despawn()
    {
        isLaunched = false;

        if (trail != null)
            trail.emitting = false;

        if (PoolingSystem.instance != null)
            PoolingSystem.instance.RemoveToPool(gameObject);
    }
    #endregion
}