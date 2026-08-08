using UnityEngine;
using UnityEngine.InputSystem;
[DefaultExecutionOrder(-99)]
public class GameManager : MonoBehaviour
{
    #region Singleton
    public static GameManager instance { get; private set; }
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
    #endregion
    #region Setting Applier
    void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
#if UNITY_ANDROID && !UNITY_EDITOR
    try 
    {
        using (AndroidJavaClass activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (AndroidJavaObject currentActivity = activityClass.GetStatic<AndroidJavaObject>("currentActivity"))
        using (AndroidJavaObject window = currentActivity.Call<AndroidJavaObject>("getWindow"))
        using (AndroidJavaObject layoutParams = window.Call<AndroidJavaObject>("getAttributes"))
        {
            layoutParams.Set("preferredRefreshRate", 60f);
            window.Call("setAttributes", layoutParams);
        }
    }
    catch (System.Exception e)
    {
        Debug.LogError("Failed to set preferred refresh rate: " + e.Message);
    }
#endif
    }
    #endregion
    #region State
    public enum GameState { MainMenu, Playing, Paused, GameOver }
    [Header("Runtime State")]
    [SerializeField] private GameState currentState = GameState.MainMenu;
    public GameState CurrentState => currentState;
    public bool IsPlaying => currentState == GameState.Playing;
    public bool IsPaused => currentState == GameState.Paused;
    #endregion
    #region State Transitions
    public void StartGame()
    {
        SetState(GameState.Playing);
        Time.timeScale = 1f;
        EventBus.RaiseGameStart();
    }
    public void PauseGame()
    {
        SetState(GameState.Paused);
        Time.timeScale = 0f;
        EventBus.RaiseGamePause();
    }
    public void ResumeGame()
    {
        if (currentState != GameState.Paused) return;
        SetState(GameState.Playing);
        Time.timeScale = 1f;
        EventBus.RaiseGameResume();
    }
    private void SetState(GameState newState)
    {
        if (currentState == newState) return;
        currentState = newState;
    }
    #endregion
    #region Convenience
    public void TogglePause()
    {
        if (IsPaused) ResumeGame();
        else if (IsPlaying) PauseGame();
    }
    #endregion
}