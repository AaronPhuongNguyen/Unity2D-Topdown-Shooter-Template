using System;
using UnityEngine;

public static class EventBus
{
    #region Player Events
    public static event Action OnPlayerDeath;
    public static event Action OnPlayerRespawn;
    #endregion

    #region Wave / Spawn Events
    public static event Action<int> OnNewWave;       // wave number
    public static event Action OnWaveCleared;
    #endregion

    #region Game Flow Events
    public static event Action OnGameStart;
    public static event Action OnGamePause;
    public static event Action OnGameResume;
    public static event Action OnGameRestart;
    public static event Action OnGameOver;
    #endregion

    #region Raise Methods
    public static void RaisePlayerDeath() => OnPlayerDeath?.Invoke();
    public static void RaisePlayerRespawn() => OnPlayerRespawn?.Invoke();

    public static void RaiseNewWave(int waveNumber) => OnNewWave?.Invoke(waveNumber);
    public static void RaiseWaveCleared() => OnWaveCleared?.Invoke();

    public static void RaiseGameStart() => OnGameStart?.Invoke();
    public static void RaiseGamePause() => OnGamePause?.Invoke();
    public static void RaiseGameResume() => OnGameResume?.Invoke();
    public static void RaiseGameRestart() => OnGameRestart?.Invoke();
    public static void RaiseGameOver() => OnGameOver?.Invoke();
    #endregion

    #region Reset
    public static void ClearAll()
    {
        OnPlayerDeath = null;
        OnPlayerRespawn = null;

        OnNewWave = null;
        OnWaveCleared = null;

        OnGameStart = null;
        OnGamePause = null;
        OnGameResume = null;
        OnGameRestart = null;
        OnGameOver = null;
    }
    #endregion
}