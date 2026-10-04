using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple toggle button to switch camera shake on/off. Shake() calls from
/// anywhere (hits, deaths, etc.) still fire normally - this toggle just
/// gates whether CamManager actually applies the offset, so players who
/// get motion-sick or just prefer a steady camera can turn it off.
/// </summary>
public class CameraShakeToggle : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image icon;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;

    private bool isOn = true;

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
        isOn = !isOn;

        if (CamManager.instance != null)
            CamManager.instance.ShakeEnabled = isOn;

        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (label != null)
            label.text = isOn ? "Shake: ON" : "Shake: OFF";

        if (icon != null)
            icon.sprite = isOn ? onSprite : offSprite;
    }
}