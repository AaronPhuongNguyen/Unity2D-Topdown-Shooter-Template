using System.Collections.Generic;
using UnityEngine;

public class HiveBrain : MonoBehaviour
{
    public static HiveBrain instance { get; private set; }
    public LayerMask enemyMask;

    [SerializeField] private List<Zombrain> zoms = new List<Zombrain>();

    #region Spatial Grid
    private Dictionary<Vector2Int, List<Zombrain>> spatialGrid = new();
    private const float cellSize = 1.5f;

    private Vector2Int GetCell(Vector2 pos) => new Vector2Int(
        Mathf.FloorToInt(pos.x / cellSize),
        Mathf.FloorToInt(pos.y / cellSize)
    );

    /// <summary>Rebuilds the spatial grid used for cheap neighbor lookups. Call once per tick pass.</summary>
    public void RebuildGrid()
    {
        spatialGrid.Clear();
        foreach (var z in zoms)
        {
            if (z == null) continue;
            var cell = GetCell(z.transform.position);
            if (!spatialGrid.TryGetValue(cell, out var list))
                spatialGrid[cell] = list = new List<Zombrain>();
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

        for (int i = zoms.Count - 1; i >= 0; i--)
        {
            if (zoms[i] == null)
            {
                zoms.RemoveAt(i); // clean up dead references instead of just skipping them
                continue;
            }
            zoms[i].Tick(Time.deltaTime);
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
        spatialGrid.Clear();
    }
    #endregion
}