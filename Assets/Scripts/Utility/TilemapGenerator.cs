using Server;
using System;
using System.Collections.Generic;
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

    private DomainManager dm => DomainManager.instance;

    [ContextMenu("Generate")]
    public async void Generate()
    {
        if (tilemap == null)
        {
            Debug.Log("No Tilemap, not generate");
            return;
        }
        if (!CanGenerate()) return;

        await GenerateAsync();
    }

    private async Task GenerateAsync()
    {
        Vector2Int size = GetTileCount();
        Vector2Int origin = new Vector2Int(-size.x / 2, -size.y / 2);

        int total = size.x * size.y;
        Vector3Int[] positions = new Vector3Int[total];
        TileBase[] tileArray = new TileBase[total];

        int index = 0;
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                positions[index] = new Vector3Int(origin.x + x, origin.y + y, 0);
                tileArray[index] = tiles[RNG.GetInt(0, tiles.Count)];
                index++;
            }
        }

        tilemap.ClearAllTiles();

        await Task.Delay(50);
        if (this == null || tilemap == null) return;
        tilemap.SetTiles(positions, tileArray);

        Debug.Log($"Tilemap generated: {size.x}x{size.y} tiles ({tiles.Count} tile types), seed: {dm?.Seed}.");

#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
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
}