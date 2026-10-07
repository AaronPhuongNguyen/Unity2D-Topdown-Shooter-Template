using UnityEngine;

/// <summary>
/// Drop this on any GameObject to expose GameManager actions to the Inspector/UI —
/// e.g. wire a Button's OnClick() to GameHelper.PauseGame(), no other setup needed.
/// </summary>
public class GameHelper : MonoBehaviour
{
    private GameManager gm => GameManager.instance;

    public bool IsPlaying => gm != null && gm.IsPlaying;
    public bool IsPaused => gm != null && gm.IsPaused;
    public GameManager.GameState CurrentState => gm != null ? gm.CurrentState : GameManager.GameState.MainMenu;

    #region State Transitions
    public void StartGame()
    {
        if (gm == null) { LogMissing(); return; }
        gm.StartGame();
    }

    public void PauseGame()
    {
        if (gm == null) { LogMissing(); return; }
        gm.PauseGame();
    }

    public void ResumeGame()
    {
        if (gm == null) { LogMissing(); return; }
        gm.ResumeGame();
    }

    public void TogglePause()
    {
        if (gm == null) { LogMissing(); return; }
        gm.TogglePause();
    }
    #endregion

    private void LogMissing()
    {
        Debug.LogWarning($"[{nameof(GameHelper)}] GameManager.instance is null — call ignored.", this);
    }
}