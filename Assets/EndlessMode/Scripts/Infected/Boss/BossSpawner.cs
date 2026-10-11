using Server;
using UnityEngine;

public class BossSpawner : MonoBehaviour
{
    #region Refs
    public HiveBrain hb => HiveBrain.instance;

    [Header("Database")]
    public ZomBossDataBase bossDatabase;

    [Header("Spawn Settings")]
    public float spawnRadius = 5f;

    [Tooltip("Fallback spawn origin if no Spawner reference is set.")]
    public Transform spawnOrigin;

    [Tooltip("Optional: reuse the normal Spawner for its position context.")]
    public Spawner spawner;
    #endregion

    #region Init
    private void OnEnable() => EventBus.OnBossWaveStarted += OnBossWave;
    private void OnDisable() => EventBus.OnBossWaveStarted -= OnBossWave;

    private void OnBossWave(int wave)
    {
        // One boss per boss wave. BossManager.CurrentBoss is shared and is
        // cleared right before this event fires, so even if two BossSpawners
        // exist in the scene (or the event fires twice) only the first spawns.
        var bm = BossManager.instance;
        if (bm != null && bm.CurrentBoss != null && bm.CurrentBoss.activeInHierarchy)
            return;

        SpawnRandomBoss(wave);
    }
    #endregion

    #region Public API
    public GameObject SpawnRandomBoss(int wave = -1)
    {
        if (bossDatabase == null)
        {
            Debug.LogWarning("BossSpawner: No bossDatabase assigned.");
            return null;
        }

        BossData boss = bossDatabase.GetRandomBoss(wave);
        if (boss == null)
        {
            Debug.LogWarning($"BossSpawner: No eligible boss for wave {wave}.");
            return null;
        }
        return SpawnBoss(boss);
    }

    public GameObject SpawnBossByID(int id)
    {
        if (bossDatabase == null) return null;

        BossData boss = bossDatabase.GetBossByID(id);
        if (boss == null)
        {
            Debug.LogWarning($"BossSpawner: No valid boss with ID {id} in database.");
            return null;
        }
        return SpawnBoss(boss);
    }
    #endregion

    #region Core
    private GameObject SpawnBoss(BossData boss)
    {
        if (boss == null || !boss.IsValid)
        {
            Debug.LogWarning("BossSpawner: Boss is missing prefab or ZomPackage.");
            return null;
        }

        if (hb == null)
        {
            Debug.LogError("BossSpawner: HiveBrain.instance is null - cannot spawn.");
            return null;
        }

        // Boss package is passed through so Zombrain.SpawnObject succeeds,
        // the boss is tracked by HiveBrain and counted in RemainingEnemy.
        GameObject o = hb.SpawnZom(boss.prefab, boss.package);
        if (o == null)
        {
            Debug.LogWarning($"BossSpawner: HiveBrain returned null for boss {boss.ID}.");
            return null;
        }

        o.transform.position = GetSpawnOrigin() + GetRandomOffset(spawnRadius);

        // DEBUG: remove once the duplicate-boss issue is found.
        Debug.Log($"[BossSpawner:{GetInstanceID()}] spawned boss '{o.name}' (id {o.GetInstanceID()}) via {boss.prefab.name}", o);

        if (BossManager.instance != null)
            BossManager.instance.RegisterBoss(o);

        return o;
    }
    #endregion

    #region Helpers
    private Vector2 GetSpawnOrigin()
    {
        if (spawnOrigin != null) return spawnOrigin.position;
        if (spawner != null) return spawner.transform.position;
        if (DomainManager.instance != null) return DomainManager.instance.mapCenter;
        return Vector2.zero;
    }

    private static Vector2 GetRandomOffset(float radius)
    {
        float angle = RNG.GetFloat(0f, Mathf.PI * 2f);
        float dist = Mathf.Sqrt(RNG.GetFloat(0f, 1f)) * radius;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
    }
    #endregion

    #region Debugger
    [ContextMenu("Test Spawn Random Boss")]
    private void Test_SpawnRandomBoss()
    {
        int wave = DomainManager.instance != null ? DomainManager.instance.CurrentWave : -1;
        SpawnRandomBoss(wave);
    }
    #endregion
}