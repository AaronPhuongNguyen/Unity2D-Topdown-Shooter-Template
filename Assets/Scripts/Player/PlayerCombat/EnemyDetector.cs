using Unity.Mathematics;
using UnityEngine;

// No longer registers with TickSystem directly - driven explicitly from
// PlayerCombat.Tick(), same reasoning as Weapon. Implements ITick purely
// so Tick(float) has a consistent signature callers can rely on.
public class Detector : MonoBehaviour, ITick
{
    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    private Collider2D[] targets = new Collider2D[16];
    private Transform currentTarget;
    private int foundTarget;

    // Was Time.time-based (bypasses GameSpeed/pause). Now a plain
    // accumulator driven by the delta passed into Tick, consistent with
    // the rest of the tick-driven systems.
    private float searchTimer;
    #endregion

    #region Tick
    public void Tick(float delta)
    {
        if (pm == null) return;
        PerformSearch(delta);
    }
    #endregion

    #region Functions
    private void PerformSearch(float delta)
    {
        searchTimer -= delta;
        if (searchTimer > 0f) return;
        searchTimer = pm.attribute.ASPD_Current;

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

    // Replaced the Burst IJob (scheduled then immediately .Complete()'d on
    // the same line) with a plain inline loop. For <=16-ish targets the
    // job's own overhead - native buffer bookkeeping, Burst dispatch, the
    // forced sync point from Complete() - costs more than the O(n)
    // distance comparison it's meant to speed up. A plain loop here is both
    // simpler and lower-latency: no job scheduling delay, no native array
    // allocation/disposal lifecycle, nothing to leak if OnDisable is ever
    // skipped (e.g. object destroyed while disabled).
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