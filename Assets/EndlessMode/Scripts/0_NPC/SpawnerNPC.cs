using UnityEngine;

/// <summary>
/// Global entry point for spawning NPCs. No scene object needed - call it from anywhere
/// (buffs, skills, events...). The NpcPackage carries its own prefab, so the
/// package is all you need:
///
///     NpcSpawner.Spawn(package, position);
///     NpcSpawner.SpawnAtPlayer(package);
/// </summary>
public static class NpcSpawner
{
    /// <summary>Spawns an NPC from its package at a world position. Returns null on failure.</summary>
    public static NpcBrain Spawn(NpcPackage package, Vector3 position)
    {
        if (package == null)
        {
            Debug.LogWarning("NpcSpawner: package is null.");
            return null;
        }

        if (package.prefab == null)
        {
            Debug.LogWarning($"NpcSpawner: package '{package.packName}' has no prefab.");
            return null;
        }

        return NpcBrain.Spawn(package.prefab, package, position);
    }

    /// <summary>Spawns an NPC from its package at the player's current position.</summary>
    public static NpcBrain SpawnAtPlayer(NpcPackage package)
    {
        PlayerManager pm = PlayerManager.instance;
        if (pm == null || pm.Controlling == null)
        {
            Debug.LogWarning("NpcSpawner: no player to spawn at.");
            return null;
        }

        return Spawn(package, pm.Controlling.transform.position);
    }
}