using System;
using UnityEngine;
using UnityEngine.Events;

public class EventLinkHelper : MonoBehaviour
{
    #region Game Flow Events
    public UnityEvent OnGameStart;
    public UnityEvent OnGamePause;
    public UnityEvent OnGameResume;
    public UnityEvent OnGameRestart;
    public UnityEvent OnGameOver;
    #endregion

    private void OnEnable()
    {
        EventBus.OnGameStart += GameStart;
        EventBus.OnGamePause += GamePause;
        EventBus.OnGameResume += GameResume;
        EventBus.OnGameRestart += GameRestart;
        EventBus.OnGameOver += GameOver;
    }
    private void OnDisable()
    {
        EventBus.OnGameStart -= GameStart;
        EventBus.OnGamePause -= GamePause;
        EventBus.OnGameResume -= GameResume;
        EventBus.OnGameRestart -= GameRestart;
        EventBus.OnGameOver -= GameOver;
    }

    #region Bridge
    private void GameStart() => OnGameStart?.Invoke();
    private void GamePause() => OnGamePause?.Invoke();
    private void GameResume() => OnGameResume?.Invoke();
    private void GameRestart() => OnGameRestart?.Invoke();
    private void GameOver() => OnGameOver?.Invoke();
    #endregion

    #region Raise
    public void StartTheGame() => EventBus.RaiseGameStart();
    public void PauseTheGame() => EventBus.RaiseGamePause();
    public void ResumeTheGame() => EventBus.RaiseGameResume();
    public void RestartTheGame() => EventBus.RaiseGameRestart();
    public void OverTheGame() => EventBus.RaiseGameOver();
    #endregion
}