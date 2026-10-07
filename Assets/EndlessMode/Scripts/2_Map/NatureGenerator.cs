using Server;
using System;
using System.Collections.Generic;
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
/// already painted, restricted to the map's walkable area only. Spawning
/// is progressive rather than one big freeze: an initial chunk goes in
/// immediately (synchronously), then the rest trickles in over time via
/// TickSystem so the game is playable while the map keeps filling in.
/// Reports progress to LoadingHandle: the preload chunk is always 1 step; the
/// drip-feed batches only count if countDripInLoading is enabled.
/// </summary>
[DisallowMultipleComponent]
public class NatureGenerator : MonoBehaviour, IUnscaledTick
{
    #region Inspector
    [SerializeField] private TerrainGenerator terrain;
    [SerializeField] private List<NatureEntry> entries = new List<NatureEntry>();

    [Header("Progressive Loading")]
    [Tooltip("Fraction of all candidate cells spawned immediately, before gameplay starts.")]
    [Range(0f, 1f)][SerializeField] private float preloadFraction = 0.25f;

    [Tooltip("Fraction of all candidate cells spawned every second after preload, until complete.")]
    [Range(0f, 1f)][SerializeField] private float ratePerSecond = 0.05f;

    [Header("Loading Screen")]
    [Tooltip("OFF: loading screen ends after the preload chunk, the rest streams in while playing.\nON: loading screen also waits for every drip batch (slower start, map fully filled).")]
    [SerializeField] private bool countDripInLoading = false;
    #endregion

    #region Cache
    private DomainManager dm => DomainManager.instance;

    private bool[,] occupied;
    private Vector2Int gridSize;

    // Shuffled candidate cells (walkable-area only), consumed progressively.
    private List<Vector2Int> pendingCells;
    private int totalCandidateCount;

    private float dripTimer;
    private bool isLoading;

    // Steps registered on LoadingHandle that haven't been reported yet.
    private int pendingLoadingSteps;

    public bool IsComplete { get; private set; }
    public float Progress => totalCandidateCount <= 0 ? 1f : 1f - ((float)(pendingCells?.Count ?? 0) / totalCandidateCount);
    #endregion

    #region Loading Steps
    private void RegisterLoadingSteps(int count)
    {
        if (count <= 0 || LoadingHandle.Instance == null) return;
        LoadingHandle.Instance.AddTotalSteps(count);
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

    /// <summary>Reports everything still outstanding so the loading screen can never hang on us.</summary>
    private void FlushLoadingSteps() => ReportLoadingSteps(pendingLoadingSteps);

    private int GetBatchCount() => Mathf.Max(1, Mathf.CeilToInt(totalCandidateCount * ratePerSecond));
    #endregion

    #region Lifecycle
    private void OnApplicationQuit()
    {
        isLoading = false;
    }

    private void OnDisable()
    {
        isLoading = false;
        FlushLoadingSteps();
        if (TickSystem.Instance != null)
            TickSystem.Unregister((IUnscaledTick)this);
    }
    #endregion

    #region Generate
    [ContextMenu("Generate")]
    public void Generate()
    {
        FlushLoadingSteps();   // a previous run might still owe steps

        if (terrain == null) terrain = FindFirstObjectByType<TerrainGenerator>();
        if (terrain == null || !terrain.IsGenerated)
        {
            Debug.LogWarning("[NatureGenerator] TerrainGenerator missing or not yet generated - generate terrain first.");
            return;
        }

        gridSize = terrain.GridSize;
        occupied = new bool[gridSize.x, gridSize.y];

        pendingCells = BuildWalkableCandidateList();
        totalCandidateCount = pendingCells.Count;
        IsComplete = false;

        if (totalCandidateCount == 0)
        {
            Debug.LogWarning("[NatureGenerator] No walkable candidate cells found - check DomainManager's walkablePercent / map bounds.");
            IsComplete = true;
            return;
        }

        // Work out how many loading steps this run owes: 1 for the preload chunk,
        // plus one per drip batch if the loading screen should wait for those too.
        int preloadCount = Mathf.CeilToInt(totalCandidateCount * preloadFraction);
        int remaining = Mathf.Max(0, totalCandidateCount - preloadCount);
        int dripSteps = (countDripInLoading && remaining > 0)
            ? Mathf.CeilToInt(remaining / (float)GetBatchCount())
            : 0;
        RegisterLoadingSteps(1 + dripSteps);

        // Preload chunk: spawned synchronously, right now, before gameplay
        // starts - this is the one-time freeze, sized small on purpose.
        SpawnBatch(preloadCount);
        ReportLoadingSteps(1);

        if (pendingCells.Count > 0)
        {
            isLoading = true;
            dripTimer = 1f;
            if (TickSystem.Instance != null)
                TickSystem.Register((IUnscaledTick)this);
        }
        else
        {
            IsComplete = true;
            FlushLoadingSteps();
        }

        Debug.Log($"[NatureGenerator] Preloaded {preloadCount}/{totalCandidateCount} cells. Remaining will stream in over time.");
    }

    /// <summary>
    /// Ticks the drip-feed of remaining props. Runs on IUnscaledTick (real
    /// time) rather than ITick, so the map keeps filling in on schedule
    /// even if the game happens to be paused/slowed during loading.
    /// </summary>
    public void UnscaledTick(float delta)
    {
        if (!isLoading) return;

        dripTimer -= delta;
        if (dripTimer > 0f) return;
        dripTimer = 1f;

        SpawnBatch(GetBatchCount());

        if (countDripInLoading)
            ReportLoadingSteps(1);

        if (pendingCells.Count == 0)
        {
            isLoading = false;
            IsComplete = true;
            FlushLoadingSteps();    // covers any rounding leftovers
            if (TickSystem.Instance != null)
                TickSystem.Unregister((IUnscaledTick)this);
            Debug.Log("[NatureGenerator] Nature scatter complete.");
        }
    }

    /// <summary>
    /// Builds the full list of cells eligible for nature spawning: inside
    /// the map's walkable bounds (DomainManager.WalkableMin/Max), then
    /// shuffled once so both the preload chunk and each timed drip pull a
    /// random spread across the whole map rather than filling top-to-bottom.
    /// </summary>
    private List<Vector2Int> BuildWalkableCandidateList()
    {
        var list = new List<Vector2Int>(gridSize.x * gridSize.y);

        bool hasWalkableBounds = dm != null;
        Vector2 walkMin = hasWalkableBounds ? dm.WalkableMin : Vector2.zero;
        Vector2 walkMax = hasWalkableBounds ? dm.WalkableMax : Vector2.zero;

        for (int x = 0; x < gridSize.x; x++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                if (hasWalkableBounds)
                {
                    Vector3 world = terrain.GridToWorld(x, y);
                    if (world.x < walkMin.x || world.x > walkMax.x) continue;
                    if (world.y < walkMin.y || world.y > walkMax.y) continue;
                }

                list.Add(new Vector2Int(x, y));
            }
        }

        Shuffle(list);
        return list;
    }

    private void Shuffle(List<Vector2Int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = RNG.GetInt(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void SpawnBatch(int count)
    {
        int spawned = 0;
        int index = pendingCells.Count - 1;

        while (spawned < count && index >= 0)
        {
            Vector2Int cell = pendingCells[index];
            pendingCells.RemoveAt(index);
            index--;

            TrySpawnAt(cell.x, cell.y);
            spawned++;
        }
    }

    private void TrySpawnAt(int gridX, int gridY)
    {
        if (occupied[gridX, gridY]) return;
        if (!terrain.TryGetLevelIndex(gridX, gridY, out int levelIndex)) return;

        TerrainLevel level = terrain.GetLevel(levelIndex);
        if (level == null) return;

        for (int i = 0; i < entries.Count; i++)
        {
            NatureEntry entry = entries[i];
            if (entry.prefabs == null || entry.prefabs.Count == 0) continue;
            if (!entry.allowedLevelNames.Contains(level.name)) continue;
            if (RNG.GetPercent() > entry.density) continue;

            GameObject prefab = entry.prefabs[RNG.GetInt(0, entry.prefabs.Count)];
            if (prefab == null) continue;

            Vector3 pos = terrain.GridToWorld(gridX, gridY);
            Transform parent = entry.parent != null ? entry.parent : transform;
            GameObject obj = Instantiate(prefab, pos, Quaternion.identity, parent);

            if (entry.randomRotation)
                obj.transform.rotation = Quaternion.Euler(0f, 0f, RNG.GetFloat(0f, 360f));

            obj.transform.localScale *= RNG.GetFloat(entry.scaleRange.x, entry.scaleRange.y);

            MarkOccupied(gridX, gridY, entry.minSpacingCells);
            break;
        }
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
        isLoading = false;
        IsComplete = false;
        pendingCells = null;
        FlushLoadingSteps();

        if (TickSystem.Instance != null)
            TickSystem.Unregister((IUnscaledTick)this);

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