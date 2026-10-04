using Server;
using UnityEngine;

/// <summary>
/// Static entry point: DamagePopupManager.Show(...) from anywhere, no
/// reference-passing needed. Handles world-to-screen conversion, pooling,
/// and random jitter so multiple near-simultaneous popups on the same
/// target (e.g. shotgun pellets) visibly fan out instead of overlapping.
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

    [Header("Jitter")]
    [Tooltip("Max random screen-space offset radius (pixels) applied per popup, so simultaneous hits on the same spot visibly fan out instead of stacking.")]
    [SerializeField] private float jitterRadius = 25f;
    #endregion

    #region Cache
    private Camera Cam => worldCamera != null ? worldCamera : Camera.main;
    #endregion

    #region Static API (call from anywhere)
    public static void Show(Vector3 worldPos, float damage) =>
        instance?.SpawnPopup(worldPos, FormatNumber(damage), instance.normalColor);

    public static void Show(Vector3 worldPos, float damage, Color color) =>
        instance?.SpawnPopup(worldPos, FormatNumber(damage), color);

    public static void Show(Vector3 worldPos, float damage, bool isCrit) =>
        instance?.SpawnPopup(worldPos, FormatNumber(damage), isCrit ? instance.critColor : instance.normalColor);

    public static void ShowHeal(Vector3 worldPos, float amount) =>
        instance?.SpawnPopup(worldPos, $"+{FormatNumber(amount)}", instance.healColor);

    public static void Show(Vector3 worldPos, string text) =>
        instance?.SpawnPopup(worldPos, text, instance.normalColor);

    public static void Show(Vector3 worldPos, string text, Color color) =>
        instance?.SpawnPopup(worldPos, text, color);
    #endregion

    #region Internal
    private void SpawnPopup(Vector3 worldPos, string text, Color color)
    {
        if (popupPrefab == null || canvas == null) return;
        if (Cam == null) return;

        GameObject obj = PoolingSystem.instance != null
            ? PoolingSystem.instance.GetFromPool(popupPrefab.gameObject)
            : Instantiate(popupPrefab.gameObject);

        if (obj == null) return;

        obj.transform.SetParent(canvas.transform, false);

        Vector2 screenPos = Cam.WorldToScreenPoint(worldPos);

        // Random screen-space jitter via project RNG (seeded, deterministic
        // with the match seed) so multiple same-frame popups on the same
        // target - shotgun pellets, multi-hit AoE, etc. - fan out visibly
        // instead of rendering exactly on top of each other.
        screenPos += RNG.GetInsideCircle(jitterRadius);

        if (obj.TryGetComponent(out DamagePopup popup))
            popup.Init(screenPos, text, color);
    }

    private static string FormatNumber(float value) => Mathf.RoundToInt(value).ToString();
    #endregion
}