using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SurvivalState
{
    None,        // before Start
    Generating,  // startup steps are running
    Ready,       // world built, game can run
    Failed       // a step failed (see Console)
}

/// <summary>
/// Scene coordinator for the survival game scene.
/// On Start it runs the startup steps in order (Grid -> Terrain -> anything registered later),
/// reports to LoadingHandle, then raises OnWorldReady.
/// Access anywhere: SurvivalManager.Instance.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-2)]
public class SurvivalManager : MonoBehaviour
{
    #region Singleton
    public static SurvivalManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }
    #endregion

    #region Inspector
    [Header("Systems (empty = found in the scene)")]
    [SerializeField] private GridGenerator grid;
    [SerializeField] private Terrain_Generator terrain;

    [Header("Start")]
    [Tooltip("Run the startup steps automatically in Start. Turn off to call StartWorld() yourself.")]
    [SerializeField] private bool buildOnStart = true;
    [Tooltip("After the first build of a new world, set IsNew = false and save.")]
    [SerializeField] private bool markWorldCreated = true;

    [Header("Scenes")]
    [SerializeField] private string menuSceneName = "MainMenu";

    [Header("Debug")]
    [SerializeField] private bool logSteps = true;
    #endregion

    #region Startup steps
    private class Step
    {
        public string name;
        public int order;
        public Func<IEnumerator> run;
    }

    // Built-in orders. Register your own with any number between/after these.
    public const int OrderGrid = 0;
    public const int OrderTerrain = 100;
    public const int OrderNature = 200;     // future
    public const int OrderPlayer = 300;     // future

    private readonly List<Step> steps = new List<Step>();
    private bool builtInRegistered;

    /// <summary>
    /// Adds a startup step, for example: AddStartupStep("Nature", SurvivalManager.OrderNature, nature.GenerateRoutine);
    /// Call it in Awake of your system so it exists before Start runs. Steps run lowest order first.
    /// </summary>
    public void AddStartupStep(string stepName, int order, Func<IEnumerator> run)
    {
        if (run == null) return;
        if (State == SurvivalState.Generating || State == SurvivalState.Ready)
            Debug.LogWarning($"[SurvivalManager] Step '{stepName}' was added after startup began, it will not run.", this);
        steps.Add(new Step { name = stepName, order = order, run = run });
    }

    private void RegisterBuiltInSteps()
    {
        if (builtInRegistered) return;
        builtInRegistered = true;
        steps.Add(new Step { name = "Grid", order = OrderGrid, run = RunGrid });
        steps.Add(new Step { name = "Terrain", order = OrderTerrain, run = RunTerrain });
    }

    private IEnumerator RunGrid()
    {
        if (Grid == null) { Debug.LogError("[SurvivalManager] No GridGenerator in the scene.", this); yield break; }

        while (Grid.IsGenerating) yield return null;               // it auto-started itself
        if (!Grid.IsGenerated) yield return Grid.GenerateRoutine();
        if (!Grid.IsGenerated) Debug.LogError("[SurvivalManager] Grid failed to generate.", this);
    }

    private IEnumerator RunTerrain()
    {
        if (Terrain == null) { Debug.LogError("[SurvivalManager] No TerrainGenerator in the scene.", this); yield break; }
        if (Grid == null || !Grid.IsGenerated) { Debug.LogError("[SurvivalManager] Terrain needs the grid first.", this); yield break; }

        while (Terrain.IsGenerating) yield return null;
        if (!Terrain.IsGenerated) yield return Terrain.GenerateRoutine();
        if (!Terrain.IsGenerated) Debug.LogError("[SurvivalManager] Terrain failed to generate.", this);
    }
    #endregion

    #region State / events
    public SurvivalState State { get; private set; } = SurvivalState.None;
    public bool IsReady => State == SurvivalState.Ready;
    public string CurrentStepName { get; private set; } = "";

    /// <summary>Fired before each startup step: (name, index, total).</summary>
    public event Action<string, int, int> OnStepStarted;
    /// <summary>Fired once when every step finished. Spawn the player, start AI, etc. here.</summary>
    public event Action OnWorldReady;
    /// <summary>Fired when a step failed. The game scene should not continue.</summary>
    public event Action OnWorldFailed;
    /// <summary>Fired right before the file is written. Copy your system's state into WorldManager.data here.</summary>
    public event Action OnSaving;
    public event Action<bool> OnPauseChanged;

    /// <summary>Runs the action now if the world is ready, otherwise as soon as it becomes ready.</summary>
    public void WhenReady(Action action)
    {
        if (action == null) return;
        if (IsReady) action();
        else OnWorldReady += action;   // note: stays subscribed, fine for one-time setup in Start
    }
    #endregion

    #region Systems and world data
    public GridGenerator Grid => grid != null ? grid : (grid = FindFirstObjectByType<GridGenerator>());
    public Terrain_Generator Terrain => terrain != null ? terrain : (terrain = FindFirstObjectByType<Terrain_Generator>());

    public WorldManager WorldMgr => WorldManager.instance;
    public SaveData Data => WorldMgr != null ? WorldMgr.data : null;
    public WorldScript World => Data != null ? Data.WorldScript : null;
    public PlayerData Player => Data != null ? Data.PlayerData : null;

    public uint Seed => World != null ? World.Seed : 0u;
    public uint WorldSize => World != null ? World.WorldSize : 0u;
    public float Difficulty => World != null ? World.Difficulty : 1f;
    public bool KeepInventory => World == null || World.KeepInventory;
    public bool IsNewWorld => World != null && World.IsNew;
    #endregion

    #region Lifecycle
    private bool saveHooked;
    private int gateSteps;          // steps we registered on LoadingHandle and still owe

    private void Start()
    {
        HookSave();
        if (buildOnStart) StartWorld();
    }

    private void OnDestroy()
    {
        if (saveHooked && WorldManager.instance != null)
            WorldManager.instance.OnBeforeSave -= ForwardSaving;
        ReleaseGate();
        if (Instance == this) Instance = null;
    }

    private void HookSave()
    {
        if (saveHooked || WorldManager.instance == null) return;
        WorldManager.instance.OnBeforeSave += ForwardSaving;
        saveHooked = true;
    }

    private void ForwardSaving() => OnSaving?.Invoke();

    /// <summary>Starts the startup pipeline. Ignored if it already started.</summary>
    [ContextMenu("Start World")]
    public void StartWorld()
    {
        if (State == SurvivalState.Generating || State == SurvivalState.Ready) return;
        StartCoroutine(StartRoutine());
    }

    private IEnumerator StartRoutine()
    {
        State = SurvivalState.Generating;
        RegisterBuiltInSteps();
        steps.Sort((a, b) => a.order.CompareTo(b.order));

        // Gate: Grid finishes its steps before Terrain registers its own, which could let
        // LoadingHandle think everything is done. This one step keeps the screen up until we finish.
        TakeGate();

        bool ok = true;
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                Step s = steps[i];
                CurrentStepName = s.name;
                if (logSteps) Debug.Log($"[SurvivalManager] Step {i + 1}/{steps.Count}: {s.name}", this);
                OnStepStarted?.Invoke(s.name, i, steps.Count);

                yield return s.run();

                if (!StepSucceeded(s)) { ok = false; break; }
            }
        }
        finally
        {
            ReleaseGate();   // always, so the loading screen can never hang
        }

        CurrentStepName = "";

        if (!ok)
        {
            State = SurvivalState.Failed;
            OnWorldFailed?.Invoke();
            yield break;
        }

        State = SurvivalState.Ready;

        if (markWorldCreated && World != null && World.IsNew)
        {
            World.IsNew = false;
            SaveGame();
        }

        if (logSteps) Debug.Log("[SurvivalManager] World ready.", this);
        OnWorldReady?.Invoke();
    }

    /// <summary>Built-in steps are checked by their IsGenerated flag. Custom steps are trusted.</summary>
    private bool StepSucceeded(Step s)
    {
        if (s.order == OrderGrid && s.name == "Grid") return Grid != null && Grid.IsGenerated;
        if (s.order == OrderTerrain && s.name == "Terrain") return Terrain != null && Terrain.IsGenerated;
        return true;
    }

    private void TakeGate()
    {
        if (LoadingHandle.Instance == null) return;
        LoadingHandle.Instance.AddTotalSteps(1, "Loading World");
        gateSteps++;
    }

    private void ReleaseGate()
    {
        if (gateSteps <= 0) return;
        if (LoadingHandle.Instance != null) LoadingHandle.Instance.CompleteStep(gateSteps);
        gateSteps = 0;
    }

    /// <summary>Clears and rebuilds everything with the current WorldScript. Mostly for testing.</summary>
    [ContextMenu("Rebuild World")]
    public void RebuildWorld()
    {
        StopAllCoroutines();
        ReleaseGate();
        if (Terrain != null) Terrain.Clear();
        if (Grid != null) Grid.Clear();
        State = SurvivalState.None;
        StartWorld();
    }
    #endregion

    #region Save / scenes
    /// <summary>Saves everything. Systems add their state through OnSaving.</summary>
    public bool SaveGame()
    {
        HookSave();
        return WorldMgr != null && WorldMgr.Save();
    }

    /// <summary>Optionally saves, then loads the menu scene behind the loading screen.</summary>
    public void ReturnToMenu(bool save = true)
    {
        if (save && IsReady) SaveGame();
        SetPaused(false);

        if (LoadingHandle.Instance == null)
        {
            Debug.LogError("[SurvivalManager] LoadingHandle is missing, cannot load the menu.", this);
            return;
        }
        LoadingHandle.Instance.LoadScene(menuSceneName, false, "Loading Menu");
    }
    #endregion

    #region Pause
    public bool IsPaused { get; private set; }
    private float speedBeforePause = 1f;

    public void SetPaused(bool paused)
    {
        if (paused == IsPaused) return;

        // The loading screen owns timeScale while it is up.
        if (LoadingHandle.Instance != null && !LoadingHandle.Instance.IsLoaded) return;

        IsPaused = paused;
        if (paused)
        {
            speedBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = speedBeforePause;
        }
        OnPauseChanged?.Invoke(paused);
    }

    public void TogglePause() => SetPaused(!IsPaused);
    #endregion

    #region World queries (shortcuts so other scripts don't touch Grid + Terrain separately)
    public Vector3Int WorldToCell(Vector3 world)
        => Grid != null ? Grid.WorldToCell(world) : Vector3Int.RoundToInt(world);

    public Vector3 CellToWorld(Vector3Int cell)
        => Grid != null ? Grid.CellToWorld(cell) : cell;

    public bool InBounds(Vector3Int cell) => Grid != null && Grid.InBounds(cell);

    /// <summary>Inside the map, not blocked, and not water.</summary>
    public bool IsWalkable(Vector3Int cell)
    {
        if (!IsReady || Grid == null || !Grid.InBounds(cell)) return false;
        if (Grid.IsBlockingPath(cell)) return false;
        return Terrain == null || !Terrain.IsWater(cell);
    }

    /// <summary>
    /// Searches in growing rings around a cell for the closest walkable, unoccupied cell.
    /// Use it for the player spawn: TryFindSpawnCell(Vector3Int.zero, 60, out cell).
    /// </summary>
    public bool TryFindSpawnCell(Vector3Int around, int maxRadius, out Vector3Int result)
    {
        result = around;
        if (!IsReady) return false;

        for (int r = 0; r <= maxRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    // only the ring edge, the inside was checked by smaller radii
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;

                    var c = new Vector3Int(around.x + dx, around.y + dy, 0);
                    if (!IsWalkable(c) || Grid.IsOccupied(c, GridLayer.Entity)) continue;

                    result = c;
                    return true;
                }
            }
        }
        return false;
    }

    public Vector3 FindSpawnWorldPosition(int maxRadius = 80)
        => TryFindSpawnCell(Vector3Int.zero, maxRadius, out Vector3Int c) ? CellToWorld(c) : Vector3.zero;
    #endregion
}