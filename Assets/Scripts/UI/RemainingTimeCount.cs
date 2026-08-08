using TMPro;
using UnityEngine;
[DefaultExecutionOrder(100)]
public class RTimeCount : MonoBehaviour
{
    [Header("Text Refs")]
    public TextMeshProUGUI remainingTimeTmp;
    public TextMeshProUGUI passedTimeTmp;   

    private DomainManager dm => DomainManager.instance;

    private float lastRemaining = -1f;
    private float elapsed;
    private int lastElapsedSecond = -1;

    private void Update()
    {
        if (dm == null) return;

        UpdateRemainingTime();
        UpdatePassedTime();
    }

    private void UpdateRemainingTime()
    {
        if (remainingTimeTmp == null) return;

        float remaining = dm.SecondBeforeNextWave + dm.PreparingTime;
        if (Mathf.Approximately(Mathf.Floor(remaining), lastRemaining)) return;
        lastRemaining = Mathf.Floor(remaining);

        remainingTimeTmp.text = FormatMinuteSeconds(remaining);
    }

    private void UpdatePassedTime()
    {
        if (passedTimeTmp == null) return;

        elapsed += Time.deltaTime;

        int wholeSecond = Mathf.FloorToInt(elapsed);
        if (wholeSecond == lastElapsedSecond) return; 
        lastElapsedSecond = wholeSecond;

        passedTimeTmp.text = FormatMinuteSeconds(elapsed);
    }

    private string FormatMinuteSeconds(float totalSeconds)
    {
        if (totalSeconds < 0f) totalSeconds = 0f;

        int minutes = Mathf.FloorToInt(totalSeconds / 60f);
        int seconds = Mathf.FloorToInt(totalSeconds % 60f);
        return $"{minutes:00}:{seconds:00}";
    }
    public void ResetElapsed()
    {
        elapsed = 0f;
        lastElapsedSecond = -1;
    }
}