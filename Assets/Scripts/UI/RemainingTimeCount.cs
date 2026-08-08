using TMPro;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class RTimeCount : MonoBehaviour
{
    public TextMeshProUGUI tmp;

    private DomainManager dm => DomainManager.instance;
    private float rt = 1;

    private void Update()
    {
        if (tmp == null) return;
        if (rt == dm.SecondBeforeNextWave) return;
        rt = dm.SecondBeforeNextWave + dm.PreparingTime;
        tmp.text = $"{rt.ToString("F0")}";
    }
}