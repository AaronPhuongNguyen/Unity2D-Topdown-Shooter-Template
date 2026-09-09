using Server;
using System.Collections.Generic;
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

    private void OnEnable()
    {
        EventBus.OnGameRestart += Reboot;
        EventBus.OnGameOver += StopDomain;
    }
    private void OnDisable()
    {
        EventBus.OnGameRestart -= Reboot;
        EventBus.OnGameOver -= StopDomain;
    }
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

    public Vector2 WalkableMin => Vector2.Lerp(mapCenter, MapMin, walkablePercent);
    public Vector2 WalkableMax => Vector2.Lerp(mapCenter, MapMax, walkablePercent);
    #endregion

    #region Border
    [Header("Border")]
    [Tooltip("Prefab with a SpriteRenderer using a plain 1x1 unit sprite (e.g. white square). Color/alpha (black, 50%) should be set on the prefab itself.")]
    [SerializeField] private SpriteRenderer borderPrefab;
    [Tooltip("Optional parent to keep spawned borders organized in the hierarchy.")]
    [SerializeField] private Transform borderContainer;

    private readonly List<SpriteRenderer> spawnedBorders = new List<SpriteRenderer>();

    /// <summary>
    /// Spawns 4 border strips covering the gap between the walkable area and the
    /// full map bounds — top, bottom, left, right — so the out-of-bounds zone reads
    /// as visually walled off. Assumes borderPrefab's sprite is exactly 1x1 unit,
    /// since strips are sized purely via transform.localScale.
    /// </summary>
    private void SpawnBorders()
    {
        if (borderPrefab == null)
        {
            Debug.LogWarning("DomainManager: No border prefab assigned — skipping border spawn.");
            return;
        }

        ClearBorders();

        Vector2 mapMin = MapMin;
        Vector2 mapMax = MapMax;
        Vector2 walkMin = WalkableMin;
        Vector2 walkMax = WalkableMax;

        float leftWidth = walkMin.x - mapMin.x;
        float rightWidth = mapMax.x - walkMax.x;
        float topHeight = mapMax.y - walkMax.y;
        float bottomHeight = walkMin.y - mapMin.y;

        // Left strip: spans full map height, sits between mapMin.x and walkMin.x
        SpawnBorderStrip(
            center: new Vector2(mapMin.x + leftWidth * 0.5f, mapCenter.y),
            size: new Vector2(leftWidth, mapSize.y));

        // Right strip: spans full map height, sits between walkMax.x and mapMax.x
        SpawnBorderStrip(
            center: new Vector2(mapMax.x - rightWidth * 0.5f, mapCenter.y),
            size: new Vector2(rightWidth, mapSize.y));

        // Top strip: spans full map width, sits between walkMax.y and mapMax.y
        SpawnBorderStrip(
            center: new Vector2(mapCenter.x, mapMax.y - topHeight * 0.5f),
            size: new Vector2(mapSize.x, topHeight));

        // Bottom strip: spans full map width, sits between mapMin.y and walkMin.y
        SpawnBorderStrip(
            center: new Vector2(mapCenter.x, mapMin.y + bottomHeight * 0.5f),
            size: new Vector2(mapSize.x, bottomHeight));
    }

    private void SpawnBorderStrip(Vector2 center, Vector2 size)
    {
        if (size.x <= 0f || size.y <= 0f) return; // walkablePercent == 1 means no border gap to fill

        SpriteRenderer border = Instantiate(borderPrefab, borderContainer);
        border.transform.position = new Vector3(center.x, center.y, border.transform.position.z);

        // Don't assume the sprite is 1x1 world unit — compute the actual native size
        // from the sprite's bounds (in local/unscaled space) so this works regardless
        // of the sprite's Pixels Per Unit import setting. This is what was causing
        // borders to appear huge and centered: a mismatched PPU made the "1x1 unit"
        // assumption wrong, sometimes by 100x or more.
        Vector2 nativeSize = border.sprite != null ? (Vector2)border.sprite.bounds.size : Vector2.one;
        if (nativeSize.x <= 0f) nativeSize.x = 1f;
        if (nativeSize.y <= 0f) nativeSize.y = 1f;

        // Also account for any scale already baked into the parent container, since
        // localScale multiplies with the parent's scale to produce the final world size.
        Vector3 parentScale = borderContainer != null ? borderContainer.lossyScale : Vector3.one;
        if (parentScale.x == 0f) parentScale.x = 1f;
        if (parentScale.y == 0f) parentScale.y = 1f;

        border.transform.localScale = new Vector3(
            (size.x / nativeSize.x) / parentScale.x,
            (size.y / nativeSize.y) / parentScale.y,
            1f);

        spawnedBorders.Add(border);
    }

    private void ClearBorders()
    {
        for (int i = spawnedBorders.Count - 1; i >= 0; i--)
        {
            if (spawnedBorders[i] != null) Destroy(spawnedBorders[i].gameObject);
        }
        spawnedBorders.Clear();
    }
    #endregion

    #region Seed Settings
    [Header("Seed Settings")]
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int seed = 0;
    public int Seed => seed;
    public PlayerManager PM => PlayerManager.instance;

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
    public int Currency;
    public int CurrentWave = 0;
    public int MaxEnemyPerWave = 1000;
    public float CurrentDifficulty = 0;
    public const float WaveDuration = 90f;
    public const float PreparingTimeDefault = 5f;

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

        SpawnBorders();
    }
    private void StopDomain()
    {
        isGameRunning = false;
        Time.timeScale = 0;
    }
    private void Reboot()
    {
        isGameRunning = true;
        Time.timeScale = 1f;
        RemainingEnemy = 0;
        Killed = 0;
        Currency = 0;
        PreparingTime = PreparingTimeDefault;
        SecondBeforeNextWave = 0f;
        CurrentWave = 0;
        HandleWave(1);
    }

    public void HandleSpawn(ZomPackage p)
    {
        RemainingEnemy++;
        AddCurrency(p.CurrencyAtKill * 0.5f);
    }
    public void HandleKill(ZomPackage p)
    {
        RemainingEnemy--;
        Killed++;
        AddCurrency(p.CurrencyAtKill);
    }
    public void AddCurrency(float v) => Currency += Mathf.FloorToInt(v);
    public void CostCurrency(float v) => Currency -= Mathf.CeilToInt(v);
    public void StartTheGame()
    {
        Time.timeScale = 1f;
        isGameRunning = true;
        PreparingTime = PreparingTimeDefault;
        HandleWave(1);
    }

    private void HandleWave(int wave)
    {
        CurrentWave+=wave;
        CurrentDifficulty = CurrentWave / 4f;
        PreparingTime = PreparingTimeDefault;
        SecondBeforeNextWave = 0f;
        isNewWave = false;
        MaxEnemyPerWave = Mathf.RoundToInt(1000 * RNG.GetFloat(0.5f, 1.5f));
        AddCurrency(Killed + 100);
    }
    private void NextWave()
    {
        if (isNewWave)
        {
            HandleWave(1);
            EventBus.RaiseWaveCleared();
            return;
        }

        if (PreparingTime > 0) return;

        SecondBeforeNextWave = WaveDuration;
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

    #region Debugger
    [ContextMenu("Add 10 wave")]
    private void AddWave() => HandleWave(10);
    #endregion
}