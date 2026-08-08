using TMPro;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class WaveCount : MonoBehaviour
{
    public TextMeshProUGUI tmp;

    private DomainManager dm => DomainManager.instance;
    private int waveCount = 1;

    private void Update()
    {
        if (tmp == null) return;
        if (waveCount == dm.CurrentWave) return;
        waveCount = dm.CurrentWave;
        tmp.text = $"{waveCount}";
    }
}