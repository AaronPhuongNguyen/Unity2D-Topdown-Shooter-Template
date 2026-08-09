using Server;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapGenerator : MonoBehaviour
{
    [Header("Tiles")]
    [SerializeField] private Tilemap tilemap;
    [SerializeField] private List<TileBase> tiles = new List<TileBase>();

    [Header("Options")]
    [SerializeField] private bool useDomainMapSize = true;
    [SerializeField] private Vector2Int manualSize = new Vector2Int(20, 20);
    [SerializeField] private float cellWorldSize = 1f;

    [Header("Progressive Generation")]
    [SerializeField] private bool autoTuneBySize = true;

    [Tooltip("Used only if autoTuneBySize is false.")]
    [SerializeField, Range(0.01f, 1f)] private float priorityFraction = 0.1f;
    [Tooltip("Used only if autoTuneBySize is false.")]
    [SerializeField] private float tilesPerSecond = 10f;

    [Header("Auto-Tune Settings")]
    [Tooltip("Roughly how many tiles the instant priority chunk should contain, regardless of map size.")]
    [SerializeField] private int targetPriorityTileCount = 60;
    [Tooltip("Priority chunk will never exceed this fraction of the total map, even on small maps.")]
    [SerializeField, Range(0.01f, 1f)] private float priorityFractionCap = 0.5f;
    [Tooltip("Roughly how long the background fill should take to finish, regardless of map size.")]
    [SerializeField] private float targetFillSeconds = 15f;
    [Tooltip("Never go slower than this many tiles/sec, even on tiny maps.")]
    [SerializeField] private float minTilesPerSecond = 5f;
    [Tooltip("Never go faster than this many tiles/sec, to keep per-frame cost bounded on huge maps.")]
    [SerializeField] private float maxTilesPerSecond = 500f;
    [SerializeField] private int tilesPerBatch = 10;

    private DomainManager dm => DomainManager.instance;
    private CancellationTokenSource cts;

    [ContextMenu("Generate")]
    public async void Generate()
    {
        if (tilemap == null)
        {
            Debug.Log("No Tilemap, not generate");
            return;
        }
        if (!CanGenerate()) return;

        cts?.Cancel();
        cts = new CancellationTokenSource();

        await GenerateAsync(cts.Token);
    }

    private async Task GenerateAsync(CancellationToken token)
    {
        Vector2Int size = GetTileCount();
        Vector2Int origin = new Vector2Int(-size.x / 2, -size.y / 2);
        int total = size.x * size.y;

        (float resolvedPriorityFraction, float resolvedTilesPerSecond) = ResolveTuning(total);

        var allPositions = new List<Vector3Int>(total);
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                allPositions.Add(new Vector3Int(origin.x + x, origin.y + y, 0));
            }
        }
        allPositions.Sort((a, b) =>
        {
            float da = ((Vector2)(Vector2Int)a).sqrMagnitude;
            float db = ((Vector2)(Vector2Int)b).sqrMagnitude;
            return da.CompareTo(db);
        });

        tilemap.ClearAllTiles();
        if (token.IsCancellationRequested) return;

        // --- Priority pass ---
        int priorityCount = Mathf.Clamp(Mathf.RoundToInt(total * resolvedPriorityFraction), 1, total);
        var priorityPositions = new Vector3Int[priorityCount];
        var priorityTiles = new TileBase[priorityCount];
        for (int i = 0; i < priorityCount; i++)
        {
            priorityPositions[i] = allPositions[i];
            priorityTiles[i] = tiles[RNG.GetInt(0, tiles.Count)];
        }

        await Task.Delay(50, token).ContinueWith(_ => { });
        if (token.IsCancellationRequested || this == null || tilemap == null) return;

        tilemap.SetTiles(priorityPositions, priorityTiles);
        Debug.Log($"Priority chunk generated: {priorityCount}/{total} tiles " +
                  $"(fraction: {resolvedPriorityFraction:P1}, fill rate: {resolvedTilesPerSecond:F1}/s).");
#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif

        // --- Background pass ---
        int remaining = total - priorityCount;
        if (remaining <= 0) return;

        float delayPerBatch = tilesPerBatch / Mathf.Max(0.01f, resolvedTilesPerSecond);
        int idx = priorityCount;

        while (idx < total)
        {
            if (token.IsCancellationRequested || this == null || tilemap == null) return;

            int batchSize = Mathf.Min(tilesPerBatch, total - idx);
            var batchPositions = new Vector3Int[batchSize];
            var batchTiles = new TileBase[batchSize];
            for (int i = 0; i < batchSize; i++)
            {
                batchPositions[i] = allPositions[idx + i];
                batchTiles[i] = tiles[RNG.GetInt(0, tiles.Count)];
            }

            tilemap.SetTiles(batchPositions, batchTiles);
            idx += batchSize;

            try
            {
                await Task.Delay(Mathf.RoundToInt(delayPerBatch * 1000), token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        Debug.Log($"Tilemap fully generated: {size.x}x{size.y} tiles ({tiles.Count} tile types), seed: {dm?.Seed}.");
    }

    /// <summary>
    /// Computes priority fraction + fill speed from map size, or falls back to manual values.
    /// </summary>
    private (float priorityFraction, float tilesPerSecond) ResolveTuning(int total)
    {
        if (!autoTuneBySize)
            return (priorityFraction, tilesPerSecond);

        // Priority: aim for a fixed tile-count "safe zone", capped as a fraction so tiny maps don't overshoot 100%.
        float autoPriorityFraction = Mathf.Clamp(
            (float)targetPriorityTileCount / Mathf.Max(1, total),
            0.01f,
            priorityFractionCap);

        // Speed: aim to finish the remaining tiles in ~targetFillSeconds, clamped to sane bounds.
        int remainingEstimate = Mathf.Max(1, total - Mathf.RoundToInt(total * autoPriorityFraction));
        float autoTilesPerSecond = Mathf.Clamp(
            remainingEstimate / Mathf.Max(0.01f, targetFillSeconds),
            minTilesPerSecond,
            maxTilesPerSecond);

        return (autoPriorityFraction, autoTilesPerSecond);
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        cts?.Cancel();
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        tilemap.ClearAllTiles();
    }

    private bool CanGenerate()
    {
        if (tiles == null || tiles.Count == 0)
        {
            Debug.LogWarning("No tiles assigned — cannot generate.");
            return false;
        }
        if (useDomainMapSize && dm == null)
        {
            Debug.LogWarning("DomainManager instance not found, and useDomainMapSize is enabled.");
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

    private void OnDestroy()
    {
        cts?.Cancel();
    }
}