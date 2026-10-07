using Server;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Paints the tilemap from an FBM noise map, 0-1 per cell, mapped to
/// ordered TerrainLevel bands. Caches the resulting level-per-cell grid so
/// other generators (NatureGenerator, etc.) can query "what terrain is at
/// this cell" without resampling noise themselves.
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
    #endregion

    #region Cache
    private DomainManager dm => DomainManager.instance;

    private int[,] levelGrid;
    private Vector2Int gridSize;
    private Vector2Int gridOrigin;
    private float noiseOffsetX, noiseOffsetY;

    public bool IsGenerated { get; private set; }
    public Vector2Int GridSize => gridSize;
    public Vector2Int GridOrigin => gridOrigin;
    public float CellWorldSize => cellWorldSize;
    #endregion

    #region Generate
    [ContextMenu("Generate")]
    public async Task Generate()
    {
        if (tilemap == null)
        {
            Debug.LogWarning("[TerrainGenerator] No Tilemap assigned.");
            return;
        }
        if (!CanGenerate()) return;
        await GenerateAsync();
    }

    private async Task GenerateAsync()
    {
        levels.Sort((a, b) => a.maxThreshold.CompareTo(b.maxThreshold));

        gridSize = GetTileCount();
        gridOrigin = new Vector2Int(-gridSize.x / 2, -gridSize.y / 2);

        // Seed-derived noise offset so each run's map layout differs with
        // the seed, without touching the global RNG state other systems rely on.
        uint seed = dm != null ? (uint)Mathf.Max(1, dm.Seed) : (uint)DateTime.Now.Ticks;
        var seededRandom = new Unity.Mathematics.Random(seed);
        noiseOffsetX = seededRandom.NextFloat(-10000f, 10000f);
        noiseOffsetY = seededRandom.NextFloat(-10000f, 10000f);

        int total = gridSize.x * gridSize.y;
        levelGrid = new int[gridSize.x, gridSize.y];

        Vector3Int[] positions = new Vector3Int[total];
        TileBase[] tileArray = new TileBase[total];

        int index = 0;
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                float n = SampleNoise(x, y);
                int levelIndex = GetLevelIndex(n);
                levelGrid[x, y] = levelIndex;

                TerrainLevel level = levels[levelIndex];
                positions[index] = new Vector3Int(gridOrigin.x + x, gridOrigin.y + y, 0);
                tileArray[index] = level.tiles[RNG.GetInt(0, level.tiles.Count)];
                index++;
            }
        }

        tilemap.ClearAllTiles();
        await Task.Delay(10);
        if (this == null || tilemap == null) return;

        tilemap.SetTiles(positions, tileArray);
        IsGenerated = true;

        Debug.Log($"[TerrainGenerator] Generated {gridSize.x}x{gridSize.y} tiles, {levels.Count} levels, seed: {dm?.Seed}.");

#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
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