using UnityEngine;

public class Bullet : MonoBehaviour, ITick
{
    private Vector2 targetPoint;
    private float speed;
    private float lifeRemaining;
    private bool isLaunched;

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
    }

    // Pooled objects are SetActive(true)/(false)'d rather than destroyed,
    // which still fires OnEnable/OnDisable - so registration naturally
    // tracks pool lifetime with no extra hooks needed.
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

    private void Despawn()
    {
        isLaunched = false;
        if (PoolingSystem.instance != null)
            PoolingSystem.instance.RemoveToPool(gameObject);
    }
}