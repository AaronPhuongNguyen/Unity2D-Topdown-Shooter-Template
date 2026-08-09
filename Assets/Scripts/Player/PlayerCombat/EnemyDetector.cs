using UnityEngine;

public class Detector : MonoBehaviour, ITick
{
    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    private readonly Collider2D[] targets = new Collider2D[10];
    private Transform currentTarget;
    private int foundTarget;
    private float searchInterval;
    #endregion

    #region Tick
    public void Tick(float dt)
    {
        PerformSearch();
    }
    #endregion

    #region Functions
    private void PerformSearch()
    {
        if (Time.time < searchInterval) return;
        searchInterval = Time.time + pm.attribute.ASPD_Current;

        SearchTarget();
        pm.Target = FindNearestTarget();
    }

    private void SearchTarget()
    {
        foundTarget = Physics2D.OverlapCircleNonAlloc(
            pm.Controlling.transform.position,
            pm.attribute.SIGHT_Current,
            targets,
            pm.EnemyMask);
    }

    private Transform FindNearestTarget()
    {
        if (foundTarget <= 0) return null;

        Vector2 originPos = pm.Controlling.transform.position;
        float nearestSqr = float.MaxValue;
        Transform nearestTransform = null;

        for (int i = 0; i < foundTarget; i++)
        {
            Collider2D col = targets[i];
            if (col == null) continue;

            float distSqr = ((Vector2)col.transform.position - originPos).sqrMagnitude;
            if (distSqr < nearestSqr)
            {
                nearestSqr = distSqr;
                nearestTransform = col.transform;
            }
        }

        currentTarget = nearestTransform;
        return currentTarget;
    }
    #endregion
}