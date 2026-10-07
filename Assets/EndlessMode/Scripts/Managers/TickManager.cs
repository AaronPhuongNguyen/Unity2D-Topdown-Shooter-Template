using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central tick dispatcher. Singleton, mobile-optimized:
/// - No boxing (interfaces are reference types, arrays/lists store references directly).
/// - No per-frame allocations (add/remove queued and flushed once per phase, no foreach enumerators).
/// - Backward for-loops so removals during iteration are safe and cheap.
/// - Custom GameSpeed decoupled from Time.timeScale.
/// </summary>
[DefaultExecutionOrder(-999)]
public sealed class TickSystem : MonoBehaviour
{
    private static TickSystem _instance;

    /// <summary>
    /// Passive accessor - returns null if no TickSystem exists yet or if it has
    /// already been destroyed (e.g. during scene/app teardown). Never creates one.
    /// Safe to call from OnDisable/OnDestroy of any ticking object without risk
    /// of resurrecting a destroyed singleton into a new orphan GameObject.
    /// </summary>
    public static TickSystem Instance => _instance;

    /// <summary>
    /// Guarantees a TickSystem exists, creating one if necessary. Call this
    /// explicitly from game bootstrap code (e.g. a startup/bootstrap scene
    /// script) - never called implicitly by Register/Unregister/Instance,
    /// so shutdown-time access can never spawn a stray instance.
    /// </summary>
    public static TickSystem EnsureExists()
    {
        if (_instance == null)
        {
            var go = new GameObject("TickSystem");
            _instance = go.AddComponent<TickSystem>();
            DontDestroyOnLoad(go);
        }
        return _instance;
    }

    // ---------------- GameSpeed ----------------

    private static float _gameSpeed = 1f;
    public static float GameSpeed => _gameSpeed;

    public static event Action<float> OnGameSpeedChanged;

    public static void SetGameSpeed(float speed)
    {
        _gameSpeed = Mathf.Max(0f, speed);
        OnGameSpeedChanged?.Invoke(_gameSpeed);
    }

    public static void AddGameSpeed(float delta) => SetGameSpeed(_gameSpeed + delta);

    public static void MultiplyGameSpeed(float factor) => SetGameSpeed(_gameSpeed * factor);

    public static void ResetGameSpeed() => SetGameSpeed(1f);

    public static void PauseGame() => SetGameSpeed(0f);

    // ---------------- Storage ----------------

    private readonly List<ITick> _ticks = new List<ITick>(128);
    private readonly List<ILateTick> _lateTicks = new List<ILateTick>(64);
    private readonly List<IUnscaledTick> _unscaledTicks = new List<IUnscaledTick>(64);

    // Pending add/remove queues, flushed at the start of each phase so that
    // Register/Unregister calls made *during* iteration never mutate a list mid-loop.
    private readonly List<ITick> _pendingAddTicks = new List<ITick>(16);
    private readonly List<ITick> _pendingRemoveTicks = new List<ITick>(16);

    private readonly List<ILateTick> _pendingAddLateTicks = new List<ILateTick>(16);
    private readonly List<ILateTick> _pendingRemoveLateTicks = new List<ILateTick>(16);

    private readonly List<IUnscaledTick> _pendingAddUnscaledTicks = new List<IUnscaledTick>(16);
    private readonly List<IUnscaledTick> _pendingRemoveUnscaledTicks = new List<IUnscaledTick>(16);

    private bool _dirtyTicks;
    private bool _dirtyLateTicks;
    private bool _dirtyUnscaledTicks;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        ResetGameSpeed();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ---------------- Public API: Register/Unregister ----------------

    public static void Register(ITick ticker)
    {
        var inst = _instance;
        if (inst == null) return; // no instance - never create one just to register
        inst._pendingRemoveTicks.Remove(ticker);
        if (!inst._pendingAddTicks.Contains(ticker))
            inst._pendingAddTicks.Add(ticker);
        inst._dirtyTicks = true;
    }

    public static void Unregister(ITick ticker)
    {
        var inst = _instance;
        if (inst == null) return;
        inst._pendingAddTicks.Remove(ticker);
        if (!inst._pendingRemoveTicks.Contains(ticker))
            inst._pendingRemoveTicks.Add(ticker);
        inst._dirtyTicks = true;
    }

    public static void Register(ILateTick ticker)
    {
        var inst = _instance;
        if (inst == null) return;
        inst._pendingRemoveLateTicks.Remove(ticker);
        if (!inst._pendingAddLateTicks.Contains(ticker))
            inst._pendingAddLateTicks.Add(ticker);
        inst._dirtyLateTicks = true;
    }

    public static void Unregister(ILateTick ticker)
    {
        var inst = _instance;
        if (inst == null) return;
        inst._pendingAddLateTicks.Remove(ticker);
        if (!inst._pendingRemoveLateTicks.Contains(ticker))
            inst._pendingRemoveLateTicks.Add(ticker);
        inst._dirtyLateTicks = true;
    }

    public static void Register(IUnscaledTick ticker)
    {
        var inst = _instance;
        if (inst == null) return;
        inst._pendingRemoveUnscaledTicks.Remove(ticker);
        if (!inst._pendingAddUnscaledTicks.Contains(ticker))
            inst._pendingAddUnscaledTicks.Add(ticker);
        inst._dirtyUnscaledTicks = true;
    }

    public static void Unregister(IUnscaledTick ticker)
    {
        var inst = _instance;
        if (inst == null) return;
        inst._pendingAddUnscaledTicks.Remove(ticker);
        if (!inst._pendingRemoveUnscaledTicks.Contains(ticker))
            inst._pendingRemoveUnscaledTicks.Add(ticker);
        inst._dirtyUnscaledTicks = true;
    }

    /// <summary>
    /// Convenience: registers an object for every tick interface it implements.
    /// </summary>
    public static void RegisterAll(object obj)
    {
        if (obj is ITick t) Register(t);
        if (obj is ILateTick lt) Register(lt);
        if (obj is IUnscaledTick ut) Register(ut);
    }

    public static void UnregisterAll(object obj)
    {
        if (obj is ITick t) Unregister(t);
        if (obj is ILateTick lt) Unregister(lt);
        if (obj is IUnscaledTick ut) Unregister(ut);
    }

    // ---------------- Flush helpers ----------------

    private void FlushTicks()
    {
        if (!_dirtyTicks) return;

        for (int i = 0; i < _pendingRemoveTicks.Count; i++)
            _ticks.Remove(_pendingRemoveTicks[i]);
        _pendingRemoveTicks.Clear();

        for (int i = 0; i < _pendingAddTicks.Count; i++)
            _ticks.Add(_pendingAddTicks[i]);
        _pendingAddTicks.Clear();

        _dirtyTicks = false;
    }

    private void FlushLateTicks()
    {
        if (!_dirtyLateTicks) return;

        for (int i = 0; i < _pendingRemoveLateTicks.Count; i++)
            _lateTicks.Remove(_pendingRemoveLateTicks[i]);
        _pendingRemoveLateTicks.Clear();

        for (int i = 0; i < _pendingAddLateTicks.Count; i++)
            _lateTicks.Add(_pendingAddLateTicks[i]);
        _pendingAddLateTicks.Clear();

        _dirtyLateTicks = false;
    }

    private void FlushUnscaledTicks()
    {
        if (!_dirtyUnscaledTicks) return;

        for (int i = 0; i < _pendingRemoveUnscaledTicks.Count; i++)
            _unscaledTicks.Remove(_pendingRemoveUnscaledTicks[i]);
        _pendingRemoveUnscaledTicks.Clear();

        for (int i = 0; i < _pendingAddUnscaledTicks.Count; i++)
            _unscaledTicks.Add(_pendingAddUnscaledTicks[i]);
        _pendingAddUnscaledTicks.Clear();

        _dirtyUnscaledTicks = false;
    }

    // ---------------- Update loops ----------------

    private void Update()
    {
        FlushTicks();

        float dt = Time.deltaTime * _gameSpeed;
        // Backward loop: cheap, tolerant of Unregister happening mid-iteration
        // (won't skip/crash even though actual removal is deferred to next flush).
        for (int i = _ticks.Count - 1; i >= 0; i--)
        {
            _ticks[i]?.Tick(dt);
        }

        FlushUnscaledTicks();
        float unscaledDt = Time.deltaTime;
        for (int i = _unscaledTicks.Count - 1; i >= 0; i--)
        {
            _unscaledTicks[i]?.UnscaledTick(unscaledDt);
        }
    }

    private void LateUpdate()
    {
        FlushLateTicks();

        float dt = Time.deltaTime * _gameSpeed;
        for (int i = _lateTicks.Count - 1; i >= 0; i--)
        {
            _lateTicks[i]?.LateTick(dt);
        }
    }
}

/// <summary>
/// Implement to receive scaled per-frame ticks (affected by GameSpeed).
/// </summary>
public interface ITick
{
    void Tick(float deltaTime);
}

/// <summary>
/// Implement to receive scaled late ticks (affected by GameSpeed), fired after LateUpdate.
/// </summary>
public interface ILateTick
{
    void LateTick(float deltaTime);
}

/// <summary>
/// Implement to receive unscaled ticks (NOT affected by GameSpeed, uses real Time.deltaTime).
/// </summary>
public interface IUnscaledTick
{
    void UnscaledTick(float deltaTime);
}