using System.Collections.Generic;
using UnityEngine;

public class HiveBrain : MonoBehaviour
{
    public static HiveBrain instance { get; private set; }
    public LayerMask enemyMask;

    [SerializeField] private List<Zombrain> zoms = new List<Zombrain>();

    // How many zombies get their Tick() called per Update() frame, in
    // round-robin order. Set to 0 (or >= zoms.Count) to tick everyone every
    // frame, same as before. Raising this on lower-end mobile targets is
    // usually the single biggest lag fix once zombie counts get high -
    // players don't perceive individual zombie AI updating a frame or two
    // "late", but they very much perceive frame drops.
    [Header("Perf")]
    [SerializeField] private int ticksPerFrame = 0; // 0 = tick everyone (old behavior)
    private int tickCursor;

    #region Spatial Grid
    // Cell -> list of zombies currently in that cell. Lists are reused
    // across frames (cleared, not discarded) so RebuildGrid() doesn't
    // generate garbage every frame - that per-frame List<> allocation was
    // the main source of GC-driven stutter on mobile.
    private Dictionary<Vector2Int, List<Zombrain>> spatialGrid = new();

    // Pool of empty lists ready to be reused for newly-occupied cells,
    // so cells that go from populated -> empty -> populated again don't
    // need a fresh allocation either.
    private readonly Stack<List<Zombrain>> _listPool = new();

    private const float cellSize = 1.5f;

    private Vector2Int GetCell(Vector2 pos) => new Vector2Int(
        Mathf.FloorToInt(pos.x / cellSize),
        Mathf.FloorToInt(pos.y / cellSize)
    );

    /// <summary>Rebuilds the spatial grid used for cheap neighbor lookups. Call once per tick pass.</summary>
    public void RebuildGrid()
    {
        // Instead of spatialGrid.Clear() (which drops all the List<>
        // instances and forces new ones below), return each cell's list to
        // a pool and clear the dictionary's entries only.
        foreach (var kvp in spatialGrid)
        {
            kvp.Value.Clear();
            _listPool.Push(kvp.Value);
        }
        spatialGrid.Clear();

        for (int i = 0; i < zoms.Count; i++)
        {
            var z = zoms[i];
            if (z == null) continue;

            var cell = GetCell(z.transform.position);
            if (!spatialGrid.TryGetValue(cell, out var list))
            {
                list = _listPool.Count > 0 ? _listPool.Pop() : new List<Zombrain>(8);
                spatialGrid[cell] = list;
            }
            list.Add(z);
        }
    }

    private static readonly List<Zombrain> _neighborBuffer = new List<Zombrain>(32);
    public List<Zombrain> GetNearbyZoms(Zombrain self, float radius)
    {
        _neighborBuffer.Clear();
        Vector2Int center = GetCell(self.transform.position);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (spatialGrid.TryGetValue(center + new Vector2Int(dx, dy), out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] != self) _neighborBuffer.Add(list[i]);
                    }
                }
            }
        return _neighborBuffer;
    }
    #endregion

    #region Singleton
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            // Destroy() is deferred to end-of-frame; disable first so this
            // duplicate's OnEnable() never runs and double-subscribes.
            enabled = false;
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void OnEnable() => EventBus.OnGameRestart += Reboot;
    private void OnDisable() => EventBus.OnGameRestart -= Reboot;
    #endregion

    private void Update()
    {
        if (zoms.Count == 0) return;

        RebuildGrid(); // refresh spatial buckets before ticking this frame

        // Clean up dead references first, separately from ticking, so the
        // round-robin cursor below always walks a clean list.
        for (int i = zoms.Count - 1; i >= 0; i--)
        {
            if (zoms[i] == null) zoms.RemoveAt(i);
        }
        if (zoms.Count == 0) return;

        float dt = Time.deltaTime;

        if (ticksPerFrame <= 0 || ticksPerFrame >= zoms.Count)
        {
            // Old behavior: tick everyone every frame.
            for (int i = 0; i < zoms.Count; i++)
                zoms[i].Tick(dt);
        }
        else
        {
            // Round-robin: only tick a slice of zombies this frame, picking
            // up where we left off last frame. Over a few frames every
            // zombie still gets ticked at roughly the same effective rate,
            // but no single frame pays the full cost of all of them.
            int count = Mathf.Min(ticksPerFrame, zoms.Count);
            for (int n = 0; n < count; n++)
            {
                tickCursor %= zoms.Count;
                zoms[tickCursor].Tick(dt);
                tickCursor++;
            }
        }
    }

    #region Zom Management
    public GameObject SpawnZom(GameObject prefab, ZomPackage zp)
    {
        if (prefab == null) return null;

        GameObject obj = PoolingSystem.instance.GetFromPool(prefab);
        if (obj == null) return null;

        if (!obj.TryGetComponent(out Zombrain o))
        {
            Debug.LogWarning($"Pooled object {obj.name} has no Zombrain component!");
            return obj;
        }

        // SpawnObject is what actually increments dm.RemainingEnemy and
        // subscribes the death handler - only track it in `zoms` if it
        // actually succeeded, otherwise it'd get ticked and eventually
        // despawned without ever having been counted, driving
        // RemainingEnemy negative.
        if (!o.SpawnObject(zp)) return obj;

        if (!zoms.Contains(o)) zoms.Add(o);
        return obj;
    }

    public void DespawnZom(GameObject obj)
    {
        if (obj == null) return;
        if (!obj.TryGetComponent(out Zombrain o)) return;
        zoms.Remove(o);
        PoolingSystem.instance.RemoveToPool(o.gameObject);
    }
    private void Reboot()
    {
        Debug.Log($"[HiveBrain] Reboot() called. Clearing {zoms.Count} tracked zoms.");
        for (int i = zoms.Count - 1; i >= 0; i--)
        {
            if (zoms[i] == null) continue;
            zoms[i].Reboot();
            PoolingSystem.instance.RemoveToPool(zoms[i].gameObject);
        }
        zoms.Clear();

        // Return grid lists to the pool instead of dropping them so the
        // next wave's RebuildGrid() doesn't need to reallocate from scratch.
        foreach (var kvp in spatialGrid)
        {
            kvp.Value.Clear();
            _listPool.Push(kvp.Value);
        }
        spatialGrid.Clear();
    }
    #endregion
}