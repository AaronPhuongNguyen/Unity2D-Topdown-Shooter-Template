using Server;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(500)]
public class PlayerHP : MonoBehaviour
{
    public TextMeshProUGUI hp;
    public Slider red;
    public Slider yellow;
    public Slider green;
    public float SlideSpeed = 3f;

    private PlayerManager pm => PlayerManager.instance;
    private float lastHp,lastMaxHP;

    private void Update()
    {
        Checker();

        UpdateHP();
        BlueLerp();
        RedLerp();
        YellowLerp();
    }
    private void Checker()
    {
        if (red == null || yellow == null || green == null) return;
        if (pm == null) return;

        if (lastMaxHP != pm.attribute.HP_Max) lastMaxHP = pm.attribute.HP_Max;
        if (red.maxValue != lastMaxHP) red.maxValue = lastMaxHP;
        if (yellow.maxValue != lastMaxHP) yellow.maxValue = lastMaxHP;
        if (green.maxValue != lastMaxHP) green.maxValue = lastMaxHP;

        if (lastHp != pm.attribute.HP_Current) lastHp = pm.attribute.HP_Current;
    }
    private void UpdateHP()
    {
        if (hp == null) return;
        if (pm == null) return;

        hp.text = $"{pm.attribute.HP_Current.ToString("F0")} / {pm.attribute.HP_Max.ToString("F0")}";
    }
    private void BlueLerp()
    {
        if (green == null) return;
        if (green.value != lastHp) green.value = MoveSlide(green.value, lastHp, 1f);
    }
    private void RedLerp()
    {
        if(red == null) return;
        if (pm.healShockDuration > 0) return;
        if (red.value != lastHp) red.value = MoveSlide(red.value, lastHp, SlideSpeed * Time.deltaTime);
    }
    private void YellowLerp()
    {
        if (pm.combatDuration > 0) return;
        if (yellow.value != red.value) yellow.value = MoveSlide(yellow.value, lastHp, SlideSpeed * SlideSpeed * Time.deltaTime);
    }
    private float MoveSlide(float o,float d,float speed)
    {
        return Mathf.Lerp(o, d, speed);
    }
}