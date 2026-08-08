using UnityEngine;

[DefaultExecutionOrder(50)]
public class VisualUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject fpsPanel;
    [SerializeField] private GameObject zomCountPanel;
    [SerializeField] private GameObject remainingTimePanel;
    [SerializeField] private GameObject wavePanel;

    [Header("Checker")]
    [SerializeField] private bool showFps = true;
    [SerializeField] private bool showZomCount = true;
    [SerializeField] private bool showRemainingTime = true;
    [SerializeField] private bool showWave = true;

    private void OnEnable() => ApplyAll();

    private void ApplyAll()
    {
        Apply(fpsPanel, showFps);
        Apply(zomCountPanel, showZomCount);
        Apply(remainingTimePanel, showRemainingTime);
        Apply(wavePanel, showWave);
    }

    private void Apply(GameObject panel, bool shouldShow)
    {
        if (panel == null) return;
        if (panel.activeSelf != shouldShow) panel.SetActive(shouldShow);
    }

    #region Public Toggles
    public void SetFpsVisible(bool v) { showFps = v; Apply(fpsPanel, v); }
    public void SetZomCountVisible(bool v) { showZomCount = v; Apply(zomCountPanel, v); }
    public void SetRemainingTimeVisible(bool v) { showRemainingTime = v; Apply(remainingTimePanel, v); }
    public void SetWaveVisible(bool v) { showWave = v; Apply(wavePanel, v); }
    #endregion
}