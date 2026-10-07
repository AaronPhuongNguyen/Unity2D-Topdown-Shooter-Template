using UnityEngine;

public class SceneSwitcher : MonoBehaviour
{
    [Header("Target Scene")]
    [SerializeField] private string targetSceneName;

    [Header("Options")]
    [SerializeField] private bool useAsync = true;
    [SerializeField] private float delayBeforeLoad = 0f;
    public void LoadScene()
    {
        if (delayBeforeLoad > 0f)
        {
            Invoke(nameof(DoLoad), delayBeforeLoad);
        }
        else
        {
            DoLoad();
        }
    }

    public void LoadSceneByName(string sceneName)
    {
        if (useAsync)
            SceneLoader.LoadAsync(sceneName);
        else
            SceneLoader.Load(sceneName);
    }

    public void ReloadCurrentScene()
    {
        SceneLoader.Reload();
    }

    public void QuitGame()
    {
        SceneLoader.QuitGame();
    }

    private void DoLoad()
    {
        LoadSceneByName(targetSceneName);
    }
}