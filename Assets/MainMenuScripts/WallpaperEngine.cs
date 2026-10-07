using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple live wallpaper:
///  - Background slowly pans to random points around the canvas (camera ping-pong feel).
///  - Sprites fall over it at random positions.
///  - ChangeBackground() / SetBackground(i) swap the background instantly (hook to Buttons).
/// Mobile-friendly: fixed pool, no per-frame allocations, raycasts off, optional frame cap.
/// </summary>
public class WallpaperEngine : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private RectTransform particleRoot;     // stretch-fill child of the Canvas, above the background

    [Header("Background")]
    [SerializeField] private Sprite[] backgrounds;
    [SerializeField] private int defaultBackground = 0;
    [Tooltip("Background is drawn this much bigger than the canvas so it has room to pan. 1.2 = 20% bigger.")]
    [SerializeField, Min(1.01f)] private float overscan = 1.2f;
    [Tooltip("Seconds to travel from one random point to the next.")]
    [SerializeField] private Vector2 panDurationRange = new Vector2(5f, 10f);

    [Header("Falling Sprites")]
    [SerializeField] private Sprite[] fallingSprites;        // variations, picked at random
    [SerializeField, Range(0, 120)] private int particleCount = 40;
    [SerializeField] private Vector2 sizeRange = new Vector2(30f, 70f);
    [SerializeField] private Vector2 speedRange = new Vector2(80f, 220f);      // px / sec
    [SerializeField] private Vector2 swayAmplitudeRange = new Vector2(10f, 40f);
    [SerializeField] private Vector2 swayFrequencyRange = new Vector2(0.5f, 1.5f);
    [SerializeField] private Vector2 spinRange = new Vector2(-40f, 40f);       // deg / sec

    [Header("Performance")]
    [Tooltip("30 saves battery for a wallpaper. 0 = don't touch.")]
    [SerializeField] private int targetFrameRate = 30;

    // ---------- background state ----------
    private RectTransform bgRt;
    private Vector2 panFrom, panTo;
    private float panT, panDuration = 1f;
    private Vector2 lastCanvasSize = new Vector2(-1f, -1f);
    private int currentBackground;

    // ---------- particle state ----------
    private struct Particle
    {
        public RectTransform rt;
        public Image img;
        public float baseX, y, size;
        public float speed, swayAmp, swayFreq, phase;
        public float angle, spin;
    }

    private Particle[] particles;
    private float time;

    // =====================================================================

    private void Awake()
    {
        if (targetFrameRate > 0) Application.targetFrameRate = targetFrameRate;

        // Background: center-anchored so anchoredPosition is the pan offset.
        bgRt = backgroundImage.rectTransform;
        bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(0.5f, 0.5f);
        backgroundImage.raycastTarget = false;

        // Particle pool (created once).
        particles = new Particle[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            var go = new GameObject("P" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(particleRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;

            particles[i].rt = rt;
            particles[i].img = img;
        }
    }

    private void Start()
    {
        SetBackground(defaultBackground);

        for (int i = 0; i < particles.Length; i++)
            Respawn(i, true);
    }

    // ---------- public API (hook to Buttons) ----------

    /// <summary>Instantly switches to the next background. Panning and falling sprites keep going.</summary>
    public void ChangeBackground()
    {
        if (backgrounds == null || backgrounds.Length == 0) return;
        SetBackground((currentBackground + 1) % backgrounds.Length);
    }

    /// <summary>Instantly switches to a specific background (e.g. one button per background).</summary>
    public void SetBackground(int index)
    {
        if (backgrounds == null || backgrounds.Length == 0) return;
        currentBackground = Mathf.Clamp(index, 0, backgrounds.Length - 1);
        backgroundImage.sprite = backgrounds[currentBackground];
    }

    // ---------- loop ----------

    private void Update()
    {
        float dt = Time.deltaTime;
        time += dt;

        Vector2 canvasSize = particleRoot.rect.size;
        UpdateBackground(dt, canvasSize);
        UpdateParticles(dt, canvasSize.y * 0.5f);
    }

    private void UpdateBackground(float dt, Vector2 canvasSize)
    {
        // Only touch sizeDelta when the canvas size actually changes (rotation, resolution).
        if (canvasSize != lastCanvasSize)
        {
            lastCanvasSize = canvasSize;
            bgRt.sizeDelta = canvasSize * overscan;
            panTo = Vector2.zero;       // restart the pan cleanly inside the new bounds
            panFrom = Vector2.zero;
            panT = 1f;
        }

        // Max offset that never shows the canvas edge.
        Vector2 max = canvasSize * (overscan - 1f) * 0.5f;

        panT += dt / panDuration;
        if (panT >= 1f)
        {
            panFrom = panTo;
            panTo = new Vector2(Random.Range(-max.x, max.x), Random.Range(-max.y, max.y));
            panDuration = Mathf.Max(0.1f, Random.Range(panDurationRange.x, panDurationRange.y));
            panT = 0f;
        }

        // SmoothStep = ease in/out, so it glides and settles at each point (ping-pong feel).
        bgRt.anchoredPosition = Vector2.Lerp(panFrom, panTo, Mathf.SmoothStep(0f, 1f, panT));
    }

    private void UpdateParticles(float dt, float halfH)
    {
        for (int i = 0; i < particles.Length; i++)
        {
            ref Particle p = ref particles[i];

            p.y -= p.speed * dt;
            p.angle += p.spin * dt;

            if (p.y < -halfH - p.size)
            {
                Respawn(i, false);
                continue;
            }

            float x = p.baseX + Mathf.Sin(time * p.swayFreq + p.phase) * p.swayAmp;
            p.rt.anchoredPosition = new Vector2(x, p.y);
            p.rt.localRotation = Quaternion.Euler(0f, 0f, p.angle);
        }
    }

    private void Respawn(int i, bool randomHeight)
    {
        ref Particle p = ref particles[i];

        if (fallingSprites == null || fallingSprites.Length == 0)
        {
            p.img.enabled = false;
            return;
        }
        p.img.enabled = true;

        Rect r = particleRoot.rect;
        float halfW = r.width * 0.5f;
        float halfH = r.height * 0.5f;

        p.size = Random.Range(sizeRange.x, sizeRange.y);
        p.speed = Random.Range(speedRange.x, speedRange.y);
        p.swayAmp = Random.Range(swayAmplitudeRange.x, swayAmplitudeRange.y);
        p.swayFreq = Random.Range(swayFrequencyRange.x, swayFrequencyRange.y);
        p.phase = Random.value * Mathf.PI * 2f;
        p.spin = Random.Range(spinRange.x, spinRange.y);
        p.angle = Random.value * 360f;

        p.baseX = Random.Range(-halfW, halfW);
        p.y = randomHeight ? Random.Range(-halfH, halfH) : halfH + p.size;

        p.rt.sizeDelta = new Vector2(p.size, p.size);
        p.img.sprite = fallingSprites[Random.Range(0, fallingSprites.Length)];
    }
}