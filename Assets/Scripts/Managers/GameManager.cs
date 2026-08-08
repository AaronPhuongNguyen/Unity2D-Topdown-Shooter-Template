using UnityEngine;

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
    private void Start()
    {
        Application.targetFrameRate = 60;
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