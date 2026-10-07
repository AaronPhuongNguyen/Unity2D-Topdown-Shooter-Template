using UnityEngine;

public class PropertiesVisual : MonoBehaviour
{
    [HideInInspector] public PlayerProperties pp;

    private void OnEnable()
    {
        if (pp != null) pp.UpdateStatus();
    }
}