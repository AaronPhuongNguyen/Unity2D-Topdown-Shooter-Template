using TMPro;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class WaveCount : MonoBehaviour
{
    public TextMeshProUGUI tmp;
    public TextMeshProUGUI diff;

    private DomainManager dm => DomainManager.instance;
    private int waveCount = 1;
    private float diffs = 1;

    private void Update()
    {
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
        diff.text = diffs.ToString("F1");
    }
}