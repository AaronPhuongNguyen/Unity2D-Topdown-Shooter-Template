using TMPro;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class VisualFPS : MonoBehaviour, IUnscaledTick
{
    public TextMeshProUGUI tmp;

    private float updateTimer;

    // FPS should reflect real engine performance regardless of
    // GameSpeed/pause, so this rides IUnscaledTick (real Time.deltaTime)
    // instead of ITick - it'll keep updating even while gameplay is frozen.
    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((IUnscaledTick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((IUnscaledTick)this);
    }

    public void UnscaledTick(float delta)
    {
        if (tmp == null) return;

        updateTimer -= delta;
        if (updateTimer > 0f) return;
        updateTimer = 0.5f;

        float fps = delta > 0f ? 1f / delta : 0f;
        tmp.text = $"FPS: {fps.ToString("F0")}";
    }
}