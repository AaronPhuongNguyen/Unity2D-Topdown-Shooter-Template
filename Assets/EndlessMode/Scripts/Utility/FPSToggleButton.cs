using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple toggle button to switch between 60 FPS and a lower target (e.g.
/// 30) via FpsForcer. Useful as a battery-saver / thermal option on mobile.
/// </summary>
public class FpsToggle : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image icon;
    [SerializeField] private Sprite highSprite; // 60
    [SerializeField] private Sprite lowSprite;  // 30

    [SerializeField] private int highFps = 60;
    [SerializeField] private int lowFps = 30;

    private bool isHigh = true;

    private void Reset()
    {
        if (button == null) button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(Toggle);

        RefreshVisual();
    }

    private void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(Toggle);
    }

    public void Toggle()
    {
        isHigh = !isHigh;

        if (FpsForcer.instance != null)
            FpsForcer.instance.SetTargetFPS(isHigh ? highFps : lowFps);

        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (label != null)
            label.text = isHigh ? $"{highFps} FPS" : $"{lowFps} FPS";

        if (icon != null)
            icon.sprite = isHigh ? highSprite : lowSprite;
    }
}