using Unity.Mathematics;
using UnityEngine;

public class Detector : MonoBehaviour, ITick
{
    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    private Collider2D[] targets = new Collider2D[16];
    private Transform currentTarget;
    private int foundTarget;

    [SerializeField] private float searchInterval = 0f;
    private float searchTimer;
    #endregion

    #region Tick
    public void Tick(float delta)
    {
        if (pm == null) return;

        // Detector's job is purely auto-targeting - it should stay idle
        // whenever AutoAttack is off (pure manual mode) OR the player is
        // actively overriding with the manual stick (hybrid mode), so it
        // never fights pm.Target against the joystick's intent, and never
        // burns a search while its result would be ignored anyway.
        if (!pm.AutoAttack || pm.ManualAttacking)
        {
            if (currentTarget != null)
            {
                currentTarget = null;
                pm.Target = null;
            }
            return;
        }

        PerformSearch(delta);
    }
    #endregion

    #region Functions
    private void PerformSearch(float delta)
    {
        if (searchInterval > 0f)
        {
            searchTimer -= delta;
            if (searchTimer > 0f) return;
            searchTimer = searchInterval;
        }

        SearchTarget();
        pm.Target = FindNearestTarget();
    }

    private void SearchTarget()
    {
        foundTarget = Physics2D.OverlapCircleNonAlloc
        (
            pm.Controlling.transform.position,
            pm.attribute.SIGHT_Current,
            targets,
            pm.EnemyMask
        );

        if (foundTarget >= targets.Length)
        {
            targets = new Collider2D[targets.Length * 2];
            SearchTarget();
        }
    }

    private Transform FindNearestTarget()
    {
        if (foundTarget <= 0)
        {
            currentTarget = null;
            return null;
        }

        float2 originPos = new float2(pm.Controlling.transform.position.x, pm.Controlling.transform.position.y);

        float nearestSqr = float.MaxValue;
        int nearestIdx = -1;

        for (int i = 0; i < foundTarget; i++)
        {
            Collider2D col = targets[i];
            if (col == null) continue;

            Vector2 p = col.transform.position;
            float2 diff = new float2(p.x, p.y) - originPos;
            float distSqr = math.dot(diff, diff);

            if (distSqr < nearestSqr)
            {
                nearestSqr = distSqr;
                nearestIdx = i;
            }
        }

        if (nearestIdx < 0)
        {
            currentTarget = null;
            return null;
        }

        currentTarget = targets[nearestIdx].transform;
        return currentTarget;
    }
    #endregion
}