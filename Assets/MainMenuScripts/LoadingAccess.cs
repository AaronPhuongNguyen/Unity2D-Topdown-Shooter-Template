using UnityEngine;

/// <summary>
/// Drop this on any object (e.g. a menu Button) to load a scene through LoadingHandle.
/// Hook Load() to a Button's OnClick, or Load(string) to pass the scene name from the event.
/// </summary>
public class LoadingAccess : MonoBehaviour
{
    [SerializeField] private string sceneName;

    /// <summary>Loads the scene set in the Inspector.</summary>
    public void Load() => Load(sceneName);

    /// <summary>Loads a scene by name.</summary>
    public void Load(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            Debug.LogWarning("LoadingAccess: scene name is empty.", this);
            return;
        }

        if (LoadingHandle.Instance == null)
        {
            Debug.LogError("LoadingHandle not found. Is it under GameManager and active?", this);
            return;
        }

        LoadingHandle.Instance.LoadScene(name);
    }
}