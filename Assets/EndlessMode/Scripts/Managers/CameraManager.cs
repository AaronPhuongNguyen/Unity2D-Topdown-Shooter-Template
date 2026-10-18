using System.Collections.Generic;
using Server;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(100)]
public class CamManager : MonoBehaviour, ILateTick
{
    public enum FlashType
    {
        White,      // one white frame, fades out
        Black,      // one black frame, fades out
        Red,        // red hit flash
        Impact,     // white -> black -> white fade (manga style impact frame)
        Strobe,     // white, black, white, black, then fade
        Double      // two quick white pulses
    }

    private struct FlashStep
    {
        public Color color;
        public float hold;   // seconds at full colour
        public float fade;   // seconds fading to transparent
    }

    #region Singleton
    public static CamManager instance { get; private set; }
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

    #region Unity
    private void Start()
    {
        if (cam == null) cam = Camera.main;
        SetFlashAlpha(Color.clear);
    }

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ILateTick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ILateTick)this);
    }
    #endregion

    #region Camera
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float ZoomValue;
    [SerializeField] private Vector2 Offset;

    private Camera cam;
    private PlayerManager pm => PlayerManager.instance;
    private Vector2 ZoomLimit
        => new Vector2(5f, Mathf.Min(50, pm != null && pm.attribute != null ? pm.attribute.SIGHT_Current / 1.5f : 50f));

    // Camera follow runs as a LateTick (after gameplay Tick) so it reads
    // the player's already-moved position this frame instead of lagging
    // a frame behind, same reasoning as Unity's Update/LateUpdate split.
    public void LateTick(float delta)
    {
        if (cam == null) return;

        // Remove last frame's shake first, so the follow lerp never starts from a shaken position.
        cam.transform.position -= appliedShake;
        appliedShake = Vector3.zero;

        if (CanMove()) MoveCamera(delta);

        // Shake and flash run even when the camera is standing still.
        TickShake(delta);
        ApplyShake();
        TickFlash();
    }

    private void MoveCamera(float delta)
    {
        Vector3 step = Vector3.Lerp(cam.transform.position, pm.Controlling.transform.position + (Vector3)Offset, moveSpeed * delta);
        step.z = -10f;
        cam.transform.position = step;
    }

    bool CanMove()
    {
        if (pm == null) return false;
        if (pm.Controlling == null) return false;
        if (cam == null) return false;
        if ((pm.Controlling.transform.position + (Vector3)Offset) == cam.transform.position) return false;
        return true;
    }

    #region Zoom
    public void ZoomIn(float v)
    {
        if (cam == null) return;
        ZoomValue = cam.orthographicSize;
        ZoomValue += v;
        Zoom();
    }
    public void ZoomOut(float v)
    {
        if (cam == null) return;
        ZoomValue = cam.orthographicSize;
        ZoomValue -= v;
        Zoom();
    }
    public void Zoom()
    {
        if (cam == null) return;
        ZoomValue = Mathf.Clamp(ZoomValue, ZoomLimit.x, ZoomLimit.y);
        cam.orthographicSize = ZoomValue;
    }
    #endregion

    #region Range check
    /// <summary>True if worldPos is within the controlled player's SIGHT.</summary>
    public bool IsInPlayerRange(Vector2 worldPos)
    {
        if (pm == null || pm.Controlling == null || pm.attribute == null) return false;
        float sight = pm.attribute.SIGHT_Current;
        return ((Vector2)pm.Controlling.transform.position - worldPos).sqrMagnitude <= sight * sight;
    }

    private float RangeFactor(Vector2 worldPos)
    {
        if (!IsInPlayerRange(worldPos)) return 0f;
        if (!scaleByDistance) return 1f;

        float sight = pm.attribute.SIGHT_Current;
        float d = Vector2.Distance(pm.Controlling.transform.position, worldPos);
        return Mathf.Lerp(1f, minDistanceScale, Mathf.Clamp01(d / Mathf.Max(0.01f, sight)));
    }
    #endregion

    #region Shake
    [Header("Shake")]
    [SerializeField] private float shakeFalloff = 4f; // how fast magnitude decays per second
    [Tooltip("Positional shakes get weaker the farther they are (inside the player's Sight).")]
    [SerializeField] private bool scaleByDistance = true;
    [Tooltip("Shake strength at the very edge of Sight when Scale By Distance is on.")]
    [SerializeField, Range(0f, 1f)] private float minDistanceScale = 0.3f;

    private float shakeMagnitude;
    private Vector3 shakeOffset;
    private Vector3 appliedShake;
    public bool ShakeEnabled = true;

    /// <summary>Default: always shakes (the source is treated as the controlled player).</summary>
    public void Shake(float magnitude)
    {
        if (!ShakeEnabled) return;
        // Take the stronger of the two rather than adding - a second small
        // shake while a big one is still playing shouldn't make it worse.
        shakeMagnitude = Mathf.Max(shakeMagnitude, magnitude);
    }

    /// <summary>Shakes only if worldPos is within the controlled player's SIGHT.</summary>
    public void Shake(float magnitude, Vector2 worldPos)
    {
        float f = RangeFactor(worldPos);
        if (f <= 0f) return;
        Shake(magnitude * f);
    }

    /// <summary>Shakes only if the transform is within the controlled player's SIGHT.</summary>
    public void Shake(float magnitude, Transform source)
    {
        if (source == null) { Shake(magnitude); return; }
        Shake(magnitude, (Vector2)source.position);
    }

    private void TickShake(float delta)
    {
        if (shakeMagnitude <= 0f)
        {
            shakeOffset = Vector3.zero;
            return;
        }

        shakeOffset = (Vector3)RNG.GetInsideCircle(shakeMagnitude);
        shakeMagnitude = Mathf.MoveTowards(shakeMagnitude, 0f, shakeFalloff * delta);
    }

    private void ApplyShake()
    {
        if (shakeOffset == Vector3.zero) return;
        cam.transform.position += shakeOffset;
        appliedShake = shakeOffset;
    }
    #endregion

    #region Flash Impact Frame
    [Header("Flash Impact Frame")]
    [Tooltip("Full-screen UI Image on a top Canvas. Raycast Target is turned off automatically.")]
    [SerializeField] private Image flashImage;
    public bool FlashEnabled = true;

    private readonly List<FlashStep> flashSteps = new List<FlashStep>(8);
    private int flashIndex;
    private float flashTimer;

    /// <summary>Default: always flashes.</summary>
    public void Flash(FlashType type = FlashType.White, float intensity = 1f)
        => StartFlash(type, intensity);

    /// <summary>Flashes only if worldPos is within the controlled player's SIGHT.</summary>
    public void Flash(Vector2 worldPos, FlashType type = FlashType.White, float intensity = 1f)
    {
        float f = RangeFactor(worldPos);
        if (f <= 0f) return;
        StartFlash(type, intensity * f);
    }

    /// <summary>Fully custom single flash: colour, time at full strength, fade time, pulses.</summary>
    public void FlashCustom(Color color, float hold, float fade, int pulses = 1, float pulseGap = 0.04f)
    {
        if (!FlashEnabled || flashImage == null) return;

        flashSteps.Clear();
        for (int i = 0; i < Mathf.Max(1, pulses); i++)
        {
            bool last = i == pulses - 1;
            flashSteps.Add(new FlashStep { color = color, hold = hold, fade = last ? fade : pulseGap });
        }
        BeginFlash();
    }

    private void StartFlash(FlashType type, float intensity)
    {
        if (!FlashEnabled || flashImage == null) return;

        float a = Mathf.Clamp01(intensity);
        Color white = new Color(1f, 1f, 1f, a);
        Color black = new Color(0f, 0f, 0f, a);
        Color red = new Color(1f, 0.1f, 0.1f, a * 0.8f);

        flashSteps.Clear();
        switch (type)
        {
            case FlashType.White:
                Add(white, 0.04f, 0.15f);
                break;
            case FlashType.Black:
                Add(black, 0.05f, 0.2f);
                break;
            case FlashType.Red:
                Add(red, 0.03f, 0.25f);
                break;
            case FlashType.Impact:
                Add(white, 0.04f, 0f);
                Add(black, 0.05f, 0f);
                Add(white, 0.03f, 0.18f);
                break;
            case FlashType.Strobe:
                Add(white, 0.03f, 0f);
                Add(black, 0.03f, 0f);
                Add(white, 0.03f, 0f);
                Add(black, 0.03f, 0.2f);
                break;
            case FlashType.Double:
                Add(white, 0.03f, 0.04f);
                Add(white, 0.03f, 0.18f);
                break;
        }
        BeginFlash();
    }

    private void Add(Color c, float hold, float fade)
        => flashSteps.Add(new FlashStep { color = c, hold = hold, fade = fade });

    private void BeginFlash()
    {
        flashImage.raycastTarget = false;
        flashIndex = 0;
        flashTimer = 0f;
        SetFlashAlpha(flashSteps[0].color);
    }

    private void TickFlash()
    {
        if (flashImage == null || flashIndex >= flashSteps.Count) return;

        // Unscaled: impact frames must not freeze with the game speed.
        flashTimer += Time.unscaledDeltaTime;

        FlashStep s = flashSteps[flashIndex];

        if (flashTimer < s.hold)
        {
            SetFlashAlpha(s.color);
            return;
        }

        float t = s.fade > 0.0001f ? (flashTimer - s.hold) / s.fade : 1f;
        if (t >= 1f)
        {
            flashIndex++;
            flashTimer = 0f;

            if (flashIndex >= flashSteps.Count) SetFlashAlpha(Color.clear);
            else SetFlashAlpha(flashSteps[flashIndex].color);
            return;
        }

        Color c = s.color;
        c.a *= 1f - t;
        SetFlashAlpha(c);
    }

    private void SetFlashAlpha(Color c)
    {
        if (flashImage == null) return;
        flashImage.color = c;
        bool show = c.a > 0.001f;
        if (flashImage.enabled != show) flashImage.enabled = show;
    }
    #endregion

    [ContextMenu("Test Flash")]
    public void TryFlash() => Flash(FlashType.Impact, 1f);

    [ContextMenu("Test Flash (Long White)")]
    public void TryFlashLong() => FlashCustom(Color.white, 0.3f, 1f);
    #endregion
}