using System;
using System.Collections.Generic;
using Server;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Day/night + weather. Drives a URP 2D Global Light (Light2D) and a full-screen rain overlay (UI/RainOverlay shader).
/// One full 24h day = dayLengthSeconds (360 by default). Weather: Clear, Cloudy, Fog, Drizzle, Rain, HeavyRain,
/// Thunderstorm. Lightning flashes last 0.8~1.4s (several flickering pulses) and the thunder sound arrives a moment later.
/// Runs on TickSystem, so it respects pause / game speed.
/// </summary>
public class WeatherManager : MonoBehaviour, ITick
{
    #region Singleton
    public static WeatherManager instance { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }
        instance = this;

        if (presets == null || presets.Count == 0) presets = DefaultPresets();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnEnable()
    {
        if (TickSystem.Instance != null) TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null) TickSystem.Unregister((ITick)this);
    }

    private void Reset() => presets = DefaultPresets();
    #endregion

    #region Inspector
    [Header("Time")]
    [Tooltip("Real seconds for a full 24h day.")]
    [SerializeField, Min(1f)] private float dayLengthSeconds = 240f;
    [SerializeField, Range(0f, 24f)] private float startHour = 7f;
    public bool FreezeTime;

    [Header("Global Light (URP 2D)")]
    [Tooltip("Leave empty to auto-find the first Global Light2D.")]
    [SerializeField] private Light2D globalLight;
    [SerializeField] private Color dayColor = new Color(1f, 0.96f, 0.88f);
    [SerializeField] private Color nightColor = new Color(0.25f, 0.32f, 0.6f);
    [SerializeField] private Color twilightColor = new Color(1f, 0.55f, 0.35f);
    [SerializeField, Min(0f)] private float dayIntensity = 1f;
    [SerializeField, Min(0f)] private float nightIntensity = 0.05f;

    [Header("Weather")]
    public bool AutoWeather = true;
    [SerializeField] private WeatherType startWeather = WeatherType.Clear;
    [Tooltip("Seconds to blend from one weather to the next.")]
    [SerializeField, Min(0.1f)] private float transitionTime = 8f;
    [SerializeField] private List<WeatherPreset> presets = new List<WeatherPreset>();

    [Header("Lightning (Thunderstorm)")]
    [Tooltip("Random seconds between strikes.")]
    [SerializeField] private Vector2 strikeInterval = new Vector2(5f, 14f);
    [Tooltip("Each flash lasts a random time between X and Y seconds.")]
    [SerializeField] private Vector2 flashDuration = new Vector2(0.8f, 1.4f);
    [SerializeField] private Color lightningColor = new Color(0.8f, 0.88f, 1f);
    [SerializeField, Min(0f)] private float lightningIntensity = 1.6f;
    [Tooltip("Thunder sound is heard this many seconds after the flash (distance).")]
    [SerializeField] private Vector2 thunderDelay = new Vector2(0.2f, 2.2f);
    [SerializeField] private bool shakeOnThunder = true;
    [SerializeField] private float thunderShake = 1.2f;

    [Header("Audio")]
    [Tooltip("Looping rain for drizzle / light rain.")]
    [SerializeField] private AudioClip rainLightLoop;
    [Tooltip("Looping rain for heavy rain / storms.")]
    [SerializeField] private AudioClip rainHeavyLoop;
    [SerializeField] private AudioClip[] thunderClips;
    [SerializeField, Range(0f, 1f)] private float rainVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float thunderVolume = 1f;
    [SerializeField, Min(0.1f)] private float audioFadeSpeed = 0.5f;

    [Header("Rain Overlay (camera effect)")]
    [Tooltip("Full-screen UI Image. Leave empty to auto-create an overlay canvas.")]
    [SerializeField] private Image rainImage;
    [Tooltip("Material using the 'UI/RainOverlay' shader. Assign it so the shader is included in builds.")]
    [SerializeField] private Material rainMaterial;
    [Tooltip("Turn on if the rain falls upwards on your platform.")]
    [SerializeField] private bool flipY = false;
    #endregion

    #region Public state / events
    /// <summary>0 = pitch dark, 1 = bright. Combines time of day and weather darkness (storms, heavy rain).</summary>
    public float Brightness => Mathf.Clamp01(dayFactor * curLightMul);
    public float Hour => hour;
    public float NormalizedTime => hour / 24f;
    public float DayFactor => dayFactor;            // 0 = full night, 1 = full day
    public bool IsNight => dayFactor < 0.25f;
    public WeatherType Current => target != null ? target.type : WeatherType.Clear;
    public float RainAmount => curRain;
    public bool IsRaining => curRain > 0.05f;
    public bool IsLightningFlashing => flashing;
    public string ClockText
    {
        get
        {
            int h = Mathf.FloorToInt(hour) % 24;
            int m = Mathf.FloorToInt((hour - Mathf.Floor(hour)) * 60f);
            return $"{h:00}:{m:00}";
        }
    }

    public event Action<WeatherType> OnWeatherChanged;
    public event Action<bool> OnNightChanged;   // true = night started, false = day started
    #endregion

    #region Runtime
    private float hour;
    private float dayFactor = 1f;
    private bool wasNight;
    private float clock;

    private WeatherPreset target;
    private float weatherTimer;

    private float curRain, curWind, curFog, curLightMul = 1f, curThunder;
    private Color curTint = Color.white;

    // Lightning
    private bool flashing;
    private float flashT, flashDur, flash;
    private float off1, off2, amp2, amp3;
    private float strikeTimer;
    private bool thunderPending;
    private float thunderAt, thunderLoud;

    // Audio
    private AudioSource lightSrc, heavySrc, thunderSrc;

    // Overlay
    private Material rainMat;
    private static readonly int RainId = Shader.PropertyToID("_Rain");
    private static readonly int WindId = Shader.PropertyToID("_Wind");
    private static readonly int FogId = Shader.PropertyToID("_Fog");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int AmbientId = Shader.PropertyToID("_Ambient");
    private static readonly int TimeId = Shader.PropertyToID("_T");
    private static readonly int FlipId = Shader.PropertyToID("_FlipY");
    #endregion

    #region Setup
    private void Start()
    {
        hour = startHour;
        EnsureLight();
        EnsureAudio();
        EnsureOverlay();

        dayFactor = ComputeDayFactor();
        wasNight = IsNight;
        SetWeather(startWeather, true);
        ApplyLight();
    }

    private void EnsureLight()
    {
        if (globalLight != null) return;

        foreach (Light2D l in FindObjectsOfType<Light2D>())
        {
            if (l.lightType == Light2D.LightType.Global) { globalLight = l; break; }
        }
        if (globalLight == null)
            Debug.LogWarning("[WeatherManager] No Global Light2D found. Assign one (URP 2D Renderer).");
    }

    private void EnsureAudio()
    {
        lightSrc = MakeLoop("RainLight", rainLightLoop);
        heavySrc = MakeLoop("RainHeavy", rainHeavyLoop);

        var go = new GameObject("Thunder");
        go.transform.SetParent(transform, false);
        thunderSrc = go.AddComponent<AudioSource>();
        thunderSrc.playOnAwake = false;
        thunderSrc.spatialBlend = 0f;
    }

    private AudioSource MakeLoop(string n, AudioClip clip)
    {
        if (clip == null) return null;
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = 0f;
        return s;
    }

    private void EnsureOverlay()
    {
        if (rainImage == null)
        {
            var canvasGo = new GameObject("RainOverlay", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // under the CamManager flash (999)

            var imgGo = new GameObject("Rain", typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rainImage = imgGo.GetComponent<Image>();
        }

        rainImage.raycastTarget = false;

        if (rainMaterial != null) rainMat = new Material(rainMaterial);
        else
        {
            Shader sh = Shader.Find("UI/RainOverlay");
            if (sh == null)
            {
                Debug.LogWarning("[WeatherManager] Shader 'UI/RainOverlay' not found. Assign a Rain Material.");
                return;
            }
            rainMat = new Material(sh);
        }

        rainImage.material = rainMat;
        rainImage.color = Color.white;
        rainImage.enabled = false;
    }

    private static List<WeatherPreset> DefaultPresets() => new List<WeatherPreset>
    {
        new WeatherPreset { type = WeatherType.Clear,        lightMultiplier = 1.00f, tint = Color.white,                     rain = 0f,    wind = 0.1f, fog = 0f,   lightning = false, duration = new Vector2(50, 110), weight = 30 },
        new WeatherPreset { type = WeatherType.Cloudy,       lightMultiplier = 0.80f, tint = new Color(0.85f, 0.88f, 0.95f), rain = 0f,    wind = 0.2f, fog = 0.05f,lightning = false, duration = new Vector2(40, 90),  weight = 22 },
        new WeatherPreset { type = WeatherType.Fog,          lightMultiplier = 0.72f, tint = new Color(0.82f, 0.86f, 0.9f),  rain = 0f,    wind = 0f,   fog = 0.75f,lightning = false, duration = new Vector2(40, 80),  weight = 6  },
        new WeatherPreset { type = WeatherType.Drizzle,      lightMultiplier = 0.72f, tint = new Color(0.8f, 0.86f, 0.95f),  rain = 0.25f, wind = 0.15f,fog = 0.15f,lightning = false, duration = new Vector2(35, 70),  weight = 12 },
        new WeatherPreset { type = WeatherType.Rain,         lightMultiplier = 0.60f, tint = new Color(0.72f, 0.8f, 0.92f),  rain = 0.55f, wind = 0.35f,fog = 0.2f, lightning = false, duration = new Vector2(40, 90),  weight = 14 },
        new WeatherPreset { type = WeatherType.HeavyRain,    lightMultiplier = 0.48f, tint = new Color(0.62f, 0.72f, 0.86f), rain = 0.9f,  wind = 0.6f, fog = 0.3f, lightning = false, duration = new Vector2(30, 70),  weight = 7  },
        new WeatherPreset { type = WeatherType.Thunderstorm, lightMultiplier = 0.40f, tint = new Color(0.55f, 0.62f, 0.8f),  rain = 1f,    wind = 0.8f, fog = 0.35f,lightning = true,  duration = new Vector2(40, 80),  weight = 9  },
    };
    #endregion

    #region Tick
    public void Tick(float dt)
    {
        if (TickSystem.GameSpeed <= 0f) return;

        clock += dt;

        TickTime(dt);
        TickWeather(dt);
        TickLightning(dt);

        ApplyLight();
        ApplyOverlay();
        TickAudio(dt);
    }

    private void TickTime(float dt)
    {
        if (!FreezeTime)
        {
            hour += dt * 24f / dayLengthSeconds;
            if (hour >= 24f) hour -= 24f;
        }

        dayFactor = ComputeDayFactor();

        bool night = IsNight;
        if (night != wasNight)
        {
            wasNight = night;
            OnNightChanged?.Invoke(night);
        }
    }

    /// <summary>Sun elevation: 6h = sunrise, 12h = noon (1), 18h = sunset, 0h = midnight (-1).</summary>
    private float Elevation() => Mathf.Sin((hour - 6f) / 24f * Mathf.PI * 2f);

    private float ComputeDayFactor()
        => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, 0.35f, Elevation()));

    private void TickWeather(float dt)
    {
        if (target == null) return;

        if (AutoWeather)
        {
            weatherTimer -= dt;
            if (weatherTimer <= 0f) PickNextWeather();
        }

        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, transitionTime * 0.35f));
        curRain = Mathf.Lerp(curRain, target.rain, k);
        curWind = Mathf.Lerp(curWind, target.wind, k);
        curFog = Mathf.Lerp(curFog, target.fog, k);
        curLightMul = Mathf.Lerp(curLightMul, target.lightMultiplier, k);
        curTint = Color.Lerp(curTint, target.tint, k);
        curThunder = Mathf.Lerp(curThunder, target.lightning ? 1f : 0f, k);
    }
    #endregion

    #region Weather control
    private WeatherPreset Find(WeatherType t)
    {
        for (int i = 0; i < presets.Count; i++)
            if (presets[i].type == t) return presets[i];
        return presets.Count > 0 ? presets[0] : null;
    }

    /// <summary>Changes the weather. instant = no blend. duration &lt;= 0 uses the preset's random duration.</summary>
    public void SetWeather(WeatherType type, bool instant = false, float duration = -1f)
    {
        WeatherPreset p = Find(type);
        if (p == null) return;

        bool wasStorm = target != null && target.lightning;
        target = p;
        weatherTimer = duration > 0f ? duration : RNG.GetFloat(Mathf.Min(p.duration.x, p.duration.y), Mathf.Max(p.duration.x, p.duration.y));

        if (p.lightning && !wasStorm) strikeTimer = RNG.GetFloat(1.5f, 4f);

        if (instant)
        {
            curRain = p.rain;
            curWind = p.wind;
            curFog = p.fog;
            curLightMul = p.lightMultiplier;
            curTint = p.tint;
            curThunder = p.lightning ? 1f : 0f;
        }

        OnWeatherChanged?.Invoke(type);
    }

    private void PickNextWeather()
    {
        float total = 0f;
        for (int i = 0; i < presets.Count; i++)
            if (presets[i] != target) total += presets[i].weight;

        if (total <= 0f) { weatherTimer = 30f; return; }

        float roll = RNG.GetFloat(0f, total);
        for (int i = 0; i < presets.Count; i++)
        {
            if (presets[i] == target) continue;
            roll -= presets[i].weight;
            if (roll <= 0f)
            {
                SetWeather(presets[i].type);
                return;
            }
        }
        SetWeather(presets[0].type);
    }
    #endregion

    #region Lightning
    private void TickLightning(float dt)
    {
        flash = 0f;

        if (flashing)
        {
            flashT += dt;
            if (flashT >= flashDur) flashing = false;
            else flash = EvalFlash();
        }
        else if (curThunder > 0.5f)
        {
            strikeTimer -= dt;
            if (strikeTimer <= 0f) StrikeLightning();
        }

        if (thunderPending && clock >= thunderAt)
        {
            thunderPending = false;
            PlayThunder();
        }
    }

    /// <summary>Starts a lightning flash right now (also used by the storm automatically).</summary>
    [ContextMenu("Strike Lightning")]
    public void StrikeLightning()
    {
        flashing = true;
        flashT = 0f;
        flashDur = RNG.GetFloat(Mathf.Min(flashDuration.x, flashDuration.y), Mathf.Max(flashDuration.x, flashDuration.y));

        // A real bolt flickers: main strike, then one or two weaker re-strikes.
        off1 = RNG.GetFloat(0.08f, 0.2f);
        off2 = off1 + RNG.GetFloat(0.1f, 0.3f);
        amp2 = RNG.GetFloat(0.5f, 0.85f);
        amp3 = RNG.GetFloat(0.3f, 0.6f);

        strikeTimer = RNG.GetFloat(Mathf.Min(strikeInterval.x, strikeInterval.y), Mathf.Max(strikeInterval.x, strikeInterval.y));

        float delay = RNG.GetFloat(Mathf.Min(thunderDelay.x, thunderDelay.y), Mathf.Max(thunderDelay.x, thunderDelay.y));
        thunderPending = true;
        thunderAt = clock + delay;
        thunderLoud = Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(thunderDelay.x, Mathf.Max(thunderDelay.y, thunderDelay.x + 0.01f), delay));
    }

    private float EvalFlash()
    {
        float t = flashT;
        float life = 1f - Mathf.Clamp01(t / flashDur);

        float e = Pulse(t, 0f, 1f) + Pulse(t, off1, amp2) + Pulse(t, off2, amp3);
        e += 0.35f * life;                                   // afterglow, so the flash really lasts its full duration
        float noise = 0.75f + 0.25f * Mathf.PerlinNoise(clock * 40f, 0.5f);

        return Mathf.Clamp01(e * life * life * noise * 1.4f);
    }

    private static float Pulse(float t, float offset, float amp)
    {
        float x = t - offset;
        if (x < 0f) return 0f;
        return amp * Mathf.Clamp01(x / 0.03f) * Mathf.Exp(-x * 5f);
    }

    private void PlayThunder()
    {
        if (thunderSrc != null && thunderClips != null && thunderClips.Length > 0)
        {
            AudioClip clip = thunderClips[RNG.GetInt(0, thunderClips.Length)];
            if (clip != null) thunderSrc.PlayOneShot(clip, thunderVolume * thunderLoud);
        }

        if (shakeOnThunder && CamManager.instance != null)
            CamManager.instance.Shake(thunderShake * thunderLoud);
    }
    #endregion

    #region Apply
    private void ApplyLight()
    {
        if (globalLight == null) return;

        float elev = Elevation();
        float twilight = Mathf.Clamp01(1f - Mathf.Abs(elev) * 3.5f);

        Color c = Color.Lerp(nightColor, dayColor, dayFactor);
        c = Color.Lerp(c, twilightColor, twilight * 0.75f);
        c *= curTint;

        float intensity = Mathf.Lerp(nightIntensity, dayIntensity, dayFactor) * curLightMul;

        c = Color.Lerp(c, lightningColor, flash * 0.8f);
        intensity += flash * lightningIntensity;

        c.a = 1f;
        globalLight.color = c;
        globalLight.intensity = intensity;
    }

    private void ApplyOverlay()
    {
        if (rainMat == null || rainImage == null) return;

        bool show = curRain > 0.01f || curFog > 0.02f || flash > 0.01f;
        if (rainImage.enabled != show) rainImage.enabled = show;
        if (!show) return;

        float ambient = Mathf.Clamp01(Mathf.Lerp(0.35f, 1f, dayFactor) + flash * 0.7f);

        rainMat.SetFloat(RainId, curRain);
        rainMat.SetFloat(WindId, curWind);
        rainMat.SetFloat(FogId, curFog);
        rainMat.SetFloat(FlashId, flash * 0.3f);
        rainMat.SetFloat(AmbientId, ambient);
        rainMat.SetFloat(TimeId, clock);
        rainMat.SetFloat(FlipId, flipY ? 1f : 0f);
    }

    private void TickAudio(float dt)
    {
        float heavy = Mathf.InverseLerp(0.45f, 0.85f, curRain);
        float lightVol = Mathf.Clamp01(curRain * 2.2f) * (1f - heavy * 0.7f) * rainVolume;
        float heavyVol = heavy * rainVolume;

        Fade(lightSrc, lightVol, dt);
        Fade(heavySrc, heavyVol, dt);
    }

    private void Fade(AudioSource s, float targetVol, float dt)
    {
        if (s == null) return;

        s.volume = Mathf.MoveTowards(s.volume, targetVol, audioFadeSpeed * dt);

        if (s.volume > 0.001f && !s.isPlaying) s.Play();
        else if (s.volume <= 0.001f && targetVol <= 0.001f && s.isPlaying) s.Stop();
    }
    #endregion

    #region Debug
    public void SetHour(float h) { hour = Mathf.Repeat(h, 24f); dayFactor = ComputeDayFactor(); }

    [ContextMenu("Next Weather")] private void DbgNext() => PickNextWeather();
    [ContextMenu("Set Noon")] private void DbgNoon() => SetHour(12f);
    [ContextMenu("Set Midnight")] private void DbgMidnight() => SetHour(0f);
    [ContextMenu("Force Thunderstorm")] private void DbgStorm() => SetWeather(WeatherType.Thunderstorm);
    [ContextMenu("Force Heavy Rain")] private void DbgHeavy() => SetWeather(WeatherType.HeavyRain);
    [ContextMenu("Force Clear")] private void DbgClear() => SetWeather(WeatherType.Clear);
    #endregion
}
public enum WeatherType { Clear, Cloudy, Fog, Drizzle, Rain, HeavyRain, Thunderstorm }

[Serializable]
public class WeatherPreset
{
    public WeatherType type;
    [Tooltip("Multiplies the Global Light intensity (storms are darker).")]
    [Range(0f, 1.2f)] public float lightMultiplier = 1f;
    [Tooltip("Multiplies the Global Light colour.")]
    public Color tint = Color.white;
    [Range(0f, 1f)] public float rain;
    [Tooltip("Rain slant. -1 = left, +1 = right.")]
    [Range(-1f, 1f)] public float wind;
    [Range(0f, 1f)] public float fog;
    public bool lightning;
    [Tooltip("How long this weather lasts (random between X and Y seconds).")]
    public Vector2 duration = new Vector2(40f, 90f);
    [Tooltip("Chance weight when the next weather is picked.")]
    [Min(0f)] public float weight = 1f;
}