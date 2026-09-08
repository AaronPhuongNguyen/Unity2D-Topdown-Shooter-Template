using Server;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class Spawner : MonoBehaviour
{
    public DomainManager dm => DomainManager.instance;
    public HiveBrain hb => HiveBrain.instance;
    public PoolingSystem pool => PoolingSystem.instance;

    public ZomDataBase dataBase;
    public float SpawnRadius = 5f;

    protected ZomDataBase _dtb;

    protected bool IsActive => dataBase != null;

    private uint seedCounter = 1;

    private void OnEnable()
    {
        if (!IsActive) return;
        CloneDataBase(dataBase);
        EventBus.OnNewWave += SpawnPerWave;
    }

    private void OnDisable()
    {
        EventBus.OnNewWave -= SpawnPerWave;
    }

    #region Main Functions
    public void SpawnPerWave(int wave)
    {
        if (_dtb == null || _dtb.packages.Count == 0 || !CanSpawn(_dtb)) return;

        List<ZomPackage> eligible = GetEligiblePackages(wave);
        if (eligible.Count == 0) return;

        List<ZomPackage> selected = GetRandomPackages(eligible);

        foreach (ZomPackage package in selected)
        {
            int spawnCount = GetSpawnCount(package);
            Spawn(package, transform.position, spawnCount, isRandom: true, SpawnRadius);
        }
    }

    private List<ZomPackage> GetEligiblePackages(int wave)
    {
        List<ZomPackage> result = new List<ZomPackage>(_dtb.packages.Count);

        foreach (ZomPackage package in _dtb.packages)
        {
            if (package == null) continue;
            if (wave < package.AppearFromWave) continue;

            result.Add(package);
        }

        return result;
    }

    private int GetSpawnCount(ZomPackage package)
    {
        float difficultyMult = Mathf.Max(1f, dm.CurrentDifficulty);
        float raw = package.CountPerSpawner * difficultyMult;

        // small random spread so spawns aren't perfectly deterministic
        float jittered = RNG.GetFloat(raw * 0.25f, raw * 4f);

        return Mathf.Max(1, Mathf.RoundToInt(jittered));
    }
    private List<ZomPackage> GetRandomPackages(List<ZomPackage> eligible)
    {
        List<ZomPackage> selected = new List<ZomPackage>();
        int count = eligible.Count;
        if (count == 0) return selected;

        int kindCount = RNG.GetInt(1, count + 1);

        ZomPackage[] shuffled = eligible.ToArray();
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = RNG.GetInt(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        for (int i = 0; i < kindCount; i++)
        {
            ZomPackage candidate = shuffled[i];
            if (RNG.GetPercent() <= candidate._rateToAppear)
                selected.Add(candidate);
        }

        return selected;
    }

    public GameObject Spawn(ZomPackage package, Vector2 pos)
    {
        if (dm.RemainingEnemy >= dm.MaxEnemyPerWave) return null;
        if (package == null || package.prefab == null) return null;

        GameObject o = hb.SpawnZom(package.prefab, package);
        if (o == null) return null;

        o.transform.position = pos;
        return o;
    }

    public List<GameObject> Spawn(ZomPackage package, Vector2 pos, int count)
    {
        if (dm.RemainingEnemy >= dm.MaxEnemyPerWave) return null;
        List<GameObject> results = new List<GameObject>(Mathf.Max(0, count));
        if (count <= 0) return results;

        for (int i = 0; i < count; i++)
        {
            GameObject o = Spawn(package, pos);
            if (o != null) results.Add(o);
        }
        return results;
    }

    public List<GameObject> Spawn(ZomPackage package, Vector2 pos, int count, bool isRandom, float radius = 2f)
    {
        if (dm.RemainingEnemy >= dm.MaxEnemyPerWave) return null;
        if (!isRandom) return Spawn(package, pos, count);
        if (count <= 0) return new List<GameObject>();

        NativeArray<float2> offsets = new NativeArray<float2>(count, Allocator.TempJob);

        var job = new SpawnOffsetJob
        {
            Radius = radius,
            Seed = NextSeed(),
            Offsets = offsets
        };

        job.Schedule(count, 32).Complete();

        List<GameObject> results = new List<GameObject>(count);
        for (int i = 0; i < count; i++)
        {
            Vector2 spawnPos = pos + (Vector2)offsets[i];
            GameObject o = Spawn(package, spawnPos);
            if (o != null) results.Add(o);
        }

        offsets.Dispose();
        return results;
    }
    #endregion

    #region Debugger
    [ContextMenu("Test Spawn")]
    public void Test_SpawnZom()
    {
        if (!CanSpawn(_dtb)) return;

        SpawnPerWave(0);
    }
    #endregion

    #region Side Functions
    public void CloneDataBase(ZomDataBase dtb)
    {
        if (dtb == null) return;
        _dtb = Instantiate(dtb);
    }

    public void CloneZomList(ZomDataBase dtb) => _dtb.packages = dtb.GetPackageList();

    protected bool CanSpawn(ZomDataBase dtb)
    {
        if (dtb == null) return false;
        if (!dtb.CanUseDataBase()) return false;
        return true;
    }

    private uint NextSeed()
    {
        seedCounter += 0x9E3779B9;
        if (seedCounter == 0) seedCounter = 1;
        return seedCounter;
    }
    #endregion

    #region Job
    [BurstCompile]
    private struct SpawnOffsetJob : IJobParallelFor
    {
        public float Radius;
        public uint Seed;
        public NativeArray<float2> Offsets;

        public void Execute(int index)
        {
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random(Seed + (uint)index * 747796405u + 2891336453u);

            float angle = rng.NextFloat(0f, math.PI * 2f);
            float dist = math.sqrt(rng.NextFloat(0f, 1f)) * Radius;

            Offsets[index] = new float2(math.cos(angle), math.sin(angle)) * dist;
        }
    }
    #endregion
}