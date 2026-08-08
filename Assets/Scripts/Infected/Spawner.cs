using Server;
using System.Collections.Generic;
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
            int spawnCount = RNG.GetInt(2, 5 * Mathf.Max(1, wave / 2));
            Spawn(package, transform.position, spawnCount, isRandom: true, SpawnRadius);
        }
    }

    private List<ZomPackage> GetEligiblePackages(int wave)
    {
        List<ZomPackage> result = new List<ZomPackage>();

        foreach (ZomPackage package in _dtb.packages)
        {
            if (package == null) continue;
            if (wave < package.AppearFromWave) continue;

            result.Add(package);
        }

        return result;
    }

    private List<ZomPackage> GetRandomPackages(List<ZomPackage> eligible)
    {
        List<ZomPackage> selected = new List<ZomPackage>();
        if (eligible.Count == 0) return selected;

        int kindCount = RNG.GetInt(1, eligible.Count + 1);
        List<int> usedIndices = new List<int>();

        int safety = 0;
        while (selected.Count < kindCount && safety < kindCount * 20)
        {
            int roll = RNG.GetInt(0, eligible.Count);
            safety++;

            if (usedIndices.Contains(roll)) continue;
            usedIndices.Add(roll);

            ZomPackage candidate = eligible[roll];

            if (RNG.GetPercent() <= candidate._rateToAppear)
            {
                selected.Add(candidate);
            }
        }

        return selected;
    }

    public GameObject Spawn(ZomPackage package, Vector2 pos)
    {
        if (package == null || package.prefab == null) return null;

        GameObject o = hb.SpawnZom(package.prefab, package);
        if (o == null) return null;

        o.transform.position = pos;
        return o;
    }

    public List<GameObject> Spawn(ZomPackage package, Vector2 pos, int count)
    {
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
        if (!isRandom) return Spawn(package, pos, count);

        List<GameObject> results = new List<GameObject>(Mathf.Max(0, count));
        if (count <= 0) return results;

        for (int i = 0; i < count; i++)
        {
            float angle = RNG.GetFloat(0, Mathf.PI * 2f);
            float dist = RNG.GetFloat(0, radius);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

            GameObject o = Spawn(package, pos + offset);
            if (o != null) results.Add(o);
        }
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
    #endregion
}