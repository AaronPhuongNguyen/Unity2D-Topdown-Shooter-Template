using UnityEngine;

[DefaultExecutionOrder(-5)]
public class WorldManager : MonoBehaviour
{
    #region Singleton
    public static WorldManager instance { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        LoadFromDisk();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
    #endregion

    public const string Slot = "save";

    [Header("Survival World")]
    public SaveData data;

    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = "SurvivalMode";
    [SerializeField] private bool useConfirmButton = true;

    /// <summary>True when a save exists and it actually contains a created world.</summary>
    public bool HasSave => data != null && data.WorldScript != null;

    /// <summary>Reads the save from disk. data stays null if there is no save file.</summary>
    public void LoadFromDisk()
    {
        data = SaveSystem.Exists(Slot) ? SaveSystem.Load(Slot) : null;
    }

    /// <summary>
    /// Loads the game scene if a save exists.
    /// Returns false when there is no save (the caller decides what UI to show).
    /// </summary>
    public bool Continue()
    {
        LoadFromDisk();
        if (!HasSave) return false;

        if (LoadingHandle.Instance == null)
        {
            Debug.LogError("WorldManager: LoadingHandle is missing, cannot load scene.");
            return false;
        }

        LoadingHandle.Instance.LoadScene(gameSceneName, useConfirmButton);
        return true;
    }

    /// <summary>Overrides SaveData.WorldScript with the given world and writes it to disk.</summary>
    public void CreateWorld(WorldScript newWorld)
    {
        if (data == null) data = new SaveData();

        // Copy so later UI edits don't silently change the saved world.
        data.WorldScript = JsonUtility.FromJson<WorldScript>(JsonUtility.ToJson(newWorld));
        data.WorldScript.IsNew = true;

        SaveSystem.Save(data, Slot);
    }
}