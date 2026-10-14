using Server;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(500)]
public class PlayerHP : MonoBehaviour, ITick
{
    public TextMeshProUGUI hp;
    public Slider red;
    public Slider yellow;
    public Slider green;
    public float SlideSpeed = 3f;

    [Header("Energy (optional)")]
    [Tooltip("Image with Image Type = Filled.")]
    public Image energy;
    [Tooltip("How fast the energy bar eases to the real value.")]
    public float EnergySlideSpeed = 12f;

    private PlayerManager pm => PlayerManager.instance;
    private float lastHp, lastMaxHP;

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);

        // Snap the energy bar instead of easing from a stale value.
        if (energy != null && pm != null && pm.Energy != null)
            energy.fillAmount = pm.Energy.Normalized;
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }

    public void Tick(float delta)
    {
        if (pm == null || pm.attribute == null) return;

        Checker();

        UpdateHP();
        BlueLerp();
        RedLerp(delta);
        YellowLerp(delta);
        UpdateEnergy(delta);
    }

    private void Checker()
    {
        if (red == null || yellow == null || green == null) return;

        if (lastMaxHP != pm.attribute.HP_Max) lastMaxHP = pm.attribute.HP_Max;
        if (red.maxValue != lastMaxHP) red.maxValue = lastMaxHP;
        if (yellow.maxValue != lastMaxHP) yellow.maxValue = lastMaxHP;
        if (green.maxValue != lastMaxHP) green.maxValue = lastMaxHP;

        if (lastHp != pm.attribute.HP_Current) lastHp = pm.attribute.HP_Current;
    }

    private void UpdateHP()
    {
        if (hp == null) return;

        hp.text = $"{pm.attribute.HP_Current.ToString("F0")} / {pm.attribute.HP_Max.ToString("F0")}";
    }

    private void BlueLerp()
    {
        if (green == null) return;
        if (green.value != lastHp) green.value = MoveSlide(green.value, lastHp, 1f);
    }

    private void RedLerp(float delta)
    {
        if (red == null) return;
        if (red.value != lastHp) red.value = MoveSlide(red.value, lastHp, SlideSpeed * delta);
    }

    private void YellowLerp(float delta)
    {
        if (yellow == null || red == null) return;
        if (pm.combatDuration > 0) return;
        if (yellow.value != red.value) yellow.value = MoveSlide(yellow.value, lastHp, SlideSpeed * SlideSpeed * delta);
    }

    private void UpdateEnergy(float delta)
    {
        if (energy == null || pm.Energy == null) return;

        float target = pm.Energy.Normalized;

        if (!Mathf.Approximately(energy.fillAmount, target))
            energy.fillAmount = MoveSlide(energy.fillAmount, target, Mathf.Clamp01(EnergySlideSpeed * delta));
    }

    private float MoveSlide(float o, float d, float speed)
    {
        return Mathf.Lerp(o, d, speed);
    }
}