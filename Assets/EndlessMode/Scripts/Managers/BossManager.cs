using Server;
using UnityEngine;

[DefaultExecutionOrder(-4)]
public class BossManager : MonoBehaviour
{
    #region Singleton
    public static BossManager instance { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }
        instance = this;
    }
    #endregion

    #region Config
    [Header("Boss Wave Settings")]
    [Tooltip("A boss wave occurs every N waves.")]
    public int bossWaveInterval = 10;

    [Tooltip("Wave duration (seconds) used on boss waves. Overrides DomainManager.WaveDuration.")]
    public float bossWaveDuration = 300f;

    [Tooltip("How long zombies stay suppressed after a boss wave begins.")]
    public float spawnSuppressionDuration = 300f;
    #endregion

    #region Runtime State
    public bool IsBossWave { get; private set; }
    public bool SpawningSuppressed { get; private set; }
    public float SuppressionTimeLeft { get; private set; }
    public GameObject CurrentBoss { get; private set; }

    private int lastWaveHandled = -1;
    #endregion

    #region Init
    private void OnEnable()
    {
        EventBus.OnNewWave += OnNewWave;
        EventBus.OnGameRestart += OnGameRestart;
        EventBus.OnGameOver += OnGameOver;
    }

    private void OnDisable()
    {
        EventBus.OnNewWave -= OnNewWave;
        EventBus.OnGameRestart -= OnGameRestart;
        EventBus.OnGameOver -= OnGameOver;
    }
    #endregion

    #region Wave Handling
    private void OnNewWave(int wave)
    {
        if (wave == lastWaveHandled) return;
        lastWaveHandled = wave;

        if (!IsBossWaveNumber(wave))
        {
            // Normal wave: lift any leftover suppression.
            IsBossWave = false;
            EndSuppression();
            return;
        }

        BeginBossWave(wave);
    }

    public bool IsBossWaveNumber(int wave)
    {
        return bossWaveInterval > 0 && wave > 0 && wave % bossWaveInterval == 0;
    }

    private void BeginBossWave(int wave)
    {
        Debug.Log($"BossManager: Wave {wave} is a BOSS wave. Suppressing spawns for {spawnSuppressionDuration}s.");

        IsBossWave = true;
        CurrentBoss = null;

        // 1. Suppress normal zombie spawning.
        StartSuppression();

        // 2. Extend this wave's timer. The boss counts in RemainingEnemy
        //    (HiveBrain -> Zombrain.SpawnObject -> HandleSpawn), so the wave
        //    cannot be cleared until it dies.
        if (DomainManager.instance != null)
        {
            DomainManager.instance.SecondBeforeNextWave = bossWaveDuration;
            DomainManager.instance.PreparingTime = 0f;
        }

        // 3. BossSpawner (subscribed to this event) picks from the database,
        //    spawns the boss and calls RegisterBoss() synchronously.
        EventBus.RaiseBossWaveStarted(wave);

        if (CurrentBoss == null)
        {
            Debug.LogError("BossManager: No boss was spawned (is a BossSpawner with a valid database in the scene?). Cancelling boss wave.");
            EndBossWave();
        }
    }

    private void EndBossWave()
    {
        if (!IsBossWave) return;
        IsBossWave = false;
        EndSuppression();
        EventBus.RaiseBossWaveEnded();
    }
    #endregion

    #region Suppression
    private void StartSuppression()
    {
        SpawningSuppressed = true;
        SuppressionTimeLeft = spawnSuppressionDuration;
    }

    private void EndSuppression()
    {
        SpawningSuppressed = false;
        SuppressionTimeLeft = 0f;
    }

    private void Update()
    {
        if (!SpawningSuppressed) return;

        SuppressionTimeLeft -= Time.deltaTime;
        if (SuppressionTimeLeft <= 0f) EndBossWave();
    }
    #endregion

    #region Restart / GameOver
    private void OnGameRestart()
    {
        IsBossWave = false;
        CurrentBoss = null;
        lastWaveHandled = -1;
        EndSuppression();
    }

    private void OnGameOver() => EndSuppression();
    #endregion

    #region Public API
    /// <summary>Called by BossSpawner right after it spawns the boss.</summary>
    public void RegisterBoss(GameObject boss) => CurrentBoss = boss;

    /// <summary>
    /// Called from the boss's death handler (BigMeat.Despawn) to end the
    /// boss wave early, before the suppression timer expires.
    /// </summary>
    public void NotifyBossKilled(GameObject boss)
    {
        if (boss != null && boss != CurrentBoss) return;
        CurrentBoss = null;

        if (!IsBossWave) return;

        // Let the domain progress past the boss wave immediately.
        if (DomainManager.instance != null)
            DomainManager.instance.SecondBeforeNextWave = 0f;

        EndBossWave();
    }
    #endregion

    #region Debugger
    [ContextMenu("Force Boss Wave")]
    private void ForceBossWave()
    {
        BeginBossWave(DomainManager.instance != null ? DomainManager.instance.CurrentWave : 10);
    }
    #endregion
}