using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TargetHP : MonoBehaviour, ITick
{
    [Header("UI")]
    [SerializeField] private GameObject root;          // child panel, toggled on/off
    [SerializeField] private Slider hpBar;             // front bar
    [SerializeField] private Slider slerpBar;          // trailing bar, drawn behind hpBar
    [SerializeField] private TMP_Text barCountText;    // "x7"

    [Header("Settings")]
    [SerializeField] private float hpPerBar = 200f;
    [SerializeField] private float slerpDelay = 0.1f;  // wait before the trail starts catching up
    [SerializeField] private float slerpSpeed = 5f;    // trail catch-up speed
    [SerializeField] private float frontSpeed = 14f;   // front bar speed (also used when switching targets)
    [SerializeField] private float hideAfter = 1f;     // seconds without a hit before hiding

    private UnitAttribute target;
    private float frontValue;   // displayed front value, in bars
    private float slerpValue;   // displayed trailing value, in bars
    private float slerpTimer;
    private float hideTimer;
    private bool visible;

    private void Awake()
    {
        SetupSlider(hpBar);
        SetupSlider(slerpBar);
    }
    void Start() => TickSystem.RegisterAll(this);

    private static void SetupSlider(Slider s)
    {
        if (s == null) return;
        s.minValue = 0f;
        s.maxValue = 1f;
        s.interactable = false;
    }

    private void OnEnable()
    {
        EventBus.OnDamageDealt += OnDamageDealt;
        // TODO: register with your tick system here, e.g. TickManager.instance.Register(this);
        SetVisible(false);
    }

    private void OnDisable()
    {
        EventBus.OnDamageDealt -= OnDamageDealt;
        // TODO: unregister from your tick system here
    }

    private void OnDamageDealt(UnitAttribute attacker, UnitAttribute victim, float damage)
    {
        var pm = PlayerManager.instance;
        if (pm == null || attacker != pm.attribute) return;
        if (victim == null) return;

        if (!visible)
        {
            // Panel was hidden, nothing on screen to slide from, so start at the real value
            frontValue = slerpValue = Bars(victim.HP_Current + damage);
            slerpTimer = slerpDelay;
        }
        else if (victim != target)
        {
            // Switching targets: keep the current displayed values and let them slide.
            // No trail delay, so the trail follows the front bar right away.
            slerpTimer = 0f;
        }
        else
        {
            slerpTimer = slerpDelay;
        }

        target = victim;
        hideTimer = hideAfter;
        SetVisible(true);
    }

    public void Tick(float dt)
    {
        if (!visible || target == null) return;

        float current = Bars(Mathf.Max(0f, target.HP_Current));

        // Front bar eases toward the real HP (this is what makes target switches smooth)
        frontValue = Smooth(frontValue, current, frontSpeed, dt);

        // Trail waits, then eases toward the front bar
        if (slerpTimer > 0f) slerpTimer -= dt;
        else slerpValue = Smooth(slerpValue, frontValue, slerpSpeed, dt);

        // The trail never sits below the front bar
        if (slerpValue < frontValue) slerpValue = frontValue;

        Refresh();

        hideTimer -= dt;
        if (hideTimer <= 0f)
        {
            target = null;
            SetVisible(false);
        }
    }

    private void Refresh()
    {
        int barCount = Mathf.CeilToInt(frontValue - 0.001f);

        if (hpBar != null)
            hpBar.SetValueWithoutNotify(FillOf(frontValue, barCount));

        if (slerpBar != null)
        {
            int slerpIndex = Mathf.CeilToInt(slerpValue - 0.001f);
            slerpBar.SetValueWithoutNotify(slerpIndex > barCount ? 1f : FillOf(slerpValue, barCount));
        }

        if (barCountText != null)
            barCountText.text = barCount > 1 ? $"x{barCount}" : string.Empty;
    }

    // Frame-rate independent exponential smoothing
    private static float Smooth(float from, float to, float speed, float dt)
        => Mathf.Lerp(from, to, 1f - Mathf.Exp(-speed * dt));

    private static float FillOf(float bars, int topBarCount)
    {
        if (topBarCount <= 0) return 0f;
        return Mathf.Clamp01(bars - (topBarCount - 1));
    }

    private float Bars(float hp) => hp / hpPerBar * (1 + DomainManager.instance.CurrentDifficulty * DomainManager.instance.CurrentDifficulty);

    private void SetVisible(bool v)
    {
        visible = v;
        if (root != null) root.SetActive(v);
    }
}