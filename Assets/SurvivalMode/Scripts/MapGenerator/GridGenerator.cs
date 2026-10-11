using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public enum GridLayer
{
    Ground = 0,     // dirt, mud, water
    Floor = 1,      // wooden floors, bridges
    Structure = 2,  // walls, doors, buildings
    Entity = 3      // player, monsters, NPCs
}

/// <summary>Behaviour of one ground type. The ground ID is the index in the Ground Types list.</summary>
[Serializable]
public class GroundRule
{
    public string name = "Dirt";
    [Tooltip("Ground itself blocks movement (until a Floor is placed over it).")]
    public bool blocksPath = false;
    public bool blocksView = false;
    public bool allowFloor = true;
    public bool allowStructure = true;
    [Tooltip("Structures can only be built after a Floor exists (e.g. water needs a bridge).")]
    public bool structureNeedsFloor = false;
}

/// <summary>Status of ONE layer of ONE cell. Four booleans packed into a byte.</summary>
[Serializable]
public struct LayerState
{
    public byte Flags;
    private const byte Occupied = 1, Place = 2, Path = 4, View = 8;

    /// <summary>Layer is already taken. Strictly blocks placing anything new here.</summary>
    public bool IsOccupied { get => (Flags & Occupied) != 0; set => Set(Occupied, value); }
    /// <summary>Terrain requirement is met, so something may be placed on this layer.</summary>
    public bool CanPlace { get => (Flags & Place) != 0; set => Set(Place, value); }
    /// <summary>Blocks movement / AI navigation.</summary>
    public bool IsBlockingPath { get => (Flags & Path) != 0; set => Set(Path, value); }
    /// <summary>Blocks light and line of sight.</summary>
    public bool IsBlockingView { get => (Flags & View) != 0; set => Set(View, value); }

    private void Set(byte bit, bool on)
    {
        if (on) Flags |= bit; else Flags &= (byte)~bit;
    }
}

/// <summary>One grid cell. Each layer has its own LayerState.</summary>
[Serializable]
public struct GridCell
{
    public Vector3Int Position;
    public ushort GroundID;   // index into GridGenerator's Ground Types
    public LayerState Ground, Floor, Structure, Entity;

    public LayerState Get(GridLayer layer)
    {
        switch (layer)
        {
            case GridLayer.Ground: return Ground;
            case GridLayer.Floor: return Floor;
            case GridLayer.Structure: return Structure;
            default: return Entity;
        }
    }

    public void Set(GridLayer layer, LayerState state)
    {
        switch (layer)
        {
            case GridLayer.Ground: Ground = state; break;
            case GridLayer.Floor: Floor = state; break;
            case GridLayer.Structure: Structure = state; break;
            default: Entity = state; break;
        }
    }
}

/// <summary>
/// Standalone multi-layer spatial grid (Dictionary based, no physics, no Tilemap).
/// Size comes from WorldManager.data.WorldScript.WorldSize, centered on (0,0).
/// Access anywhere with GridGenerator.Instance. Progress goes to LoadingHandle as fixed steps.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-4)]
public class GridGenerator : MonoBehaviour
{
    #region Singleton
    public static GridGenerator Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
    #endregion

    #region Inspector
    [Header("Size")]
    [Tooltip("Used only when there is no WorldManager / WorldScript.")]
    [SerializeField] private Vector2Int fallbackSize = new Vector2Int(900, 900);
    [SerializeField, Min(0.01f)] private float cellWorldSize = 1f;

    [Header("Start")]
    [Tooltip("Generate automatically in Start. Turn off if another script calls GenerateRoutine().")]
    [SerializeField] private bool generateOnStart = true;

    [Header("Ground types (ID = list index; new cells use Default Ground ID)")]
    [SerializeField]
    private List<GroundRule> groundTypes = new List<GroundRule>
    {
        new GroundRule { name = "Dirt" },
        new GroundRule { name = "Water", blocksPath = true, structureNeedsFloor = true },
    };
    [SerializeField, Min(0)] private int defaultGroundID = 0;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawMapBounds = true;
    [Tooltip("Cells within this radius of the debug cell are drawn. Keep it small.")]
    [SerializeField, Range(1, 40)] private int gizmoRadius = 12;
    [Tooltip("Optional: the debug cell follows this object (e.g. the player).")]
    [SerializeField] private Transform debugFollow;
    [SerializeField] private Vector3Int debugCell;
    [TextArea(4, 8)]
    [SerializeField] private string debugInfo;

    private const int loadingSteps = 1000;
    #endregion

    #region Data
    private struct RuleData
    {
        public bool blocksPath, blocksView, allowFloor, allowStructure, structureNeedsFloor;
    }

    private Dictionary<Vector3Int, GridCell> cells = new Dictionary<Vector3Int, GridCell>();
    private readonly Dictionary<Vector3Int, GameObject>[] objects = new Dictionary<Vector3Int, GameObject>[4];
    private RuleData[] rules;

    private Vector2Int gridSize;
    private Vector2Int gridOrigin;      // lowest cell coordinate (min corner)

    private bool isGenerating;
    private volatile bool cancelRequested;
    private int columnsDone;
    private int pendingLoadingSteps;

    public bool IsGenerated { get; private set; }
    public int CellCount => cells.Count;
    public Vector2Int GridSize => gridSize;
    /// <summary>Lowest cell coordinate. For 900: (-450,-450).</summary>
    public Vector2Int MinCell => gridOrigin;
    /// <summary>Highest cell coordinate. For 900: (449,449).</summary>
    public Vector2Int MaxCell => gridOrigin + gridSize - Vector2Int.one;
    public float CellWorldSize => cellWorldSize;

    /// <summary>Fired after any placement or removal.</summary>
    public event Action<Vector3Int, GridLayer> OnCellChanged;

    /// <summary>Read-only access to the dictionary.</summary>
    public IReadOnlyDictionary<Vector3Int, GridCell> Cells => cells;
    #endregion

    #region Loading Steps
    private void RegisterLoadingSteps(int count)
    {
        if (count <= 0 || LoadingHandle.Instance == null) return;
        LoadingHandle.Instance.AddTotalSteps(count, "Grid");
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
    #endregion

    #region Generate
    private void Start()
    {
        if (generateOnStart) Generate();
    }

    /// <summary>Fire-and-forget. To wait for it: yield return grid.GenerateRoutine();</summary>
    [ContextMenu("Generate")]
    public void Generate()
    {
        if (isGenerating) return;
        StartCoroutine(GenerateRoutine());
    }

    /// <summary>Size from WorldScript, or the fallback size.</summary>
    private Vector2Int ReadSize()
    {
        WorldManager wm = WorldManager.instance;
        if (wm != null && wm.data != null && wm.data.WorldScript != null)
        {
            int s = (int)Math.Min(wm.data.WorldScript.WorldSize, 20000u);
            return new Vector2Int(Mathf.Max(1, s), Mathf.Max(1, s));
        }

        Debug.LogWarning("[GridGenerator] No WorldScript found, using fallback size.", this);
        return new Vector2Int(Mathf.Max(1, fallbackSize.x), Mathf.Max(1, fallbackSize.y));
    }

    public IEnumerator GenerateRoutine()
    {
        if (isGenerating) yield break;

        isGenerating = true;
        cancelRequested = false;
        IsGenerated = false;

        gridSize = ReadSize();
        // Centered on (0,0): 900 -> -450 .. 449
        gridOrigin = new Vector2Int(-gridSize.x / 2, -gridSize.y / 2);

        BuildRules();
        for (int i = 0; i < objects.Length; i++) objects[i] = new Dictionary<Vector3Int, GameObject>();

        int w = gridSize.x;
        int h = gridSize.y;
        cells = new Dictionary<Vector3Int, GridCell>(w * h);

        int steps = Mathf.Max(1, loadingSteps);
        RegisterLoadingSteps(steps);
        int reported = 0;

        columnsDone = 0;
        int groundID = Mathf.Clamp(defaultGroundID, 0, rules.Length - 1);
        Task build = Task.Run(() => BuildCells(groundID));

        while (!build.IsCompleted)
        {
            float f = Volatile.Read(ref columnsDone) / (float)w;
            reported = ReportProgress((int)(f * 1000f), 1000, steps, reported);
            yield return null;
        }

        if (build.IsFaulted)
        {
            Debug.LogException(build.Exception);
            Abort();
            yield break;
        }
        if (cancelRequested) { Abort(); yield break; }

        FlushLoadingSteps();
        IsGenerated = true;
        isGenerating = false;
        RefreshDebugInfo();
        Debug.Log($"[GridGenerator] Built {cells.Count} cells ({w}x{h}), x {MinCell.x}..{MaxCell.x}, y {MinCell.y}..{MaxCell.y}.");
    }

    private void Abort()
    {
        FlushLoadingSteps();
        isGenerating = false;
    }

    private void BuildRules()
    {
        if (groundTypes == null || groundTypes.Count == 0)
            groundTypes = new List<GroundRule> { new GroundRule() };

        rules = new RuleData[groundTypes.Count];
        for (int i = 0; i < rules.Length; i++)
        {
            GroundRule g = groundTypes[i] ?? new GroundRule();
            rules[i] = new RuleData
            {
                blocksPath = g.blocksPath,
                blocksView = g.blocksView,
                allowFloor = g.allowFloor,
                allowStructure = g.allowStructure,
                structureNeedsFloor = g.structureNeedsFloor
            };
        }
    }

    /// <summary>Runs on a thread-pool thread. Plain data only.</summary>
    private void BuildCells(int groundID)
    {
        int w = gridSize.x;
        int h = gridSize.y;

        for (int x = 0; x < w; x++)
        {
            if (cancelRequested) return;

            for (int y = 0; y < h; y++)
            {
                Vector3Int pos = new Vector3Int(gridOrigin.x + x, gridOrigin.y + y, 0);
                var cell = new GridCell { Position = pos };
                ApplyGround(ref cell, groundID);
                cells.Add(pos, cell);
            }
            Interlocked.Increment(ref columnsDone);
        }
    }

    /// <summary>Sets the ground layer of a cell from a ground type, then recalculates CanPlace.</summary>
    private void ApplyGround(ref GridCell cell, int groundID)
    {
        groundID = Mathf.Clamp(groundID, 0, rules.Length - 1);
        RuleData r = rules[groundID];

        cell.GroundID = (ushort)groundID;
        cell.Ground = new LayerState();
        cell.Ground.IsOccupied = true;          // ground always exists, nothing is placed ON it
        cell.Ground.IsBlockingPath = r.blocksPath;
        cell.Ground.IsBlockingView = r.blocksView;
        cell.Ground.CanPlace = false;

        RecalculateCanPlace(ref cell);
    }
    public bool IsGenerating => isGenerating;
    public int GroundTypeCount => groundTypes != null ? groundTypes.Count : 0;

    /// <summary>Raised after ApplyGroundMapRoutine finishes.</summary>
    public event Action OnGroundMapApplied;

    /// <summary>
    /// Bulk-sets the ground type of every cell. map index = x * height + y,
    /// where x/y are relative to MinCell. Runs in a per-frame time budget.
    /// </summary>
    public IEnumerator ApplyGroundMapRoutine(ushort[] map, Action<float> progress = null)
    {
        if (!IsGenerated || map == null || map.Length != gridSize.x * gridSize.y)
        {
            Debug.LogWarning("[GridGenerator] ApplyGroundMap: grid not generated or map size mismatch.", this);
            yield break;
        }

        int w = gridSize.x, h = gridSize.y;
        long budget = (long)(100.0 * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                Vector3Int pos = new Vector3Int(gridOrigin.x + x, gridOrigin.y + y, 0);
                if (!cells.TryGetValue(pos, out GridCell c)) continue;
                ApplyGround(ref c, map[x * h + y]);
                cells[pos] = c;
            }

            if (sw.ElapsedTicks >= budget)
            {
                progress?.Invoke((x + 1) / (float)w);
                yield return null;
                sw.Restart();
            }
        }

        progress?.Invoke(1f);
        RefreshDebugInfo();
        OnGroundMapApplied?.Invoke();
    }
    #endregion

    #region Rules (canPlace / path / view)
    private void RecalculateCanPlace(ref GridCell c)
    {
        RuleData rule = rules[c.GroundID];

        c.Floor.CanPlace = rule.allowFloor;
        c.Structure.CanPlace = rule.allowStructure && (!rule.structureNeedsFloor || c.Floor.IsOccupied);
        c.Entity.CanPlace = !IsPathBlockedWithoutEntity(c);
    }

    private static bool IsPathBlockedWithoutEntity(in GridCell c)
    {
        // Raw ground (water) only blocks while no Floor/bridge covers it.
        if (c.Ground.IsBlockingPath && !c.Floor.IsOccupied) return true;
        if (c.Floor.IsOccupied && c.Floor.IsBlockingPath) return true;
        if (c.Structure.IsOccupied && c.Structure.IsBlockingPath) return true;
        return false;
    }
    #endregion

    #region Query API (use from anywhere: GridGenerator.Instance....)
    public bool TryGetCell(Vector3Int pos, out GridCell cell) => cells.TryGetValue(pos, out cell);

    public bool HasCell(Vector3Int pos) => cells.ContainsKey(pos);

    /// <summary>Layer status of a cell. A missing cell returns a default (all false) state.</summary>
    public LayerState GetLayer(Vector3Int pos, GridLayer layer)
        => cells.TryGetValue(pos, out GridCell c) ? c.Get(layer) : default;

    public bool IsOccupied(Vector3Int pos, GridLayer layer) => GetLayer(pos, layer).IsOccupied;

    /// <summary>Terrain requirement only (does not check occupation).</summary>
    public bool CanPlace(Vector3Int pos, GridLayer layer) => GetLayer(pos, layer).CanPlace;

    /// <summary>Free AND allowed by terrain. This is what TryPlace checks.</summary>
    public bool IsFreeToPlace(Vector3Int pos, GridLayer layer)
    {
        LayerState s = GetLayer(pos, layer);
        return layer != GridLayer.Ground && !s.IsOccupied && s.CanPlace;
    }

    /// <summary>Any layer blocks movement here? Outside the map counts as blocked.</summary>
    public bool IsBlockingPath(Vector3Int pos)
    {
        if (!cells.TryGetValue(pos, out GridCell c)) return true;
        return IsPathBlockedWithoutEntity(c) || (c.Entity.IsOccupied && c.Entity.IsBlockingPath);
    }

    /// <summary>Any layer blocks sight or light here? Outside the map does not.</summary>
    public bool IsBlockingView(Vector3Int pos)
    {
        if (!cells.TryGetValue(pos, out GridCell c)) return false;
        return (c.Ground.IsBlockingView && !c.Floor.IsOccupied)
            || (c.Floor.IsOccupied && c.Floor.IsBlockingView)
            || (c.Structure.IsOccupied && c.Structure.IsBlockingView)
            || (c.Entity.IsOccupied && c.Entity.IsBlockingView);
    }

    public int GetGroundID(Vector3Int pos)
        => cells.TryGetValue(pos, out GridCell c) ? c.GroundID : -1;

    public GameObject GetObject(Vector3Int pos, GridLayer layer)
    {
        if (!IsGenerated) return null;
        return objects[(int)layer].TryGetValue(pos, out GameObject go) ? go : null;
    }

    /// <summary>Snapshot copy of all cells.</summary>
    public List<GridCell> GetCellList() => new List<GridCell>(cells.Values);

    /// <summary>Is the coordinate inside the map?</summary>
    public bool InBounds(Vector3Int pos)
        => pos.x >= MinCell.x && pos.x <= MaxCell.x && pos.y >= MinCell.y && pos.y <= MaxCell.y;
    #endregion

    #region Place / Remove / Ground
    /// <summary>
    /// Fails if the cell is missing, the layer is occupied, or the terrain requirement is not met.
    /// To replace, Remove() the old one first.
    /// </summary>
    public bool TryPlace(Vector3Int pos, GridLayer layer, bool blocksPath, bool blocksView, GameObject obj = null)
    {
        if (!IsGenerated || layer == GridLayer.Ground) return false;
        if (!cells.TryGetValue(pos, out GridCell c)) return false;

        LayerState s = c.Get(layer);
        if (s.IsOccupied || !s.CanPlace) return false;

        s.IsOccupied = true;
        s.IsBlockingPath = blocksPath;
        s.IsBlockingView = blocksView;
        c.Set(layer, s);

        RecalculateCanPlace(ref c);
        cells[pos] = c;

        if (obj != null) objects[(int)layer][pos] = obj;
        OnCellChanged?.Invoke(pos, layer);
        return true;
    }

    /// <summary>Frees a layer. A Floor can't be removed while a Structure depends on it.</summary>
    public bool Remove(Vector3Int pos, GridLayer layer, bool destroyObject = false)
    {
        if (!IsGenerated || layer == GridLayer.Ground) return false;
        if (!cells.TryGetValue(pos, out GridCell c)) return false;
        if (!c.Get(layer).IsOccupied) return false;

        if (layer == GridLayer.Floor && c.Structure.IsOccupied && rules[c.GroundID].structureNeedsFloor)
            return false;

        c.Set(layer, new LayerState());
        RecalculateCanPlace(ref c);
        cells[pos] = c;

        if (objects[(int)layer].TryGetValue(pos, out GameObject go))
        {
            objects[(int)layer].Remove(pos);
            if (destroyObject && go != null) Destroy(go);
        }

        OnCellChanged?.Invoke(pos, layer);
        return true;
    }

    /// <summary>For the terrain step later: sets the ground type of one cell (ID = index in Ground Types).</summary>
    public bool SetGround(Vector3Int pos, int groundID)
    {
        if (!IsGenerated || !cells.TryGetValue(pos, out GridCell c)) return false;
        ApplyGround(ref c, groundID);
        cells[pos] = c;
        OnCellChanged?.Invoke(pos, GridLayer.Ground);
        return true;
    }
    #endregion

    #region Coordinates (cell 0,0 covers world 0..cellSize; no Tilemap needed)
    public Vector3Int WorldToCell(Vector3 world)
        => new Vector3Int(Mathf.FloorToInt(world.x / cellWorldSize), Mathf.FloorToInt(world.y / cellWorldSize), 0);

    /// <summary>Center of the cell in world space.</summary>
    public Vector3 CellToWorld(Vector3Int cell)
        => new Vector3((cell.x + 0.5f) * cellWorldSize, (cell.y + 0.5f) * cellWorldSize, 0f);
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
        cells.Clear();
        for (int i = 0; i < objects.Length; i++) objects[i]?.Clear();
    }
    #endregion

    #region Debug
    /// <summary>Text report of one cell.</summary>
    public string DescribeCell(Vector3Int pos)
    {
        if (!IsGenerated) return "Grid not generated yet.";
        if (!cells.TryGetValue(pos, out GridCell c)) return $"Cell {pos.x},{pos.y}: outside the map.";

        string g = c.GroundID < groundTypes.Count ? groundTypes[c.GroundID].name : "?";
        return $"Cell {pos.x},{pos.y}  Ground: {g} (ID {c.GroundID})\n" +
               $"  Path blocked: {IsBlockingPath(pos)}   View blocked: {IsBlockingView(pos)}\n" +
               Line("Ground", c.Ground) + Line("Floor", c.Floor) +
               Line("Structure", c.Structure) + Line("Entity", c.Entity);
    }

    private static string Line(string name, LayerState s)
        => $"  {name,-9} occupied:{(s.IsOccupied ? 1 : 0)} canPlace:{(s.CanPlace ? 1 : 0)} " +
           $"path:{(s.IsBlockingPath ? 1 : 0)} view:{(s.IsBlockingView ? 1 : 0)}\n";

    [ContextMenu("Log Debug Cell")]
    private void LogDebugCell() => Debug.Log(DescribeCell(debugCell), this);

    private void RefreshDebugInfo() => debugInfo = DescribeCell(debugCell);

    private void Update()
    {
#if UNITY_EDITOR
        if (!IsGenerated) return;
        if (debugFollow != null) debugCell = WorldToCell(debugFollow.position);
        RefreshDebugInfo();
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying && IsGenerated) RefreshDebugInfo();
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Map outline (works even before generating, from the fallback size).
        if (drawMapBounds)
        {
            Vector2Int size = IsGenerated ? gridSize : fallbackSize;
            Vector2Int min = IsGenerated ? gridOrigin : new Vector2Int(-size.x / 2, -size.y / 2);
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Vector3 center = new Vector3((min.x + size.x * 0.5f) * cellWorldSize, (min.y + size.y * 0.5f) * cellWorldSize, 0f);
            Gizmos.DrawWireCube(center, new Vector3(size.x * cellWorldSize, size.y * cellWorldSize, 0.01f));
        }

        if (!Application.isPlaying || !IsGenerated) return;

        Vector3 cs = new Vector3(cellWorldSize, cellWorldSize, 0.01f);

        for (int dx = -gizmoRadius; dx <= gizmoRadius; dx++)
        {
            for (int dy = -gizmoRadius; dy <= gizmoRadius; dy++)
            {
                Vector3Int p = new Vector3Int(debugCell.x + dx, debugCell.y + dy, 0);
                if (!cells.TryGetValue(p, out GridCell c)) continue;

                Color col;
                if (c.Structure.IsOccupied) col = new Color(1f, 0.85f, 0.1f, 0.55f);        // yellow: structure
                else if (c.Floor.IsOccupied) col = new Color(0.1f, 0.9f, 0.9f, 0.45f);      // cyan: floor
                else if (IsPathBlockedWithoutEntity(c)) col = new Color(1f, 0.2f, 0.2f, 0.45f); // red: blocked
                else col = new Color(0.2f, 1f, 0.3f, 0.12f);                                // green: free
                if (c.Entity.IsOccupied) col = new Color(0.8f, 0.3f, 1f, 0.6f);             // purple: entity

                Gizmos.color = col;
                Gizmos.DrawCube(CellToWorld(p), cs * 0.95f);
            }
        }

        // The inspected cell, with a label.
        Gizmos.color = Color.white;
        Vector3 dc = CellToWorld(debugCell);
        Gizmos.DrawWireCube(dc, cs);
        UnityEditor.Handles.Label(dc + Vector3.up * cellWorldSize, DescribeCell(debugCell));
    }
#endif
    #endregion
}