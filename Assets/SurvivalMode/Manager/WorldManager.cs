using UnityEngine;

[DefaultExecutionOrder(-5)]
public class WorldManager : MonoBehaviour
{
    #region Singleton
    public static WorldManager instance { get; private set; }
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }
    #endregion

    #region Properties

    #endregion
}