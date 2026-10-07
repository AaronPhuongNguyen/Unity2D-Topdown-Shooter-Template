using TMPro;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class WaveCount : MonoBehaviour, ITick
{
    public TextMeshProUGUI tmp;
    public TextMeshProUGUI diff;

    private DomainManager dm => DomainManager.instance;
    private int waveCount = 0;
    private float diffs = 0;

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }

    public void Tick(float delta)
    {
        if (dm == null) return;

        ShowWaveCount();
        ShowDifficulty();
    }

    void ShowWaveCount()
    {
        if (tmp == null) return;
        if (waveCount == dm.CurrentWave) return;
        waveCount = dm.CurrentWave;
        tmp.text = waveCount.ToString();
    }

    void ShowDifficulty()
    {
        if (diff == null) return;
        if (diffs == dm.CurrentDifficulty) return;
        diffs = dm.CurrentDifficulty;
        diff.text = diffs.ToString("F2");
    }
}