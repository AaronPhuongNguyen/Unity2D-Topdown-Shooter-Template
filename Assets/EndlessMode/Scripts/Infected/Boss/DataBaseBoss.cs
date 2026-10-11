using Server;
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Boss Data", menuName = "Endless/DataBase/Boss")]
public class ZomBossDataBase : ScriptableObject
{
    public List<BossData> list = new List<BossData>();

    // Caches are built lazily and rebuilt only when the list changes
    // (OnValidate / OnEnable), so runtime lookups allocate nothing.
    [NonSerialized] private bool _dirty = true;
    [NonSerialized] private readonly Dictionary<int, BossData> _byId = new Dictionary<int, BossData>();
    [NonSerialized] private readonly List<BossData> _valid = new List<BossData>();
    [NonSerialized] private readonly List<BossData> _eligible = new List<BossData>();

    private void OnEnable() => _dirty = true;
    private void OnValidate() => _dirty = true;

    /// <summary>Call if you modify <see cref="list"/> at runtime.</summary>
    public void Invalidate() => _dirty = true;

    private void EnsureCache()
    {
        if (!_dirty) return;
        _dirty = false;
        _byId.Clear();
        _valid.Clear();

        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            BossData b = list[i];
            if (b == null || !b.IsValid) continue;
            _valid.Add(b);
            if (!_byId.TryAdd(b.ID, b))
                Debug.LogWarning($"ZomBossDataBase: duplicate boss ID {b.ID} ({b.name}) ignored.", this);
        }
    }

    public bool CanUseDataBase()
    {
        EnsureCache();
        return _valid.Count > 0;
    }

    public BossData GetBossByID(int id)
    {
        EnsureCache();
        return _byId.TryGetValue(id, out var b) ? b : null;
    }

    public GameObject GetBossPrefabByID(int id) => GetBossByID(id)?.prefab;

    /// <summary>Read-only view of valid bosses. Do not mutate; no allocation.</summary>
    public IReadOnlyList<BossData> ValidBosses
    {
        get { EnsureCache(); return _valid; }
    }

    /// <summary>Defensive copy for callers that need to mutate.</summary>
    public List<BossData> GetBossList() => list != null ? new List<BossData>(list) : new List<BossData>();
    public List<BossData> GetValidBosses() { EnsureCache(); return new List<BossData>(_valid); }

    /// <summary>
    /// Weighted random pick among bosses whose minWave &lt;= wave.
    /// wave &lt; 0 skips the wave filter. Allocation-free.
    /// </summary>
    public BossData GetRandomBoss(int wave = -1)
    {
        EnsureCache();
        _eligible.Clear();
        float total = 0f;

        for (int i = 0; i < _valid.Count; i++)
        {
            BossData b = _valid[i];
            if (wave >= 0 && b.minWave > wave) continue;
            _eligible.Add(b);
            total += b.Weight;
        }

        if (_eligible.Count == 0) return null;
        if (_eligible.Count == 1) return _eligible[0];

        float roll = RNG.GetFloat(0f, total);
        for (int i = 0; i < _eligible.Count; i++)
        {
            roll -= _eligible[i].Weight;
            if (roll <= 0f) return _eligible[i];
        }
        return _eligible[_eligible.Count - 1];
    }
}

[Serializable]
public class BossData
{
    public string name = "Boss";
    public int ID;
    public GameObject prefab;

    [Tooltip("Stats/currency package. Required: HiveBrain.SpawnZom fails without it.")]
    public ZomPackage package;

    [Tooltip("Boss can only appear on or after this wave.")]
    public int minWave = 0;

    [Tooltip("Relative chance when rolling among eligible bosses.")]
    [Min(0f)] public float weight = 1f;

    public float Weight => Mathf.Max(0.0001f, weight);
    public bool IsValid => prefab != null && package != null;
}