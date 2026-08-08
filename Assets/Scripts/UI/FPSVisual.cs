using TMPro;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class VisualFPS : MonoBehaviour
{
    public TextMeshProUGUI tmp;

    private float updateInterval;

    private void Update()
    {
        if (tmp == null) return;
        if (updateInterval > Time.time) return;
        updateInterval = Time.time + 0.5f;

        float fps = 1 / Time.unscaledDeltaTime;
        tmp.text = $"FPS: {fps.ToString("F0")}";
    }
}