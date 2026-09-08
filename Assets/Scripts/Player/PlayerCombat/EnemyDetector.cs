using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class Detector : MonoBehaviour, ITick
{
    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    private Collider2D[] targets = new Collider2D[16];
    private Transform currentTarget;
    private int foundTarget;
    private float searchInterval;
    #endregion

    #region Native Buffers
    private NativeArray<float2> positions;
    private NativeArray<float> distancesSqr;
    private NativeReference<int> nearestIndex;
    #endregion

    #region Unity Events
    private void OnEnable()
    {
        AllocateNative(targets.Length);
    }

    private void OnDisable()
    {
        DisposeNative();
    }
    #endregion

    #region Native Buffer Management
    private void AllocateNative(int size)
    {
        DisposeNative();
        positions = new NativeArray<float2>(size, Allocator.Persistent);
        distancesSqr = new NativeArray<float>(size, Allocator.Persistent);
        nearestIndex = new NativeReference<int>(Allocator.Persistent);
    }

    private void DisposeNative()
    {
        if (positions.IsCreated) positions.Dispose();
        if (distancesSqr.IsCreated) distancesSqr.Dispose();
        if (nearestIndex.IsCreated) nearestIndex.Dispose();
    }
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
            AllocateNative(targets.Length);
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

        Vector2 originPos = pm.Controlling.transform.position;

        for (int i = 0; i < foundTarget; i++)
        {
            Collider2D col = targets[i];
            Vector2 p = col != null ? (Vector2)col.transform.position : originPos;
            positions[i] = new float2(p.x, p.y);
        }

        nearestIndex.Value = -1;

        var job = new FindNearestJob
        {
            Origin = new float2(originPos.x, originPos.y),
            Positions = positions,
            Count = foundTarget,
            Distances = distancesSqr,
            NearestIndex = nearestIndex
        };

        job.Schedule().Complete();

        int idx = nearestIndex.Value;
        if (idx < 0 || idx >= foundTarget || targets[idx] == null)
        {
            currentTarget = null;
            return null;
        }

        currentTarget = targets[idx].transform;
        return currentTarget;
    }
    #endregion

    #region Job
    [BurstCompile]
    private struct FindNearestJob : IJob
    {
        [ReadOnly] public float2 Origin;
        [ReadOnly] public NativeArray<float2> Positions;
        [ReadOnly] public int Count;
        public NativeArray<float> Distances;
        public NativeReference<int> NearestIndex;

        public void Execute()
        {
            float nearestSqr = float.MaxValue;
            int nearest = -1;

            for (int i = 0; i < Count; i++)
            {
                float2 diff = Positions[i] - Origin;
                float distSqr = math.dot(diff, diff);
                Distances[i] = distSqr;

                if (distSqr < nearestSqr)
                {
                    nearestSqr = distSqr;
                    nearest = i;
                }
            }

            NearestIndex.Value = nearest;
        }
    }
    #endregion
}