using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple toggle button to switch PlayerManager.AutoAttack on/off.
/// Attach to a UI Button (or any object with a Button component).
/// </summary>
public class AutoAttackToggle : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label; // optional - shows current state
    [SerializeField] private Image icon;             // optional - swap sprite by state
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;

    private PlayerManager pm => PlayerManager.instance;

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
        if (pm == null) return;

        pm.AutoAttack = !pm.AutoAttack;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (pm == null) return;

        if (label != null)
            label.text = pm.AutoAttack ? "Auto Attack: ON" : "Auto Attack: OFF";

        if (icon != null)
            icon.sprite = pm.AutoAttack ? onSprite : offSprite;
    }
}