using Server;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns ONE random NPC from the list at the player's position every N waves
/// (default 5). Only one NPC can be alive at a time: if the previous one is
/// still alive when the next milestone comes, nothing is spawned.
/// NPCs remove themselves on game restart; this script just forgets them.
/// </summary>
public class NpcSpawner : MonoBehaviour
{
    [Serializable]
    public class NpcEntry
    {
        public string name = "Npc";
        public GameObject prefab;
        public NpcPackage package;

        public bool IsValid => prefab != null && package != null;
    }

    PlayerManager pm => PlayerManager.instance;

    [Header("Spawn")]
    [Tooltip("An NPC is spawned on every wave that is a multiple of this.")]
    [SerializeField] private int wavesPerSpawn = 5;

    [Tooltip("NPCs to pick from (random, equal chance).")]
    [SerializeField] private List<NpcEntry> npcList = new List<NpcEntry>();

    private NpcBrain current;
    private int lastSpawnWave = -1;

    // Reused so picking a random entry never allocates.
    private readonly List<NpcEntry> validBuffer = new List<NpcEntry>();

    #region Lifecycle
    private void OnEnable()
    {
        EventBus.OnNewWave += OnNewWave;
        EventBus.OnGameRestart += OnGameRestart;
    }

    private void OnDisable()
    {
        EventBus.OnNewWave -= OnNewWave;
        EventBus.OnGameRestart -= OnGameRestart;
    }

    private void OnGameRestart()
    {
        // The NPC removes itself on restart; just reset our bookkeeping.
        current = null;
        lastSpawnWave = -1;
    }
    #endregion

    #region Spawning
    private void OnNewWave(int wave)
    {
        if (wavesPerSpawn <= 0 || wave <= 0) return;
        if (wave % wavesPerSpawn != 0) return;
        if (wave == lastSpawnWave) return; // event fired twice for the same wave

        lastSpawnWave = wave;
        TrySpawn();
    }

    /// <summary>Spawns one random NPC at the player's position if none is alive.</summary>
    public NpcBrain TrySpawn()
    {
        if (current != null && current.IsActive) return null; // only one at a time

        if (pm == null || pm.Controlling == null)
        {
            Debug.LogWarning("NpcSpawner: no player to spawn at.");
            return null;
        }

        NpcEntry entry = PickRandom();
        if (entry == null)
        {
            Debug.LogWarning("NpcSpawner: npcList has no valid entry (prefab + package).");
            return null;
        }

        current = NpcBrain.Spawn(entry.prefab, entry.package, pm.Controlling.transform.position);
        return current;
    }

    private NpcEntry PickRandom()
    {
        validBuffer.Clear();
        for (int i = 0; i < npcList.Count; i++)
        {
            NpcEntry e = npcList[i];
            if (e != null && e.IsValid) validBuffer.Add(e);
        }

        if (validBuffer.Count == 0) return null;
        return validBuffer[RNG.GetInt(0, validBuffer.Count)];
    }
    #endregion

    #region Debugger
    [ContextMenu("Test Spawn Npc")]
    private void Test_Spawn() => TrySpawn();
    #endregion
}