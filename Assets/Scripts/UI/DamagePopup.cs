using UnityEngine;
using TMPro;

/// <summary>
/// Single damage popup instance: pooled, screen-space UI text with a
/// simple jump-then-fall-then-fade animation. Position is converted from
/// world space to screen space once at spawn time (not re-tracked every
/// frame) - cheap, and the small screen-space arc animation is applied on
/// top of that fixed starting point.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DamagePopup : MonoBehaviour, ITick
{
    #region Inspector
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private float lifetime = 0.8f;
    [SerializeField] private float jumpHeight = 40f;
    [SerializeField] private float jumpDuration = 0.15f;
    [SerializeField] private float fallDistance = 25f;
    [SerializeField] private AnimationCurve jumpCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    #endregion

    #region Cache
    private RectTransform rt;
    private Vector2 startAnchoredPos;
    private float age;
    private Color baseColor;
    #endregion

    #region Lifecycle
    private void Awake()
    {
        rt = (RectTransform)transform;
        if (label == null) label = GetComponentInChildren<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }
    #endregion

    #region Public API
    /// <summary>
    /// Called by DamagePopupManager right after fetching this from the pool.
    /// screenPos: already-converted screen-space position (see manager).
    /// </summary>
    public void Init(Vector2 screenPos, string text, Color color)
    {
        age = 0f;
        baseColor = color;

        rt.position = screenPos;
        startAnchoredPos = rt.anchoredPosition;

        label.text = text;
        label.color = color;
        rt.localScale = Vector3.one;
    }

    public void Tick(float delta)
    {
        age += delta;

        float t = age / lifetime;
        if (t >= 1f)
        {
            Despawn();
            return;
        }

        // Jump phase (first jumpDuration seconds): curved rise.
        // Fall phase (remainder): straight linear drop, standard "number pop" feel.
        float jumpT = Mathf.Clamp01(age / jumpDuration);
        float jumpOffset = jumpCurve.Evaluate(jumpT) * jumpHeight;

        float fallT = Mathf.Clamp01((age - jumpDuration) / Mathf.Max(0.01f, lifetime - jumpDuration));
        float fallOffset = fallT * fallDistance;

        rt.anchoredPosition = startAnchoredPos + new Vector2(0f, jumpOffset - fallOffset);

        // Fade out over the last 40% of lifetime only, so it stays fully
        // visible/readable at first instead of fading the whole time.
        float fadeT = Mathf.Clamp01((t - 0.6f) / 0.4f);
        Color c = baseColor;
        c.a = Mathf.Lerp(baseColor.a, 0f, fadeT);
        label.color = c;
    }
    #endregion

    #region Despawn
    private void Despawn()
    {
        if (PoolingSystem.instance != null)
            PoolingSystem.instance.RemoveToPool(gameObject);
        else
            gameObject.SetActive(false);
    }
    #endregion
}