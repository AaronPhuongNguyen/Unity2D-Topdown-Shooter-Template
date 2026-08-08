using UnityEngine;

public class Bullet : MonoBehaviour
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

    private void Update()
    {
        if (!isLaunched) return;

        transform.position = Vector2.MoveTowards(transform.position, targetPoint, speed * Time.deltaTime);

        lifeRemaining -= Time.deltaTime;

        bool reachedTarget = Vector2.Distance(transform.position, targetPoint) < 0.05f;

        if (lifeRemaining <= 0 || reachedTarget)
        {
            Despawn();
        }
    }

    private void Despawn()
    {
        isLaunched = false;
        PoolingSystem.instance.RemoveToPool(gameObject);
    }
}