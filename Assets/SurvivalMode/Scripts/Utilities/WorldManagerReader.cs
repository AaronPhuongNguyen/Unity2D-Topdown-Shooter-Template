using System.Collections;
using UnityEngine;
using UnityEngine.Events;
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

    [Header("Save Feedback (optional)")]
    [SerializeField] private GameObject savedToast;   // e.g. a "Game Saved!" label
    [SerializeField] private float toastSeconds = 1.5f;

    [Header("Events")]
    [Tooltip("Fired when Continue finds a valid save. Hook LoadingAccess.Load() here.")]
    [SerializeField] private UnityEvent onContinueSuccess;

    private Coroutine toastRoutine;

    private void Start()
    {
        if (noSaveFileUI != null) noSaveFileUI.SetActive(false);
        if (savedToast != null) savedToast.SetActive(false);
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

        bool ok = wm.Continue();

        // No save -> show the NoSaveFile UI. Valid save -> hide it and fire the event.
        if (noSaveFileUI != null) noSaveFileUI.SetActive(!ok && !wm.HasSave);

        if (ok) onContinueSuccess?.Invoke();
    }

    // Hook to the SaveGame button.
    public void SaveGame()
    {
        if (!TryGetManager(out var wm)) return;

        bool ok = wm.Save();
        RefreshContinueButton();

        if (ok && savedToast != null && isActiveAndEnabled)
        {
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ShowToast());
        }
    }

    private IEnumerator ShowToast()
    {
        savedToast.SetActive(true);
        yield return new WaitForSecondsRealtime(toastSeconds);   // realtime, so it works while paused
        savedToast.SetActive(false);
        toastRoutine = null;
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