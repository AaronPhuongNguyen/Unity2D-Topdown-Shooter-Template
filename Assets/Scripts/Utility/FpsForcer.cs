using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class FpsForcer : MonoBehaviour
{
    [Header("Target")]
    [SerializeField, Range(30, 240)] private int targetFPS = 60;

    [Header("Force Settings")]
    [SerializeField] private bool forcePeriodically = true;

    [SerializeField, Min(0.1f)] private float checkInterval = 1f;

    [SerializeField] private bool syncFixedTimestep = true;

    private float nextCheckTime;
    private int lastAppliedFPS = -1;

    public static FpsForcer instance { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void Update()
    {
        if (!forcePeriodically) return;
        if (Time.unscaledTime < nextCheckTime) return;
        nextCheckTime = Time.unscaledTime + checkInterval;

        if (Application.targetFrameRate != targetFPS || (!Application.isEditor && QualitySettings.vSyncCount != 0))
        {
            Apply();
        }
    }

    #region Public API
    public void SetTargetFPS(int fps)
    {
        targetFPS = Mathf.Clamp(fps, 15, 240);
        Apply();
    }

    public void Apply()
    {
        if (!Application.isEditor)
        {
            QualitySettings.vSyncCount = 0;
        }

        Application.targetFrameRate = targetFPS;

#if UNITY_ANDROID && !UNITY_EDITOR
        ForceAndroid();
#endif

        if (syncFixedTimestep)
            Time.fixedDeltaTime = 1f / targetFPS;

        lastAppliedFPS = targetFPS;
    }
    #endregion

#if UNITY_ANDROID && !UNITY_EDITOR
    private void ForceAndroid()
    {
        try
        {
            if (Screen.sleepTimeout != SleepTimeout.NeverSleep)
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FpsForcer] Android sleep override failed: {e.Message}");
        }
    }
#endif

    private void OnApplicationPause(bool pause)
    {
        if (!pause) Apply();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) Apply();
    }
}