using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    public static string CurrentSceneName => SceneManager.GetActiveScene().name;
    public static int CurrentSceneIndex => SceneManager.GetActiveScene().buildIndex;

    public static void Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("SceneLoader: scene name is empty.");
            return;
        }
        SceneManager.LoadScene(sceneName);
    }

    public static void Load(int buildIndex)
    {
        if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning($"SceneLoader: build index {buildIndex} out of range.");
            return;
        }
        SceneManager.LoadScene(buildIndex);
    }

    public static void LoadAdditive(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
    }

    public static AsyncOperation LoadAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        return SceneManager.LoadSceneAsync(sceneName);
    }

    public static void Reload()
    {
        SceneManager.LoadScene(CurrentSceneIndex);
    }

    public static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}