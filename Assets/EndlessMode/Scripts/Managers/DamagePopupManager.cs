using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Static entry point: DamagePopupManager.Show(...) from anywhere.
/// Owns its pool and updates every popup in ONE Update (no per-popup scripts ticking).
/// Rapid calls fan out in a fixed rhythm (up, right, left, wider right, wider left...).
/// Numbers are shown in short form: 1.5K, 2.3M, 4.1B, 1.2T...
/// </summary>
public class DamagePopupManager : MonoBehaviour
{
    #region Singleton
    public static DamagePopupManager instance { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        Prewarm();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
    #endregion

    #region Inspector
    [SerializeField] private Canvas canvas;
    [SerializeField] private DamagePopup popupPrefab;
    [SerializeField] private Camera worldCamera;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color critColor = new Color(1f, 0.6f, 0f);
    [SerializeField] private Color healColor = new Color(0.4f, 1f, 0.4f);

    [Header("Pool")]
    [SerializeField] private int prewarm = 32;
    [Tooltip("Max popups on screen. Over this, the oldest one is recycled.")]
    [SerializeField] private int maxActive = 80;

    [Header("Scale")]
    [Tooltip("Scale a popup starts at the moment it is shown.")]
    [SerializeField] private float startScale = 1.6f;
    [Tooltip("Scale it shrinks to by the end of its life.")]
    [SerializeField] private float endScale = 0.4f;
    [SerializeField] private float critScaleMultiplier = 1.5f;

    [Header("Life")]
    [SerializeField] private float lifetime = 0.9f;
    [Tooltip("0..1: fraction of the life when fading starts.")]
    [SerializeField, Range(0f, 0.95f)] private float fadeStart = 0.45f;

    [Header("Jump (canvas units)")]
    [SerializeField] private float jumpSpeed = 330f;
    [SerializeField] private float gravity = 900f;
    [SerializeField] private float horizontalDrag = 1.5f;

    [Header("Rhythm / Fan")]
    [Tooltip("Calls within this time (s) of each other count as one burst.")]
    [SerializeField] private float comboWindow = 0.25f;
    [Tooltip("Angle between consecutive popups in a burst (degrees).")]
    [SerializeField] private float angleStep = 14f;
    [SerializeField] private float maxAngle = 75f;
    [Tooltip("Small random angle / speed variation so it isn't robotic.")]
    [SerializeField] private float angleNoise = 4f;
    [SerializeField, Range(0f, 0.5f)] private float speedNoise = 0.1f;
    [Tooltip("Tiny random start offset (canvas units).")]
    [SerializeField] private float startJitter = 8f;
    #endregion

    #region Runtime
    private readonly Stack<DamagePopup> pool = new Stack<DamagePopup>();
    private readonly List<DamagePopup> active = new List<DamagePopup>();
    private RectTransform canvasRect;
    private int comboIndex;
    private float lastSpawnTime = -999f;

    private Camera Cam => worldCamera != null ? worldCamera : Camera.main;
    #endregion

    #region Static API (call from anywhere)
    public static void Show(Vector3 worldPos, float damage) =>
        instance?.SpawnPopup(worldPos, FormatNumber(damage), instance.normalColor, 1f);

    public static void Show(Vector3 worldPos, float damage, Color color) =>
        instance?.SpawnPopup(worldPos, FormatNumber(damage), color, 1f);

    public static void Show(Vector3 worldPos, float damage, bool isCrit)
    {
        if (instance == null) return;
        instance.SpawnPopup(worldPos, FormatNumber(damage),
            isCrit ? instance.critColor : instance.normalColor,
            isCrit ? instance.critScaleMultiplier : 1f);
    }

    public static void ShowHeal(Vector3 worldPos, float amount) =>
        instance?.SpawnPopup(worldPos, "+" + FormatNumber(amount), instance.healColor, 1f);

    public static void Show(Vector3 worldPos, string text) =>
        instance?.SpawnPopup(worldPos, text, instance.normalColor, 1f);

    public static void Show(Vector3 worldPos, string text, Color color) =>
        instance?.SpawnPopup(worldPos, text, color, 1f);
    #endregion

    #region Pool
    private void Prewarm()
    {
        if (popupPrefab == null || canvas == null) return;
        canvasRect = canvas.transform as RectTransform;

        for (int i = 0; i < prewarm; i++)
            pool.Push(CreatePopup());
    }

    private DamagePopup CreatePopup()
    {
        DamagePopup p = Instantiate(popupPrefab, canvas.transform, false);
        p.Build();
        p.gameObject.SetActive(false);
        return p;
    }

    private DamagePopup Rent()
    {
        // Never reuse a popup that is still alive: take a free one, or make a new one.
        DamagePopup p = pool.Count > 0 ? pool.Pop() : CreatePopup();
        p.gameObject.SetActive(true);
        return p;
    }

    private void Return(DamagePopup p)
    {
        p.gameObject.SetActive(false);
        pool.Push(p);
    }
    #endregion

    #region Spawn
    private void SpawnPopup(Vector3 worldPos, string text, Color color, float scaleMul)
    {
        if (popupPrefab == null || canvas == null) return;
        Camera cam = Cam;
        if (cam == null) return;
        if (canvasRect == null) canvasRect = canvas.transform as RectTransform;

        Vector3 sp = cam.WorldToScreenPoint(worldPos);
        if (sp.z < 0f) return; // behind the camera

        Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, uiCam, out Vector2 local))
            return;

        // Over the cap: recycle the oldest popup instead of dropping the new one.
        if (active.Count >= maxActive)
        {
            Return(active[0]);
            active.RemoveAt(0);
        }

        // ---- rhythm: burst index decides the fan angle ----
        float now = Time.unscaledTime;
        if (now - lastSpawnTime > comboWindow) comboIndex = 0;
        lastSpawnTime = now;

        int i = comboIndex++;
        float side = (i % 2 == 0) ? 1f : -1f;
        float step = (i + 1) / 2;                 // 0,1,1,2,2,3,3...
        float angle = Mathf.Clamp(side * step * angleStep, -maxAngle, maxAngle);
        angle += UnityEngine.Random.Range(-angleNoise, angleNoise);

        float rad = angle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)); // 0 deg = straight up
        float speed = jumpSpeed * (1f + UnityEngine.Random.Range(-speedNoise, speedNoise));

        Vector2 start = local + new Vector2(
            UnityEngine.Random.Range(-startJitter, startJitter),
            UnityEngine.Random.Range(-startJitter, startJitter));

        DamagePopup p = Rent();
        p.Setup(start, dir * speed, text, color, startScale * scaleMul, endScale * scaleMul, lifetime);
        p.transform.SetAsLastSibling(); // newest on top
        active.Add(p);
    }
    #endregion

    #region Update (single loop for every popup)
    private void Update()
    {
        int count = active.Count;
        if (count == 0) return;

        float dt = Time.unscaledDeltaTime;

        // Compact in place: keeps order (oldest first), O(n), no allocations.
        int write = 0;
        for (int read = 0; read < count; read++)
        {
            DamagePopup p = active[read];
            if (p.Tick(dt, gravity, horizontalDrag, fadeStart))
            {
                active[write++] = p;
            }
            else
            {
                Return(p);
            }
        }
        if (write < count) active.RemoveRange(write, count - write);
    }
    #endregion

    #region Number Formatting
    private static readonly string[] Suffixes =
        { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp" };

    /// <summary>
    /// 999 -> "999", 1500 -> "1.5K", 2300000 -> "2.3M", 4.1e9 -> "4.1B".
    /// Up to 2 decimals, trailing zeros trimmed.
    /// </summary>
    public static string FormatNumber(float value)
    {
        if (float.IsNaN(value)) return "0";
        if (float.IsInfinity(value)) return value > 0f ? "∞" : "-∞";

        bool negative = value < 0f;
        float abs = Mathf.Abs(value);

        if (abs < 1000f)
            return (negative ? "-" : "") + Mathf.RoundToInt(abs).ToString(CultureInfo.InvariantCulture);

        int tier = 0;
        while (abs >= 1000f && tier < Suffixes.Length - 1)
        {
            abs /= 1000f;
            tier++;
        }

        // Rounding can push 999.999K to "1000K": bump it up to the next tier.
        abs = (float)Math.Round(abs, 2);
        if (abs >= 1000f && tier < Suffixes.Length - 1)
        {
            abs /= 1000f;
            tier++;
        }

        string num = abs.ToString("0.##", CultureInfo.InvariantCulture);
        return (negative ? "-" : "") + num + Suffixes[tier];
    }
    #endregion
}