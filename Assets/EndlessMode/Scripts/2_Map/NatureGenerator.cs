using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class NatureEntry
{
    public string name = "Tree";
    public List<string> allowedLevelNames = new List<string>();
    public List<GameObject> prefabs = new List<GameObject>();
    [Range(0f, 1f)] public float density = 0.1f;
    public Vector2 scaleRange = new Vector2(0.9f, 1.1f);
    public bool randomRotation = true;
    public Transform parent;

    [Tooltip("Minimum distance (in cells) from another spawned prop of ANY entry. 0 = no spacing, 2-3 gives natural-looking gaps.")]
    [Min(0)] public int minSpacingCells = 2;
}

/// <summary>
/// Scatters nature props across the map based on what TerrainGenerator
/// already painted, restricted to the map's walkable area only.
///
/// Speed model:
///   1) PLAN runs on a background thread: finds walkable cells, shuffles them and decides
///      every spawn (which entry/prefab, rotation, scale, spacing) using plain data only.
///      The main thread just polls progress and yields, so the loading bar keeps moving.
///   2) SPAWN is the only main-thread part (Instantiate), run in a per-frame time budget.
/// Normally started by TerrainGenerator right after the terrain is finished.
/// Progress goes to LoadingHandle as a fixed number of steps.
/// </summary>
[DisallowMultipleComponent]
public class NatureGenerator : MonoBehaviour
{
    #region Inspector
    [SerializeField] private TerrainGenerator terrain;
    [SerializeField] private List<NatureEntry> entries = new List<NatureEntry>();

    private const float loadingBudget = 500f;

    private const int loadingSteps = 1000;
    #endregion

    #region Types
    // Plain-data copies of NatureEntry, safe to read from a worker thread (no Unity objects).
    private struct PlanEntry
    {
        public int[] validPrefabs;      // indices into entries[e].prefabs that are not null
        public bool[] allowedByLevel;   // [terrain level index] -> may spawn here
        public float density;
        public float scaleMin, scaleMax;
        public bool randomRotation;
        public int spacing;
    }

    private struct SpawnData
    {
        public int cell;                // x * height + y
        public int entry;
        public int prefab;
        public float rotation;
        public float scale;
    }
    #endregion

    #region Cache
    private const float PlanWeight = 0.3f;      // share of the progress bar for the background plan phase

    private DomainManager dm => DomainManager.instance;

    private bool[,] occupied;
    private Vector2Int gridSize;

    // Plan inputs (set on the main thread before the worker starts)
    private PlanEntry[] planEntries;
    private int[,] planLevelGrid;
    private Vector3 planOrigin, planDx, planDy;   // affine cell -> world, so no Tilemap calls on workers
    private bool planHasWalkable;
    private Vector2 planWalkMin, planWalkMax;
    private int planSeed;

    private volatile bool cancelRequested;
    private int planProgress;                     // 0..1000, written by the worker (Volatile)

    private bool isGenerating;
    private int pendingLoadingSteps;              // registered on LoadingHandle, not yet reported
    private int spawnDone, spawnTotal;

    public bool IsComplete { get; private set; }
    public float Progress => spawnTotal <= 0 ? (IsComplete ? 1f : 0f) : spawnDone / (float)spawnTotal;
    #endregion

    #region Loading Steps
    private void RegisterLoadingSteps(int count)
    {
        if (count <= 0 || LoadingHandle.Instance == null) return;
        LoadingHandle.Instance.AddTotalSteps(count, "Loading Nature");
        pendingLoadingSteps += count;
    }

    private void ReportLoadingSteps(int count)
    {
        count = Mathf.Min(count, pendingLoadingSteps);
        if (count <= 0) return;
        pendingLoadingSteps -= count;
        if (LoadingHandle.Instance != null)
            LoadingHandle.Instance.CompleteStep(count);
    }

    /// <summary>Reports everything still owed so the loading screen can never hang on us.</summary>
    private void FlushLoadingSteps() => ReportLoadingSteps(pendingLoadingSteps);

    /// <summary>Turns "done of total" into whole steps and reports only the new ones.</summary>
    private int ReportProgress(int done, int total, int steps, int alreadyReported)
    {
        int target = (int)((long)done * steps / Mathf.Max(1, total));
        if (target > alreadyReported)
        {
            ReportLoadingSteps(target - alreadyReported);
            return target;
        }
        return alreadyReported;
    }

    private void OnDisable()
    {
        // A disabled object's coroutines stop silently; stop the worker and settle our steps.
        cancelRequested = true;
        isGenerating = false;
        FlushLoadingSteps();
    }
    #endregion

    #region Generate
    /// <summary>Fire-and-forget (Play mode only). To wait for it, use: yield return nature.GenerateRoutine();</summary>
    [ContextMenu("Generate")]
    public void Generate()
    {
        if (isGenerating) return;
        StartCoroutine(GenerateRoutine());
    }

    public IEnumerator GenerateRoutine()
    {
        if (isGenerating) yield break;

        if (terrain == null) terrain = FindFirstObjectByType<TerrainGenerator>();
        if (terrain == null || !terrain.IsGenerated)
        {
            Debug.LogWarning("[NatureGenerator] TerrainGenerator missing or not yet generated - generate terrain first.");
            yield break;
        }

        isGenerating = true;
        cancelRequested = false;
        IsComplete = false;
        spawnDone = spawnTotal = 0;

        gridSize = terrain.GridSize;
        occupied = new bool[gridSize.x, gridSize.y];

        int steps = Mathf.Max(1, loadingSteps);
        RegisterLoadingSteps(steps);
        int reported = 0;

        PreparePlanInputs();

        // ---------- Phase 1: plan every spawn on a background thread ----------
        planProgress = 0;
        Task<List<SpawnData>> planTask = Task.Run(Plan);

        while (!planTask.IsCompleted)
        {
            float f = Volatile.Read(ref planProgress) / 1000f;
            reported = ReportProgress((int)(f * PlanWeight * 1000f), 1000, steps, reported);
            yield return null;                      // UI keeps rendering while the worker runs
        }

        if (planTask.IsFaulted)
        {
            Debug.LogException(planTask.Exception);
            Abort();
            yield break;
        }
        if (cancelRequested || terrain == null) { Abort(); yield break; }

        List<SpawnData> spawns = planTask.Result;
        spawnTotal = spawns.Count;

        if (spawnTotal == 0)
        {
            Debug.LogWarning("[NatureGenerator] Nothing to spawn - check walkable bounds, allowed levels and densities.");
            FlushLoadingSteps();
            IsComplete = true;
            isGenerating = false;
            yield break;
        }

        // ---------- Phase 2: Instantiate (main thread only) within the time budget ----------
        int h = gridSize.y;
        long budgetTicks = (long)(loadingBudget * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int s = 0; s < spawnTotal; s++)
        {
            SpawnData d = spawns[s];
            NatureEntry entry = entries[d.entry];
            GameObject prefab = entry.prefabs[d.prefab];

            if (prefab != null)
            {
                int x = d.cell / h;
                int y = d.cell % h;
                Vector3 pos = planOrigin + planDx * x + planDy * y;
                Transform parent = entry.parent != null ? entry.parent : transform;
                Quaternion rot = entry.randomRotation ? Quaternion.Euler(0f, 0f, d.rotation) : Quaternion.identity;

                GameObject obj = Instantiate(prefab, pos, rot, parent);
                obj.transform.localScale *= d.scale;
            }

            spawnDone = s + 1;

            // Instantiate is the expensive part: check the clock every few spawns.
            if ((spawnDone & 3) == 0 && sw.ElapsedTicks >= budgetTicks)
            {
                float p = PlanWeight + (1f - PlanWeight) * (spawnDone / (float)spawnTotal);
                reported = ReportProgress((int)(p * 1000f), 1000, steps, reported);
                yield return null;
                if (cancelRequested) { Abort(); yield break; }
                sw.Restart();
            }
        }

        FlushLoadingSteps();
        IsComplete = true;
        isGenerating = false;
        Debug.Log($"[NatureGenerator] Nature scatter complete ({spawnTotal} props).");
    }

    private void Abort()
    {
        FlushLoadingSteps();
        isGenerating = false;
    }

    /// <summary>Main thread: copies everything the worker needs into plain data.</summary>
    private void PreparePlanInputs()
    {
        // Cell -> world is a straight affine map for a normal Grid, so workers never call the Tilemap.
        planOrigin = terrain.GridToWorld(0, 0);
        planDx = terrain.GridToWorld(1, 0) - planOrigin;
        planDy = terrain.GridToWorld(0, 1) - planOrigin;

        planHasWalkable = dm != null;
        planWalkMin = planHasWalkable ? dm.WalkableMin : Vector2.zero;
        planWalkMax = planHasWalkable ? dm.WalkableMax : Vector2.zero;

        planLevelGrid = terrain.LevelGrid;
        planSeed = dm != null ? dm.Seed : Environment.TickCount;

        int levelCount = terrain.LevelCount;
        planEntries = new PlanEntry[entries.Count];

        for (int e = 0; e < entries.Count; e++)
        {
            NatureEntry src = entries[e];

            var valid = new List<int>();
            if (src.prefabs != null)
                for (int p = 0; p < src.prefabs.Count; p++)
                    if (src.prefabs[p] != null) valid.Add(p);

            var allowed = new bool[levelCount];
            for (int l = 0; l < levelCount; l++)
            {
                TerrainLevel level = terrain.GetLevel(l);
                allowed[l] = level != null && src.allowedLevelNames != null && src.allowedLevelNames.Contains(level.name);
            }

            planEntries[e] = new PlanEntry
            {
                validPrefabs = valid.ToArray(),
                allowedByLevel = allowed,
                density = src.density,
                scaleMin = src.scaleRange.x,
                scaleMax = src.scaleRange.y,
                randomRotation = src.randomRotation,
                spacing = src.minSpacingCells
            };
        }
    }

    /// <summary>
    /// Runs on a thread-pool thread. Plain data only, own seeded System.Random:
    /// same seed = same scatter, no shared RNG state.
    /// </summary>
    private List<SpawnData> Plan()
    {
        int w = gridSize.x;
        int h = gridSize.y;
        int total = w * h;
        var rng = new System.Random(planSeed);

        // 1) walkable candidate cells
        int[] cells = new int[total];
        int n = 0;
        for (int i = 0; i < total; i++)
        {
            if (cancelRequested) return null;

            int x = i / h;
            int y = i % h;

            bool walkable = true;
            if (planHasWalkable)
            {
                float wx = planOrigin.x + planDx.x * x + planDy.x * y;
                float wy = planOrigin.y + planDx.y * x + planDy.y * y;
                walkable = wx >= planWalkMin.x && wx <= planWalkMax.x
                        && wy >= planWalkMin.y && wy <= planWalkMax.y;
            }
            if (walkable) cells[n++] = i;

            if ((i & 4095) == 0) Volatile.Write(ref planProgress, (int)(i * 300L / total));
        }

        // 2) shuffle so props spread across the whole map
        for (int k = n - 1; k > 0; k--)
        {
            int j = rng.Next(k + 1);
            (cells[k], cells[j]) = (cells[j], cells[k]);
        }
        Volatile.Write(ref planProgress, 400);

        // 3) decide spawns
        var result = new List<SpawnData>(Mathf.Max(16, n / 4));
        for (int k = 0; k < n; k++)
        {
            if (cancelRequested) return null;

            int cell = cells[k];
            int x = cell / h;
            int y = cell % h;
            if (occupied[x, y]) continue;

            int level = planLevelGrid[x, y];

            for (int e = 0; e < planEntries.Length; e++)
            {
                PlanEntry pe = planEntries[e];
                if (pe.validPrefabs.Length == 0) continue;
                if (!pe.allowedByLevel[level]) continue;
                if (rng.NextDouble() > pe.density) continue;

                result.Add(new SpawnData
                {
                    cell = cell,
                    entry = e,
                    prefab = pe.validPrefabs[rng.Next(pe.validPrefabs.Length)],
                    rotation = pe.randomRotation ? (float)(rng.NextDouble() * 360.0) : 0f,
                    scale = pe.scaleMin + (float)rng.NextDouble() * (pe.scaleMax - pe.scaleMin)
                });

                MarkOccupied(x, y, pe.spacing);
                break;
            }

            if ((k & 4095) == 0) Volatile.Write(ref planProgress, 400 + (int)(k * 600L / Mathf.Max(1, n)));
        }

        Volatile.Write(ref planProgress, 1000);
        return result;
    }

    private void MarkOccupied(int cx, int cy, int radius)
    {
        if (radius <= 0)
        {
            occupied[cx, cy] = true;
            return;
        }

        int minX = Mathf.Max(0, cx - radius);
        int maxX = Mathf.Min(gridSize.x - 1, cx + radius);
        int minY = Mathf.Max(0, cy - radius);
        int maxY = Mathf.Min(gridSize.y - 1, cy + radius);

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                occupied[x, y] = true;
    }
    #endregion

    #region Clear
    [ContextMenu("Clear")]
    public void Clear()
    {
        cancelRequested = true;
        StopAllCoroutines();
        isGenerating = false;
        IsComplete = false;
        spawnDone = spawnTotal = 0;
        FlushLoadingSteps();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(transform.GetChild(i).gameObject);
            else
#endif
                Destroy(transform.GetChild(i).gameObject);
        }
    }
    #endregion
}