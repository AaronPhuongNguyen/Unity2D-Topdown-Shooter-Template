using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Button-friendly wrapper for WorldManager.instance, and the owner of its UI.
/// Put it on the menu canvas and hook the public methods to Button OnClick.
/// </summary>
public class WorldManagerReader : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject noSaveFileUI;
    [SerializeField] private Button continueButton;   // optional: greyed out when no save

    private void Start()
    {
        if (noSaveFileUI != null) noSaveFileUI.SetActive(false);
        RefreshContinueButton();
    }

    private bool TryGetManager(out WorldManager manager)
    {
        manager = WorldManager.instance;
        if (manager == null)
            Debug.LogError("WorldManagerReader: no WorldManager in the scene.");
        return manager != null;
    }

    private void RefreshContinueButton()
    {
        if (continueButton != null && WorldManager.instance != null)
            continueButton.interactable = WorldManager.instance.HasSave;
    }

    // Hook to the Continue button.
    public void Continue()
    {
        if (!TryGetManager(out var wm)) return;

        bool loading = wm.Continue();

        // No save -> show the NoSaveFile UI. Loading -> make sure it's hidden.
        if (noSaveFileUI != null) noSaveFileUI.SetActive(!loading && !wm.HasSave);
    }

    // Hook to the NoSaveFile UI's close/OK button.
    public void CloseNoSaveFile()
    {
        if (noSaveFileUI != null) noSaveFileUI.SetActive(false);
    }

    // Hook to a "Reload save" button, if you need one.
    public void ReloadFromDisk()
    {
        if (!TryGetManager(out var wm)) return;
        wm.LoadFromDisk();
        RefreshContinueButton();
    }

    // Hook to a "Delete save" button.
    public void DeleteSave()
    {
        if (!TryGetManager(out var wm)) return;
        SaveSystem.Delete(WorldManager.Slot);
        wm.LoadFromDisk();   // data becomes null, so HasSave is false
        RefreshContinueButton();
    }
}