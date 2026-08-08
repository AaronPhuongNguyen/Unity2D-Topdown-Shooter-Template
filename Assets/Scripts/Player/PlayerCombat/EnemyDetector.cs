using UnityEngine;

public class Detector : MonoBehaviour,ITick
{
    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    private Collider2D[] targets = new Collider2D[10];

    private Transform currentTarget;

    private int foundTarget;
    #endregion

    #region Tick
    public void Tick(float dt)
    {
        PerformSearch();
    }
    #endregion

    #region Functions
    float searchInterval;
    private void PerformSearch()
    {
        if (Time.time < searchInterval) return;
        searchInterval = Time.time + pm.attribute.ASPD_Current;

        SearchTarget();
        pm.Target = FindNearestTarget();
    }
    private void SearchTarget() =>
        foundTarget = Physics2D.OverlapCircleNonAlloc(pm.Controlling.transform.position, pm.attribute.SIGHT_Current, targets, pm.EnemyMask);
    private Transform FindNearestTarget()
    {
        if(targets == null || foundTarget ==0) return null;

        float nearest=999;
        int nearestAtIndex=0;
        for(int i = foundTarget-1;i>=0 ; i--)
        {
            if (targets[i] == null) continue;
            float dist = Vector2.Distance(targets[i].transform.position,pm.Controlling.transform.position);
            if (dist < nearest)
            {
                nearest = dist;
                nearestAtIndex = i;
            }
            else continue;
        }
        currentTarget = targets[nearestAtIndex].transform;
        return currentTarget;
    }
    #endregion
}