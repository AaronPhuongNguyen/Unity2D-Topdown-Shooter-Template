using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Loads a scene by name behind its own loading canvas + slider.
/// Other systems register work with AddTotalSteps() and report it with CompleteStep().
/// The scene load itself counts as one extra step, so the bar moves even with zero custom steps.
/// Child of GameManager (already DontDestroyOnLoad), so no DontDestroyOnLoad here.
/// </summary>
public class LoadingHandle : MonoBehaviour
{
    public static LoadingHandle Instance { get; private set; }

    [Header("UI (its own canvas)")]
    [SerializeField] private Canvas canvas;      // Screen Space - Overlay, high Sorting Order
    [SerializeField] private Slider slider;      // loading bar
    [SerializeField] private TextMeshProUGUI txtShower;   // shows "current / total"

    [Header("Settings")]
    [Tooltip("How fast the bar fills (progress per second). Higher = snappier.")]
    [SerializeField, Min(0.1f)] private float fillSpeed = 2f;
    [Tooltip("Frames to wait after the scene activates so its Awake/Start can register steps.")]
    [SerializeField, Min(1)] private int settleFrames = 2;

    // ---------- steps ----------
    public int TotalSteps { get; private set; }
    public int CurrentSteps { get; private set; }
    public bool IsLoading { get; private set; }

    /// <summary>
    /// False while a scene + all its registered steps are still loading (Time.timeScale = 0),
    /// true once everything is ready (Time.timeScale = 1). Defaults to true outside of loading.
    /// </summary>
    public bool IsLoaded { get; private set; } = true;

    /// <summary>0..1 including the scene load itself.</summary>
    public float Progress => (CurrentSteps + sceneProgress) / (TotalSteps + 1f);

    /// <summary>Fired once the scene is loaded, all steps are done and the bar is hidden.</summary>
    public event Action OnLoadFinished;

    private float sceneProgress;
    private int shownCurrent = -1, shownTotal = -1;   // last values written to the text
    private static readonly int UnscaledTimeId = Shader.PropertyToID("_UnscaledTime");
    private string loadingAssetName = "Loading Asset";

    // =====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.interactable = false;
        slider.value = 0f;

        canvas.enabled = false;     // keep this script's GameObject active; only hide the visuals
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---------- public API ----------

    /// <summary>Loads a scene by name behind the loading screen.</summary>
    public void LoadScene(string sceneName)
    {
        if (IsLoading) return;
        StartCoroutine(LoadRoutine(sceneName));
    }

    /// <summary>Register more work. Call before it starts (e.g. in Awake of the new scene).</summary>
    public void AddTotalSteps(int count = 1, string loadingLog = "Loading Asset")
    {
        TotalSteps += Mathf.Max(0, count);
        loadingAssetName = loadingLog;
    }

    /// <summary>Report finished work.</summary>
    public void CompleteStep(int count = 1)
    {
        CurrentSteps = Mathf.Min(CurrentSteps + Mathf.Max(0, count), TotalSteps);
    }

    public void ResetSteps()
    {
        TotalSteps = 0;
        CurrentSteps = 0;
    }

    // ---------- internals ----------

    /// <summary>Single place that gates the game: not loaded = frozen, loaded = running.</summary>
    private void SetLoaded(bool loaded)
    {
        IsLoaded = loaded;
        Time.timeScale = loaded ? 1f : 0f;
    }

    /// <summary>Writes "current / total" to the label. Only touches the text when a number changed.</summary>
    private void RefreshText(bool force = false)
    {
        if (txtShower == null) return;
        if (!force && CurrentSteps == shownCurrent && TotalSteps == shownTotal) return;

        shownCurrent = CurrentSteps;
        shownTotal = TotalSteps;
        txtShower.text = $"{loadingAssetName}: {(Progress * 100f).ToString("F0")}%";
    }

    private void Update()
    {
        if (!canvas.enabled) return;

        // Shader _Time freezes at timeScale 0, so feed it real time for the slider shine.
        Shader.SetGlobalFloat(UnscaledTimeId, Time.unscaledTime);

        // Unscaled so the bar still moves if GameSpeed / timeScale is 0.
        slider.value = Mathf.MoveTowards(slider.value, Progress, fillSpeed * Time.unscaledDeltaTime);

        RefreshText();
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        IsLoading = true;
        ResetSteps();
        loadingAssetName = "Loading World";
        SetLoaded(false);
        sceneProgress = 0f;
        slider.value = 0f;
        canvas.enabled = true;

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            Debug.LogError($"LoadingHandle: could not load scene '{sceneName}'.");
            canvas.enabled = false;
            IsLoading = false;
            SetLoaded(true);
            yield break;
        }
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
        {
            sceneProgress = op.progress;
            RefreshText(true);
            yield return null;
        }
        sceneProgress = 1f;
        RefreshText(true);
        op.allowSceneActivation = true;

        while (!op.isDone) yield return null;   

        for (int i = 0; i < settleFrames; i++) yield return null;

        while (CurrentSteps < TotalSteps) yield return null;

        while (slider.value < 0.999f) yield return null;

        yield return new WaitForSecondsRealtime(2);

        canvas.enabled = false;
        IsLoading = false;
        SetLoaded(true);
        OnLoadFinished?.Invoke();
    }
}