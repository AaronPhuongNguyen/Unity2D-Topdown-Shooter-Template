using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Static entry point: DamagePopupManager.Show(...) from anywhere, no
/// reference-passing needed. Handles world-to-screen conversion and pooling;
/// actual per-popup animation lives in DamagePopup.
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
    [SerializeField] private Canvas canvas;          // screen-space canvas popups are parented under
    [SerializeField] private DamagePopup popupPrefab;
    [SerializeField] private Camera worldCamera;      // camera used for WorldToScreenPoint; falls back to Camera.main

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color critColor = new Color(1f, 0.6f, 0f);
    [SerializeField] private Color healColor = new Color(0.4f, 1f, 0.4f);
    #endregion

    #region Cache
    private Camera Cam => worldCamera != null ? worldCamera : Camera.main;
    #endregion

    #region Static API (call from anywhere)
    public static void Show(Vector3 worldPos, float damage) =>
        instance?.SpawnPopup(worldPos, FormatNumber(damage), instance.normalColor);

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

        if (obj.TryGetComponent(out DamagePopup popup))
            popup.Init(screenPos, text, color);
    }

    private static string FormatNumber(float value) => Mathf.RoundToInt(value).ToString();
    #endregion
}