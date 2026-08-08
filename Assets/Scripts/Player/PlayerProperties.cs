using Server;
using System;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(999)]
public class PlayerProperties : MonoBehaviour
{
    UnitAttribute a => PlayerManager.instance.attribute;

    private void Awake()
    {
        EventBus.OnGameRestart += Refresh;
        EventBus.OnWaveCleared += GetPoint;
    }
    private void OnDestroy()
    {
        EventBus.OnGameRestart -= Refresh;
        EventBus.OnWaveCleared -= GetPoint;
    }
    private void OnEnable()
    {
        UpdateStatus();
    }

    #region Visual
    [Header("Point")]
    public TextMeshProUGUI UpgradePoint;

    [Header("Properties")]
    public TextMeshProUGUI HPShower;
    public TextMeshProUGUI ATKShower;
    public TextMeshProUGUI DEFShower;
    public TextMeshProUGUI SPEEDShower;
    public TextMeshProUGUI SIGHTShower;

    private void Refresh()
    {
        Up_Point = 3;
        Up_HP = Up_ATK = Up_DEF = Up_SPEED = Up_SIGHT = 0f;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (a == null) return;

        a.HP_Ampl.FlatBonus += Up_HP - hp;
        a.ATK_Ampl.FlatBonus += Up_ATK - atk;
        a.DEF_Ampl.FlatBonus += Up_DEF - def;
        a.SPEED_Ampl.FlatBonus += Up_SPEED - speed;
        a.SIGHT_Ampl.FlatBonus += Up_SIGHT - sight;

        hp = Up_HP; atk = Up_ATK; def = Up_DEF; speed = Up_SPEED; sight = Up_SIGHT;

        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (UpgradePoint != null) UpgradePoint.text = Up_Point.ToString("F0");
        if (HPShower != null) HPShower.text = a.HP_Max.ToString("F0");
        if (ATKShower != null) ATKShower.text = a.ATK_Current.ToString("F0");
        if (DEFShower != null) DEFShower.text = a.DEF_Current.ToString("F0");
        if (SPEEDShower != null) SPEEDShower.text = a.SPEED_Current.ToString("F0");
        if (SIGHTShower != null) SIGHTShower.text = a.SIGHT_Current.ToString("F0");
    }
    #endregion

    #region Upgrader
    public int Up_Point=3;
    private float Up_HP;
    private float Up_ATK;
    private float Up_DEF;
    private float Up_SPEED;
    private float Up_SIGHT;

    private float hp, atk, def, speed, sight;

    private void GetPoint()
    {
        float luck = RNG.GetPercent();
        int reward=0;

        if (luck < 0.05f) reward = 4;
        else if (luck < 0.15f) reward = 3;
        else if (luck < 0.35f) reward = 2;
        else reward = 1;

        Up_Point += reward;
        UpdateStatus();
    }
    public void UpgradeHP()
    {
        if (Up_Point <= 0) return;
        Up_HP+=20;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeATK()
    {
        if (Up_Point <= 0) return;
        Up_ATK+=2;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeDEF()
    {
        if (Up_Point <= 0) return;
        Up_DEF+=50;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeSPEED()
    {
        if (Up_Point <= 0) return;
        Up_SPEED+=0.25f;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeSIGHT()
    {
        if (Up_Point <= 0) return;
        Up_SIGHT+=1;
        Up_Point--;
        UpdateStatus();
    }
    #endregion
}