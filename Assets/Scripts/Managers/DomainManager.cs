using Server;
using UnityEngine;

[DefaultExecutionOrder(-5)]
public class DomainManager : MonoBehaviour
{
    #region Singleton
    public static DomainManager instance { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            // Destroy() is deferred to end-of-frame, so without disabling
            // first this duplicate's OnEnable() would still run this frame
            // and double-subscribe to EventBus.OnPlayerRespawn.
            enabled = false;
            Destroy(gameObject);
            return;
        }

        instance = this;
        PrepareTheMatch();
    }
    #endregion

    #region Init
    private void Start() => StartTheGame();

    private void OnEnable() => EventBus.OnPlayerRespawn += Reboot;
    private void OnDisable() => EventBus.OnPlayerRespawn -= Reboot;
    #endregion

    #region Cycle
    private void Update() => RunGame();
    #endregion

    #region Map Settings
    [Header("Map Settings")]
    public Vector2 mapCenter = Vector2.zero;
    public Vector2 mapSize = new Vector2(100f, 100f);

    public Rect MapBounds => new Rect(
        mapCenter.x - mapSize.x * 0.5f,
        mapCenter.y - mapSize.y * 0.5f,
        mapSize.x,
        mapSize.y
    );

    public Vector2 MapMin => new Vector2(MapBounds.xMin, MapBounds.yMin);
    public Vector2 MapMax => new Vector2(MapBounds.xMax, MapBounds.yMax);

    // Only a fraction of the map is walkable, leaving a margin so the
    // player doesn't see the blue void at the edges.
    [Range(0f, 1f)]
    public float walkablePercent = 0.5f;

    public Vector2 ClampToMap(Vector2 pos)
    {
        Vector2 min = Vector2.Lerp(mapCenter, MapMin, walkablePercent);
        Vector2 max = Vector2.Lerp(mapCenter, MapMax, walkablePercent);
        pos.x = Mathf.Clamp(pos.x, min.x, max.x);
        pos.y = Mathf.Clamp(pos.y, min.y, max.y);
        return pos;
    }
    #endregion

    #region Seed Settings
    [Header("Seed Settings")]
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int seed = 0;
    public int Seed => seed;

    private void InitializeSeed()
    {
        seed = useRandomSeed ? RNG.GetInt(10000, int.MaxValue) : seed;
        RNG.SetSeed((uint)seed);
    }

    [ContextMenu("Reroll Seed")]
    public void RerollSeed()
    {
        seed = RNG.GetInt(10000, int.MaxValue);
        RNG.SetSeed((uint)seed);
    }

    public void ApplyCurrentSeed() => RNG.SetSeed((uint)seed);
    #endregion

    #region Wave / Match State
    [Header("Wave")]
    public int RemainingEnemy;
    public int Killed;
    public int CurrentWave = 0;
    public float CurrentDifficulty=0;
    public const float WaveDuration = 90f;

    [Header("Runtime debug")]
    public float SecondBeforeNextWave;
    public float PreparingTime;

    private bool isGameRunning;
    private bool isNewWave = false;

    private void PrepareTheMatch()
    {
        InitializeSeed();

        var generator = FindFirstObjectByType<TilemapGenerator>();
        if (generator == null)
        {
            Debug.LogError("DomainManager: No TilemapGenerator found in scene - cannot generate map.");
            return;
        }
        generator.Generate();
    }

    private void Reboot()
    {
        Time.timeScale = 1f;
        isNewWave = false;
        RemainingEnemy = 0;
        Killed = 0;
        CurrentWave = 0;
        PreparingTime = 5f;
        SecondBeforeNextWave = 0f;
        CurrentDifficulty = 0f;
    }

    public void StartTheGame()
    {
        Time.timeScale = 1f;
        isGameRunning = true;
        PreparingTime = 5f;
    }

    private void NextWave()
    {
        if (isNewWave)
        {
            PreparingTime = 5f;
            SecondBeforeNextWave = 0f;
            EventBus.RaiseWaveCleared();
            isNewWave = false;
            return;
        }

        if (PreparingTime > 0) return;

        SecondBeforeNextWave = WaveDuration;
        CurrentWave++;
        CurrentDifficulty = CurrentWave / 4f;
        isNewWave = true;
        EventBus.RaiseNewWave(CurrentWave);
    }

    private void RunGame()
    {
        if (!isGameRunning) return;

        if (SecondBeforeNextWave > 0) SecondBeforeNextWave -= Time.deltaTime;
        if (PreparingTime > 0) PreparingTime -= Time.deltaTime;

        if (RemainingEnemy <= 0 || SecondBeforeNextWave <= 0)
            NextWave();
    }
    #endregion
}