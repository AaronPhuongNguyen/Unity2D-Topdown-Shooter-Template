using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD for the player's level: a text showing the level and a filled image used as the EXP bar.
/// Reads PlayerManager.Level every tick, so it always stays in sync (level-ups, new game, respawn).
/// Uses the unscaled tick so the bar keeps animating even while the game is paused (e.g. card selection).
/// </summary>
public class LevelUI : MonoBehaviour, IUnscaledTick
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI levelText;
    [Tooltip("Image used as the EXP bar. It is switched to Filled mode automatically (needs a sprite assigned).")]
    [SerializeField] private Image expFill;

    [Header("Look")]
    [Tooltip("How fast the bar catches up to the real EXP. 0 = instant.")]
    [Min(0f)][SerializeField] private float fillSpeed = 6f;

    private int lastLevel = -1;
    private float shownFill;

    private void OnEnable()
    {
        lastLevel = -1; // force a refresh on enable
        TickSystem.Register((IUnscaledTick)this);
    }

    private void OnDisable()
    {
        TickSystem.Unregister((IUnscaledTick)this);
    }

    public void UnscaledTick(float deltaTime)
    {
        PlayerManager pm = PlayerManager.instance;
        if (pm == null || pm.Level == null) return;

        UnitLevel lv = pm.Level;

        // Text: only rebuild the string when the level actually changes (no per-frame garbage).
        if (lv.Level_Current != lastLevel)
        {
            bool leveledUp = lastLevel != -1 && lv.Level_Current > lastLevel;
            lastLevel = lv.Level_Current;

            if (levelText != null)
                levelText.text = lv.Level_Current.ToString("F0");

            // New level (or reset): the bar starts over instead of sliding backwards from full.
            if (leveledUp || lv.Level_Current <= 1)
                shownFill = 0f;
        }

        if (expFill == null) return;

        float needed = lv.EXP_ToUpLevel;
        float target = needed > 0f ? Mathf.Clamp01(lv.EXP_Current / needed) : 0f;

        shownFill = fillSpeed <= 0f
            ? target
            : Mathf.Lerp(shownFill, target, fillSpeed * deltaTime);

        expFill.fillAmount = shownFill;
    }
}