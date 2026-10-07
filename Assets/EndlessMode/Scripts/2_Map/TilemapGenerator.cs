using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Paints the tilemap from an FBM noise map, 0-1 per cell, mapped to
/// ordered TerrainLevel bands. Caches the resulting level-per-cell grid so
/// other generators (NatureGenerator, etc.) can query "what terrain is at
/// this cell" without resampling noise themselves.
///
/// Speed model:
///   1) COMPUTE (noise, level, tile variation) runs on ALL CPU cores in the background
///      (Parallel.For). The main thread only polls progress and yields each frame, so the
///      loading bar keeps moving and nothing freezes.
///   2) PAINT (Tilemap is main-thread only) runs in a per-frame time budget.
/// Workflow: Terrain -> Nature (chained at the end) -> done.
/// Progress goes to LoadingHandle as a fixed number of steps.
/// </summary>
[DisallowMultipleComponent]
public class TerrainGenerator : MonoBehaviour
{
    #region Inspector
    [Header("Tilemap")]
    [SerializeField] private Tilemap tilemap;

    [Header("Levels (any count, sorted by maxThreshold automatically)")]
    [SerializeField]
    private List<TerrainLevel> levels = new List<TerrainLevel>
    {
        new TerrainLevel { name = "Stony", maxThreshold = 0.33f },
        new TerrainLevel { name = "Sandy", maxThreshold = 0.66f },
        new TerrainLevel { name = "Grass", maxThreshold = 1f },
    };

    [Header("Noise")]
    [SerializeField] private NoiseSettings noise = NoiseSettings.Default;

    [Header("Options")]
    [SerializeField] private bool useDomainMapSize = true;
    [SerializeField] private Vector2Int manualSize = new Vector2Int(20, 20);
    [SerializeField] private float cellWorldSize = 1f;

    [Header("Next in workflow")]
    [Tooltip("Runs right after the terrain finishes. Left empty = found automatically in the scene.")]
    [SerializeField] private NatureGenerator nature;

    private const float loadingBudget = 250f;

    private const int loadingSteps = 1000;
    #endregion

    #region Cache
    private const float ComputeWeight = 0.6f;   // share of the progress bar for the background compute phase

    private DomainManager dm => DomainManager.instance;

    private int[,] levelGrid;
    private int[] tileIndex;            // per cell (x * height + y): which tile variation to use
    private Vector2Int gridSize;
    private Vector2Int gridOrigin;
    private float noiseOffsetX, noiseOffsetY;
    private uint tileSeed;

    private bool isGenerating;
    private volatile bool cancelRequested;
    private int computeColumnsDone;     // written by worker threads (Interlocked)
    private int pendingLoadingSteps;    // registered on LoadingHandle, not yet reported

    public bool IsGenerated { get; private set; }
    public Vector2Int GridSize => gridSize;
    public Vector2Int GridOrigin => gridOrigin;
    public float CellWorldSize => cellWorldSize;
    public int LevelCount => levels.Count;
    /// <summary>Level index per cell [x, y]. Read-only use; safe to read from worker threads once IsGenerated.</summary>
    public int[,] LevelGrid => levelGrid;
    #endregion

    #region Loading Steps
    private void RegisterLoadingSteps(int count)
    {
        if (count <= 0 || LoadingHandle.Instance == null) return;
        LoadingHandle.Instance.AddTotalSteps(count, "Loading Map");
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
        // A disabled object's coroutines stop silently; stop the workers and settle our steps.
        cancelRequested = true;
        isGenerating = false;
        FlushLoadingSteps();
    }
    #endregion

    #region Generate
    /// <summary>Fire-and-forget (Play mode only). To wait for it, use: yield return terrain.GenerateRoutine();</summary>
    [ContextMenu("Generate")]
    public void Generate()
    {
        if (isGenerating) return;
        StartCoroutine(GenerateRoutine());
    }

    /// <summary>Terrain, then Nature. Finishes when BOTH are done.</summary>
    public IEnumerator GenerateRoutine()
    {
        if (isGenerating) yield break;
        if (tilemap == null)
        {
            Debug.LogWarning("[TerrainGenerator] No Tilemap assigned.");
            yield break;
        }
        if (!CanGenerate()) yield break;

        isGenerating = true;
        cancelRequested = false;
        IsGenerated = false;

        levels.Sort((a, b) => a.maxThreshold.CompareTo(b.maxThreshold));

        gridSize = GetTileCount();
        gridOrigin = new Vector2Int(-gridSize.x / 2, -gridSize.y / 2);

        // Seed-derived values: same seed = same map, without touching the global RNG other systems rely on.
        uint seed = dm != null ? (uint)Mathf.Max(1, dm.Seed) : (uint)DateTime.Now.Ticks;
        var seededRandom = new Unity.Mathematics.Random(seed);
        noiseOffsetX = seededRandom.NextFloat(-10000f, 10000f);
        noiseOffsetY = seededRandom.NextFloat(-10000f, 10000f);
        tileSeed = seed;

        int w = gridSize.x;
        int h = gridSize.y;
        levelGrid = new int[w, h];
        tileIndex = new int[w * h];

        int steps = Mathf.Max(1, loadingSteps);
        RegisterLoadingSteps(steps);
        int reported = 0;

        tilemap.ClearAllTiles();

        // ---------- Phase 1: compute on all cores, main thread just polls ----------
        computeColumnsDone = 0;
        Task compute = Task.Run(ComputeAll);

        while (!compute.IsCompleted)
        {
            float f = Volatile.Read(ref computeColumnsDone) / (float)w;
            reported = ReportProgress((int)(f * ComputeWeight * 1000f), 1000, steps, reported);
            yield return null;                      // UI keeps rendering while the cores work
        }

        if (compute.IsFaulted)
        {
            Debug.LogException(compute.Exception);
            Abort();
            yield break;
        }
        if (cancelRequested || tilemap == null) { Abort(); yield break; }

        // ---------- Phase 2: paint (main thread only), a column at a time within the time budget ----------
        var column = new TileBase[h];
        long budgetTicks = (long)(loadingBudget * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
                column[y] = levels[levelGrid[x, y]].tiles[tileIndex[x * h + y]];

            tilemap.SetTilesBlock(new BoundsInt(gridOrigin.x + x, gridOrigin.y, 0, 1, h, 1), column);

            if (sw.ElapsedTicks >= budgetTicks)
            {
                float p = ComputeWeight + (1f - ComputeWeight) * ((x + 1) / (float)w);
                reported = ReportProgress((int)(p * 1000f), 1000, steps, reported);
                yield return null;
                if (tilemap == null || cancelRequested) { Abort(); yield break; }
                sw.Restart();
            }
        }

        tileIndex = null;                           // free memory, only needed for painting
        IsGenerated = true;
        isGenerating = false;
        FlushLoadingSteps();                        // rounding leftovers: terrain's share is now 100%

        Debug.Log($"[TerrainGenerator] Generated {w}x{h} tiles, {levels.Count} levels, seed: {dm?.Seed}.");

#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif

        // ---------- Next: Nature. Same frame, so LoadingHandle never sees a gap. ----------
        if (nature == null) nature = FindFirstObjectByType<NatureGenerator>();
        if (nature != null)
            yield return nature.GenerateRoutine();
        else
            Debug.LogWarning("[TerrainGenerator] No NatureGenerator found - skipping nature step.");
    }

    private void Abort()
    {
        FlushLoadingSteps();
        isGenerating = false;
    }

    /// <summary>
    /// Runs on a thread-pool thread. Only touches plain managed data (no Unity objects),
    /// each column writes its own slice, so Parallel.For needs no locks.
    /// </summary>
    private void ComputeAll()
    {
        int w = gridSize.x;
        int h = gridSize.y;
        uint seed = tileSeed;

        Parallel.For(0, w, x =>
        {
            if (cancelRequested) return;

            for (int y = 0; y < h; y++)
            {
                int li = GetLevelIndex(SampleNoise(x, y));
                levelGrid[x, y] = li;

                // Deterministic per-cell hash instead of a shared RNG: thread-safe and seed-stable.
                int tileCount = levels[li].tiles.Count;
                tileIndex[x * h + y] = (int)(Hash(x, y, seed) % (uint)tileCount);
            }

            Interlocked.Increment(ref computeColumnsDone);
        });
    }

    private static uint Hash(int x, int y, uint seed)
    {
        unchecked
        {
            uint hsh = seed ^ (uint)(x * 73856093) ^ (uint)(y * 19349663);
            hsh ^= hsh >> 16;
            hsh *= 0x7feb352dU;
            hsh ^= hsh >> 15;
            hsh *= 0x846ca68bU;
            hsh ^= hsh >> 16;
            return hsh;
        }
    }

    private float SampleNoise(int x, int y)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float noiseSum = 0f;
        float amplitudeSum = 0f;

        for (int o = 0; o < noise.octaves; o++)
        {
            float sampleX = (x + noiseOffsetX) / noise.scale * frequency;
            float sampleY = (y + noiseOffsetY) / noise.scale * frequency;

            noiseSum += Mathf.PerlinNoise(sampleX, sampleY) * amplitude;
            amplitudeSum += amplitude;

            amplitude *= noise.persistence;
            frequency *= noise.lacunarity;
        }

        return amplitudeSum > 0f ? noiseSum / amplitudeSum : 0f;
    }

    private int GetLevelIndex(float noiseValue)
    {
        for (int i = 0; i < levels.Count; i++)
        {
            if (noiseValue <= levels[i].maxThreshold) return i;
        }
        return levels.Count - 1;
    }
    #endregion

    #region Setup
    private bool CanGenerate()
    {
        if (levels == null || levels.Count == 0)
        {
            Debug.LogWarning("[TerrainGenerator] No levels configured.");
            return false;
        }
        foreach (var l in levels)
        {
            if (l.tiles == null || l.tiles.Count == 0)
            {
                Debug.LogWarning($"[TerrainGenerator] Level '{l.name}' has no tiles assigned.");
                return false;
            }
        }
        if (useDomainMapSize && dm == null)
        {
            Debug.LogWarning("[TerrainGenerator] DomainManager instance not found, and useDomainMapSize is enabled.");
            return false;
        }
        return true;
    }

    private Vector2Int GetTileCount()
    {
        if (!useDomainMapSize) return manualSize;
        Rect bounds = dm.MapBounds;
        int width = Mathf.Max(1, Mathf.RoundToInt(bounds.width / cellWorldSize));
        int height = Mathf.Max(1, Mathf.RoundToInt(bounds.height / cellWorldSize));
        return new Vector2Int(width, height);
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        cancelRequested = true;
        StopAllCoroutines();
        isGenerating = false;
        FlushLoadingSteps();

        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        tilemap.ClearAllTiles();
        IsGenerated = false;
    }
    #endregion

    #region Public Query API (used by NatureGenerator, etc.)
    public bool TryGetLevelIndex(int gridX, int gridY, out int levelIndex)
    {
        levelIndex = -1;
        if (!IsGenerated) return false;
        if (gridX < 0 || gridX >= gridSize.x || gridY < 0 || gridY >= gridSize.y) return false;
        levelIndex = levelGrid[gridX, gridY];
        return true;
    }

    public TerrainLevel GetLevel(int levelIndex) =>
        (levelIndex >= 0 && levelIndex < levels.Count) ? levels[levelIndex] : null;

    /// <summary>Main thread only (uses the Tilemap).</summary>
    public Vector3 GridToWorld(int gridX, int gridY)
    {
        Vector3Int cell = new Vector3Int(gridOrigin.x + gridX, gridOrigin.y + gridY, 0);
        return tilemap.GetCellCenterWorld(cell);
    }
    #endregion
}
[Serializable]
public struct NoiseSettings
{
    public float scale;
    [Range(1, 8)] public int octaves;
    [Range(0f, 1f)] public float persistence; // amplitude falloff per octave
    public float lacunarity;                  // frequency growth per octave

    public static NoiseSettings Default => new NoiseSettings
    {
        scale = 20f,
        octaves = 4,
        persistence = 0.5f,
        lacunarity = 2f
    };
}
[Serializable]
public class TerrainLevel
{
    public string name = "Level";
    [Range(0f, 1f)] public float maxThreshold = 1f; // covers noise values up to this (exclusive of the previous level's max)
    public List<TileBase> tiles = new List<TileBase>(); // random variation among these
}