using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>One terrain type. Bands are decided by maxElevation (types are sorted by it).</summary>
[Serializable]
public class TerrainType
{
    public string name = "Grass";
    [Tooltip("Stable ID for Nature Gen and saves. Do not change once saves exist.")]
    public int id;
    [Range(0f, 1f), Tooltip("Covers elevation up to this value (after the previous type's max).")]
    public float maxElevation = 1f;
    [Tooltip("Index into GridGenerator's Ground Types (blocking / canPlace rules come from there).")]
    public int gridGroundID = 0;
    [Tooltip("Rivers carve into the FIRST type with this flag. Also used by IsWater(). Painted on the Water Tilemap.")]
    public bool isWater;
    [Tooltip("River banks use the FIRST type with this flag (e.g. Sand).")]
    public bool isBank;
    public Color debugColor = Color.green;
    [Tooltip("Random variation among these tiles.")]
    public List<TileBase> tiles = new List<TileBase>();
}

[Serializable]
public struct TerrainNoise
{
    [Min(1f), Tooltip("Cells per noise feature. Bigger = larger continents.")]
    public float scale;
    [Range(1, 8)] public int octaves;
    [Range(0f, 1f)] public float persistence;
    [Min(1f)] public float lacunarity;
    [Range(0f, 1f), Tooltip("Pushes the map edges down toward water (0 = off, 1 = island).")]
    public float edgeFalloff;

    public static TerrainNoise Default => new TerrainNoise
    {
        scale = 120f,
        octaves = 5,
        persistence = 0.5f,
        lacunarity = 2f,
        edgeFalloff = 0.25f
    };
}

[Serializable]
public class RiverSettings
{
    public bool enabled = true;
    [Min(0)] public int count = 6;
    [Min(10)] public int minLength = 300;
    [Min(10)] public int maxLength = 900;
    [Range(0f, 1f), Tooltip("Rivers start on land at least this high (0..1 after normalizing).")]
    public float startMinElevation = 0.55f;
    [Min(0.1f)] public float stepSize = 1f;
    [Min(0f), Tooltip("How sharply the noise bends the river each step (radians).")]
    public float turnStrength = 0.35f;
    [Min(0.001f), Tooltip("Noise frequency for bending. Lower = longer lazy curves.")]
    public float turnNoiseScale = 0.02f;
    [Range(0f, 1f), Tooltip("Pull toward lower ground, so rivers run downhill to lakes.")]
    public float downhillBias = 0.15f;
    [Min(0.5f)] public float minRadius = 1.5f;
    [Min(0.5f)] public float maxRadius = 3.5f;
    [Min(0), Tooltip("Cells of bank (e.g. sand) on each side of the river. 0 = none.")]
    public int bankWidth = 1;
}

/// <summary>
/// Perlin elevation map -> terrain types -> Tilemaps, plus Perlin-worm rivers.
/// Land goes on the Ground Tilemap, water on its own Water Tilemap (own material later).
/// Size and origin come from GridGenerator, seed from WorldScript. After painting,
/// every cell's ground type is pushed to the grid. Access anywhere: Terrain_Generator.Instance.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-3)]
public class Terrain_Generator : MonoBehaviour
{
    #region Singleton
    public static Terrain_Generator Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }
    #endregion

    #region Inspector
    [Header("References")]
    [Tooltip("Land tiles (dirt, grass, stone, sand...).")]
    [SerializeField] private Tilemap tilemap;
    [Tooltip("Water tiles only. A separate Tilemap so it can use its own material. Empty = water is painted on the Ground Tilemap.")]
    [SerializeField] private Tilemap waterTilemap;
    [Tooltip("Optional: tile placed on the Ground Tilemap UNDER water (e.g. a seabed), useful for transparent water materials.")]
    [SerializeField] private TileBase underWaterTile;
    [SerializeField] private GridGenerator grid;

    [Header("Start")]
    [Tooltip("Turn off if another script calls GenerateRoutine().")]
    [SerializeField] private bool generateOnStart = true;
    [Tooltip("Used only when there is no WorldManager / WorldScript.")]
    [SerializeField] private uint fallbackSeed = 1062008;

    [Header("Terrain types (sorted by Max Elevation automatically)")]
    [SerializeField]
    private List<TerrainType> terrainTypes = new List<TerrainType>
    {
        new TerrainType { name = "Water", id = 0, maxElevation = 0.30f, gridGroundID = 1, isWater = true, debugColor = new Color(0.15f, 0.4f, 0.95f) },
        new TerrainType { name = "Sand",  id = 1, maxElevation = 0.36f, gridGroundID = 0, isBank = true,  debugColor = new Color(0.95f, 0.85f, 0.5f) },
        new TerrainType { name = "Dirt",  id = 2, maxElevation = 0.50f, gridGroundID = 0, debugColor = new Color(0.55f, 0.35f, 0.18f) },
        new TerrainType { name = "Grass", id = 3, maxElevation = 0.80f, gridGroundID = 0, debugColor = new Color(0.25f, 0.75f, 0.25f) },
        new TerrainType { name = "Stone", id = 4, maxElevation = 1.00f, gridGroundID = 0, debugColor = new Color(0.55f, 0.55f, 0.6f) },
    };

    [Header("Noise")]
    [SerializeField] private TerrainNoise noiseSettings = TerrainNoise.Default;

    [Header("Rivers (Perlin worm)")]
    [SerializeField] private RiverSettings rivers = new RiverSettings();

    [Header("Debug (editor only)")]
    [SerializeField] private bool drawMapOverlay = true;
    [Range(0.1f, 1f)][SerializeField] private float overlayAlpha = 0.6f;
    [Tooltip("Tick this if the overlay appears upside down in the Scene view.")]
    [SerializeField] private bool flipOverlayY = false;
    [SerializeField] private bool drawLocalCells = true;
    [Range(1, 40)][SerializeField] private int gizmoRadius = 12;
    [SerializeField] private Transform debugFollow;
    [SerializeField] private Vector3Int debugCell;
    [TextArea(3, 6)][SerializeField] private string debugInfo;

    private const int loadingSteps = 1000;
    private const float ComputeWeight = 0.5f;
    private const float PaintWeight = 0.35f;     // the rest goes to applying the grid
    private const float loadingBudget = 250f;    // ms per frame for painting
    #endregion

    #region Data
    private Vector2Int gridSize, gridOrigin;
    private Vector2 noiseOffset;
    private uint tileSeed, riverSeed;

    // Plain copies for the worker thread (index = terrain type index)
    private float[] typeMax;
    private int[] typeGround, typeTiles;
    private bool[] typeWater;
    private int waterIdx, bankIdx;

    // Per cell, index = x * height + y
    private float[] elevation;
    private ushort[] typeMap;
    private ushort[] groundMap;
    private ushort[] tileVar;
    private int[] typeCounts;

    private bool isGenerating;
    private volatile bool cancelRequested;
    private int unitsDone, totalUnits;
    private int pendingLoadingSteps;

    public bool IsGenerated { get; private set; }
    public bool IsGenerating => isGenerating;
    public IReadOnlyList<TerrainType> Types => terrainTypes;
    public Tilemap GroundTilemap => tilemap;
    public Tilemap WaterTilemap => waterTilemap != null ? waterTilemap : tilemap;
    public event Action OnGenerated;
    #endregion

    #region Loading Steps
    private void RegisterLoadingSteps(int count)
    {
        if (count <= 0 || LoadingHandle.Instance == null) return;
        LoadingHandle.Instance.AddTotalSteps(count, "Terrain");
        pendingLoadingSteps += count;
    }

    private void ReportLoadingSteps(int count)
    {
        count = Mathf.Min(count, pendingLoadingSteps);
        if (count <= 0) return;
        pendingLoadingSteps -= count;
        if (LoadingHandle.Instance != null) LoadingHandle.Instance.CompleteStep(count);
    }

    private void FlushLoadingSteps() => ReportLoadingSteps(pendingLoadingSteps);

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
        cancelRequested = true;
        isGenerating = false;
        FlushLoadingSteps();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
    #endregion

    #region Generate
    private void Start()
    {
        if (generateOnStart) Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (isGenerating) return;
        StartCoroutine(GenerateRoutine());
    }

    public IEnumerator GenerateRoutine()
    {
        if (isGenerating) yield break;
        if (!CanGenerate()) yield break;

        isGenerating = true;
        cancelRequested = false;
        IsGenerated = false;

        // ---- make sure the grid exists first (works for any start order) ----
        if (!grid.IsGenerated)
        {
            if (grid.IsGenerating) { while (grid.IsGenerating) yield return null; }
            else yield return grid.GenerateRoutine();
        }
        if (!grid.IsGenerated)
        {
            Debug.LogWarning("[Terrain_Generator] Grid failed to generate.", this);
            Abort();
            yield break;
        }

        gridSize = grid.GridSize;
        gridOrigin = grid.MinCell;
        int w = gridSize.x, h = gridSize.y;

        // ---- RNG setup: WorldScript seed -> S_RNG -> noise offsets / river seed ----
        uint seed = fallbackSeed;
        WorldManager wm = WorldManager.instance;
        if (wm != null && wm.data != null && wm.data.WorldScript != null) seed = wm.data.WorldScript.Seed;

        S_RNG.SetSeed(seed);                                   // resets the global stream to this seed
        noiseOffset = S_RNG.GetVector2(-10000f, 10000f);
        riverSeed = (uint)S_RNG.GetInt(1, int.MaxValue);
        tileSeed = seed;

        PrepareInputs();

        elevation = new float[w * h];
        typeMap = new ushort[w * h];
        groundMap = new ushort[w * h];
        tileVar = new ushort[w * h];

        int steps = Mathf.Max(1, loadingSteps);
        RegisterLoadingSteps(steps);
        int reported = 0;

        // ---------- Phase 1: compute on background threads ----------
        int riverUnits = rivers.enabled ? Mathf.Max(1, rivers.count) : 1;
        totalUnits = 2 * w + riverUnits;
        unitsDone = 0;
        Task compute = Task.Run(ComputeAll);

        while (!compute.IsCompleted)
        {
            float f = Volatile.Read(ref unitsDone) / (float)totalUnits;
            reported = ReportProgress((int)(f * ComputeWeight * 1000f), 1000, steps, reported);
            yield return null;
        }

        if (compute.IsFaulted) { Debug.LogException(compute.Exception); Abort(); yield break; }
        if (cancelRequested || tilemap == null) { Abort(); yield break; }

        // ---------- Phase 2: paint the Tilemaps (land and water separately) ----------
        tilemap.ClearAllTiles();
        bool separateWater = waterTilemap != null && waterTilemap != tilemap;
        if (separateWater) waterTilemap.ClearAllTiles();

        var groundColumn = new TileBase[h];
        var waterColumn = separateWater ? new TileBase[h] : null;
        long budgetTicks = (long)(loadingBudget * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int i = x * h + y;
                TerrainType t = terrainTypes[typeMap[i]];
                TileBase tile = t.tiles[tileVar[i]];

                if (separateWater && t.isWater)
                {
                    groundColumn[y] = underWaterTile;   // seabed (or nothing)
                    waterColumn[y] = tile;
                }
                else
                {
                    groundColumn[y] = tile;
                    if (separateWater) waterColumn[y] = null;
                }
            }

            var bounds = new BoundsInt(gridOrigin.x + x, gridOrigin.y, 0, 1, h, 1);
            tilemap.SetTilesBlock(bounds, groundColumn);
            if (separateWater) waterTilemap.SetTilesBlock(bounds, waterColumn);

            if (sw.ElapsedTicks >= budgetTicks)
            {
                float p = ComputeWeight + PaintWeight * ((x + 1) / (float)w);
                reported = ReportProgress((int)(p * 1000f), 1000, steps, reported);
                yield return null;
                if (tilemap == null || cancelRequested) { Abort(); yield break; }
                sw.Restart();
            }
        }
        tileVar = null;

        // ---------- Phase 3: push ground types into the grid ----------
        float gridStart = ComputeWeight + PaintWeight;
        yield return grid.ApplyGroundMapRoutine(groundMap, p =>
        {
            reported = ReportProgress((int)((gridStart + (1f - gridStart) * p) * 1000f), 1000, steps, reported);
        });
        groundMap = null;

        FlushLoadingSteps();
        IsGenerated = true;
        isGenerating = false;

        LogCounts();
#if UNITY_EDITOR
        BuildOverlay();
#endif
        OnGenerated?.Invoke();
    }

    private void Abort()
    {
        FlushLoadingSteps();
        isGenerating = false;
    }

    private bool CanGenerate()
    {
        if (tilemap == null) { Debug.LogWarning("[Terrain_Generator] No Ground Tilemap assigned.", this); return false; }
        if (grid == null) grid = FindFirstObjectByType<GridGenerator>();
        if (grid == null) { Debug.LogWarning("[Terrain_Generator] No GridGenerator found.", this); return false; }
        if (terrainTypes == null || terrainTypes.Count == 0) { Debug.LogWarning("[Terrain_Generator] No terrain types.", this); return false; }

        foreach (TerrainType t in terrainTypes)
        {
            if (t.tiles == null || t.tiles.Count == 0 || t.tiles.Contains(null))
            {
                Debug.LogWarning($"[Terrain_Generator] Type '{t.name}' has no tiles, or an empty tile slot.", this);
                return false;
            }
        }

        if (waterTilemap == null)
            Debug.LogWarning("[Terrain_Generator] No Water Tilemap assigned, water will be painted on the Ground Tilemap.", this);
        return true;
    }

    /// <summary>Main thread: sort the types and copy what the worker needs into plain arrays.</summary>
    private void PrepareInputs()
    {
        terrainTypes.Sort((a, b) => a.maxElevation.CompareTo(b.maxElevation));

        int n = terrainTypes.Count;
        typeMax = new float[n];
        typeGround = new int[n];
        typeTiles = new int[n];
        typeWater = new bool[n];
        waterIdx = bankIdx = -1;

        for (int i = 0; i < n; i++)
        {
            TerrainType t = terrainTypes[i];
            typeMax[i] = t.maxElevation;
            typeGround[i] = Mathf.Max(0, t.gridGroundID);
            typeTiles[i] = t.tiles.Count;
            typeWater[i] = t.isWater;
            if (t.isWater && waterIdx < 0) waterIdx = i;
            if (t.isBank && bankIdx < 0) bankIdx = i;

            if (t.gridGroundID >= grid.GroundTypeCount)
                Debug.LogWarning($"[Terrain_Generator] '{t.name}' uses Grid Ground ID {t.gridGroundID}, " +
                                 $"but GridGenerator only has {grid.GroundTypeCount} Ground Types.", this);
        }

        if (rivers.enabled && waterIdx < 0)
        {
            Debug.LogWarning("[Terrain_Generator] Rivers need a type with 'Is Water' ticked. Rivers skipped.", this);
            rivers.enabled = false;
        }
    }
    #endregion

    #region Worker thread
    /// <summary>Plain data only, no Unity objects.</summary>
    private void ComputeAll()
    {
        int w = gridSize.x, h = gridSize.y;

        // 1) elevation (all cores, one column per task)
        Parallel.For(0, w, x =>
        {
            if (cancelRequested) return;
            for (int y = 0; y < h; y++) elevation[x * h + y] = SampleElevation(x, y, w, h);
            Interlocked.Increment(ref unitsDone);
        });
        if (cancelRequested) return;

        Normalize();

        // 2) rivers
        var river = new bool[w * h];
        var bank = new bool[w * h];
        if (rivers.enabled && waterIdx >= 0) CarveRivers(river, bank, w, h);
        else Interlocked.Increment(ref unitsDone);
        if (cancelRequested) return;

        // 3) classify every cell, ground ID and tile variation
        uint seed = tileSeed;
        Parallel.For(0, w, x =>
        {
            if (cancelRequested) return;
            for (int y = 0; y < h; y++)
            {
                int i = x * h + y;
                int t = BandOf(elevation[i]);

                if (river[i]) t = waterIdx;
                else if (bank[i] && bankIdx >= 0 && !typeWater[t]) t = bankIdx;

                typeMap[i] = (ushort)t;
                groundMap[i] = (ushort)typeGround[t];
                tileVar[i] = (ushort)(Hash(x, y, seed) % (uint)typeTiles[t]);
            }
            Interlocked.Increment(ref unitsDone);
        });

        // 4) counts for the debug log
        var counts = new int[typeMax.Length];
        for (int i = 0; i < typeMap.Length; i++) counts[typeMap[i]]++;
        typeCounts = counts;
    }

    private static float Snoise(float x, float y)
        => Unity.Mathematics.noise.snoise(new Unity.Mathematics.float2(x, y));

    private float SampleElevation(int x, int y, int w, int h)
    {
        float amp = 1f, freq = 1f, sum = 0f, ampSum = 0f;
        for (int o = 0; o < noiseSettings.octaves; o++)
        {
            float sx = (x + noiseOffset.x) / noiseSettings.scale * freq;
            float sy = (y + noiseOffset.y) / noiseSettings.scale * freq;
            sum += Snoise(sx, sy) * amp;
            ampSum += amp;
            amp *= noiseSettings.persistence;
            freq *= noiseSettings.lacunarity;
        }

        float e = ampSum > 0f ? sum / ampSum * 0.5f + 0.5f : 0.5f;   // -1..1 -> 0..1

        if (noiseSettings.edgeFalloff > 0f)
        {
            float nx = x / (float)Mathf.Max(1, w - 1) * 2f - 1f;
            float ny = y / (float)Mathf.Max(1, h - 1) * 2f - 1f;
            float d = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
            e = Mathf.Lerp(e, e - d * d, noiseSettings.edgeFalloff);
        }
        return e;
    }

    /// <summary>Stretches the map to 0..1 so the type thresholds behave predictably.</summary>
    private void Normalize()
    {
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < elevation.Length; i++)
        {
            float e = elevation[i];
            if (e < min) min = e;
            if (e > max) max = e;
        }
        float range = max - min;
        if (range < 1e-6f) return;
        for (int i = 0; i < elevation.Length; i++) elevation[i] = (elevation[i] - min) / range;
    }

    private int BandOf(float e)
    {
        for (int i = 0; i < typeMax.Length; i++)
            if (e <= typeMax[i]) return i;
        return typeMax.Length - 1;
    }

    private float ElevationAt(float x, float y, int w, int h)
    {
        int ix = (int)x, iy = (int)y;
        if (ix < 0 || iy < 0 || ix >= w || iy >= h) return 0f;   // off the map counts as low
        return elevation[ix * h + iy];
    }

    /// <summary>
    /// Perlin worm: a walker whose heading is bent by noise each step and nudged downhill,
    /// carving a disc of water behind it. It stops at the map edge or when it reaches low ground.
    /// </summary>
    private void CarveRivers(bool[] river, bool[] bank, int w, int h)
    {
        var rng = new Unity.Mathematics.Random(riverSeed == 0 ? 1u : riverSeed);
        RiverSettings rs = rivers;
        float waterLevel = typeMax[waterIdx];

        for (int r = 0; r < rs.count; r++)
        {
            if (cancelRequested) return;

            // pick a start on high ground (a few tries, then accept whatever we got)
            float x = 0f, y = 0f;
            for (int t = 0; t < 80; t++)
            {
                x = rng.NextFloat(0f, w);
                y = rng.NextFloat(0f, h);
                if (ElevationAt(x, y, w, h) >= rs.startMinElevation) break;
            }

            float angle = rng.NextFloat(0f, Mathf.PI * 2f);
            float baseRadius = rng.NextFloat(rs.minRadius, Mathf.Max(rs.minRadius, rs.maxRadius));
            int length = rng.NextInt(rs.minLength, Mathf.Max(rs.minLength, rs.maxLength) + 1);
            float seedShift = rng.NextFloat(-1000f, 1000f);

            for (int s = 0; s < length; s++)
            {
                float n = Snoise(x * rs.turnNoiseScale + seedShift, y * rs.turnNoiseScale - seedShift);
                angle += n * rs.turnStrength;

                if (rs.downhillBias > 0f)
                {
                    float best = angle, bestE = float.MaxValue;
                    for (int k = -1; k <= 1; k++)
                    {
                        float a = angle + k * 0.6f;
                        float e = ElevationAt(x + Mathf.Cos(a) * 4f, y + Mathf.Sin(a) * 4f, w, h);
                        if (e < bestE) { bestE = e; best = a; }
                    }
                    angle += (best - angle) * rs.downhillBias;
                }

                x += Mathf.Cos(angle) * rs.stepSize;
                y += Mathf.Sin(angle) * rs.stepSize;
                if (x < 0f || y < 0f || x >= w || y >= h) break;

                float radius = baseRadius * (0.8f + 0.4f * (n * 0.5f + 0.5f));
                Stamp(river, x, y, radius, w, h);
                if (rs.bankWidth > 0) Stamp(bank, x, y, radius + rs.bankWidth, w, h);

                // reached a lake / sea: stop (after the first few steps so it can leave its start)
                if (s > 10 && ElevationAt(x, y, w, h) <= waterLevel) break;
            }
            Interlocked.Increment(ref unitsDone);
        }
    }

    private static void Stamp(bool[] mask, float cx, float cy, float radius, int w, int h)
    {
        int r = Mathf.CeilToInt(radius);
        int ix = (int)cx, iy = (int)cy;
        float r2 = radius * radius;

        for (int dx = -r; dx <= r; dx++)
        {
            int px = ix + dx;
            if (px < 0 || px >= w) continue;
            for (int dy = -r; dy <= r; dy++)
            {
                int py = iy + dy;
                if (py < 0 || py >= h) continue;
                if (dx * dx + dy * dy <= r2) mask[px * h + py] = true;
            }
        }
    }

    private static uint Hash(int x, int y, uint seed)
    {
        unchecked
        {
            uint hsh = seed ^ (uint)(x * 73856093) ^ (uint)(y * 19349663);
            hsh ^= hsh >> 16; hsh *= 0x7feb352dU;
            hsh ^= hsh >> 15; hsh *= 0x846ca68bU;
            hsh ^= hsh >> 16;
            return hsh;
        }
    }
    #endregion

    #region Query API (for Nature Gen etc.)
    private bool TryIndex(Vector3Int cell, out int i)
    {
        i = -1;
        if (!IsGenerated || typeMap == null) return false;
        int x = cell.x - gridOrigin.x, y = cell.y - gridOrigin.y;
        if (x < 0 || y < 0 || x >= gridSize.x || y >= gridSize.y) return false;
        i = x * gridSize.y + y;
        return true;
    }

    /// <summary>Stable terrain ID at a cell (TerrainType.id).</summary>
    public bool TryGetTerrainID(Vector3Int cell, out int id)
    {
        id = -1;
        if (!TryIndex(cell, out int i)) return false;
        id = terrainTypes[typeMap[i]].id;
        return true;
    }

    public TerrainType GetTerrainType(Vector3Int cell)
        => TryIndex(cell, out int i) ? terrainTypes[typeMap[i]] : null;

    public TerrainType GetTypeByID(int id)
    {
        for (int i = 0; i < terrainTypes.Count; i++)
            if (terrainTypes[i].id == id) return terrainTypes[i];
        return null;
    }

    public bool IsWater(Vector3Int cell)
        => TryIndex(cell, out int i) && terrainTypes[typeMap[i]].isWater;

    /// <summary>Normalized 0..1 height, or -1 outside the map.</summary>
    public float GetElevation(Vector3Int cell)
        => TryIndex(cell, out int i) ? elevation[i] : -1f;
    #endregion

    #region Clear
    [ContextMenu("Clear")]
    public void Clear()
    {
        cancelRequested = true;
        StopAllCoroutines();
        isGenerating = false;
        IsGenerated = false;
        FlushLoadingSteps();
        if (tilemap != null) tilemap.ClearAllTiles();
        if (waterTilemap != null) waterTilemap.ClearAllTiles();
        elevation = null; typeMap = null;
    }

    [ContextMenu("Log Terrain Counts")]
    private void LogCounts()
    {
        if (typeCounts == null) return;
        long total = 0;
        foreach (int c in typeCounts) total += c;

        var sb = new System.Text.StringBuilder($"[Terrain_Generator] Seed {S_RNG.Seed}, {total} cells:");
        for (int i = 0; i < typeCounts.Length && i < terrainTypes.Count; i++)
            sb.Append($"\n  {terrainTypes[i].name} (ID {terrainTypes[i].id}): {typeCounts[i]} ({typeCounts[i] * 100f / Mathf.Max(1, total):F1}%)");
        Debug.Log(sb.ToString(), this);
    }
    #endregion

    #region Debug
    public string DescribeCell(Vector3Int cell)
    {
        if (!TryIndex(cell, out int i)) return $"Cell {cell.x},{cell.y}: no terrain data.";
        TerrainType t = terrainTypes[typeMap[i]];
        return $"Cell {cell.x},{cell.y}  {t.name} (ID {t.id})\n" +
               $"  Elevation: {elevation[i]:F3}   Water: {t.isWater}   Grid ground ID: {t.gridGroundID}";
    }

#if UNITY_EDITOR
    private Texture2D overlay;

    private void Update()
    {
        if (!IsGenerated) return;
        if (debugFollow != null) debugCell = grid != null ? grid.WorldToCell(debugFollow.position) : debugCell;
        debugInfo = DescribeCell(debugCell);
    }

    /// <summary>One pixel per cell, colored by terrain type. Water = blue, dirt = brown, etc.</summary>
    [ContextMenu("Rebuild Debug Overlay")]
    private void BuildOverlay()
    {
        if (!IsGenerated || typeMap == null) return;
        int w = gridSize.x, h = gridSize.y;

        if (overlay == null || overlay.width != w || overlay.height != h)
        {
            if (overlay != null) DestroyImmediate(overlay);
            overlay = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        var colors = new Color32[terrainTypes.Count];
        for (int t = 0; t < colors.Length; t++)
        {
            Color c = terrainTypes[t].debugColor;
            c.a = overlayAlpha;
            colors[t] = c;
        }

        var pixels = new Color32[w * h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                pixels[y * w + x] = colors[typeMap[x * h + y]];

        overlay.SetPixels32(pixels);
        overlay.Apply(false);
    }

    private void OnValidate()
    {
        if (Application.isPlaying && IsGenerated) BuildOverlay();   // alpha / color edits
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !IsGenerated) return;
        float cs = grid != null ? grid.CellWorldSize : 1f;

        // Whole map, one texture draw.
        if (drawMapOverlay && overlay != null)
        {
            float rx = gridOrigin.x * cs, ry = gridOrigin.y * cs;
            float rw = gridSize.x * cs, rh = gridSize.y * cs;
            Rect rect = flipOverlayY ? new Rect(rx, ry + rh, rw, -rh) : new Rect(rx, ry, rw, rh);
            Gizmos.DrawGUITexture(rect, overlay);
        }

        // Cells around the debug cell, with a label.
        if (drawLocalCells)
        {
            Vector3 size = new Vector3(cs, cs, 0.01f) * 0.95f;
            for (int dx = -gizmoRadius; dx <= gizmoRadius; dx++)
            {
                for (int dy = -gizmoRadius; dy <= gizmoRadius; dy++)
                {
                    var p = new Vector3Int(debugCell.x + dx, debugCell.y + dy, 0);
                    TerrainType t = GetTerrainType(p);
                    if (t == null) continue;

                    Color c = t.debugColor; c.a = 0.45f;
                    Gizmos.color = c;
                    Gizmos.DrawCube(new Vector3((p.x + 0.5f) * cs, (p.y + 0.5f) * cs, 0f), size);
                }
            }

            Vector3 center = new Vector3((debugCell.x + 0.5f) * cs, (debugCell.y + 0.5f) * cs, 0f);
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(center, new Vector3(cs, cs, 0.01f));
            UnityEditor.Handles.Label(center + Vector3.up * cs, DescribeCell(debugCell));
        }
    }
#endif
    #endregion
}