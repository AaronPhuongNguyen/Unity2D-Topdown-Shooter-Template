using UnityEngine;

/// <summary>
/// Simple button access for saving. Put it on any object and hook
/// the public methods to Button OnClick. It only forwards to WorldManager.
/// </summary>
public class SerializationAccess : MonoBehaviour
{
    private bool TryGetManager(out WorldManager manager)
    {
        manager = WorldManager.instance;
        if (manager == null)
            Debug.LogError("SerializationAccess: no WorldManager in the scene.", this);
        return manager != null;
    }

    // Saves everything in memory to disk.
    public void Save()
    {
        if (TryGetManager(out var wm)) wm.Save();
    }

    // Re-reads the save file from disk (discards unsaved changes).
    public void Load()
    {
        if (TryGetManager(out var wm)) wm.LoadFromDisk();
    }

    // Deletes the save file and clears the data in memory.
    public void Delete()
    {
        if (!TryGetManager(out var wm)) return;
        SaveSystem.Delete(WorldManager.Slot);
        wm.LoadFromDisk();   // data becomes null
    }
}