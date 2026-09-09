using Server;
using System;
using System.Collections;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(999)]
public class PlayerProperties : MonoBehaviour
{
    [SerializeField] private PropertiesVisual pv;
    UnitAttribute a => PlayerManager.instance.attribute;
    DomainManager dm => DomainManager.instance;

    private void Awake()
    {
        Up_Point = DefaultPoint;
        EventBus.OnGameRestart += Refresh;
        EventBus.OnWaveCleared += GetPoint;

        if (pv != null) pv.pp = this;
    }
    private void OnDestroy()
    {
        EventBus.OnGameRestart -= Refresh;
        EventBus.OnWaveCleared -= GetPoint;
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
    public TextMeshProUGUI CDRShower;

    private void Refresh()
    {
        Up_Point = DefaultPoint;
        a.HP_Ampl.FlatBonus = a.ATK_Ampl.FlatBonus = a.DEF_Ampl.FlatBonus = a.SPEED_Ampl.FlatBonus = a.SIGHT_Ampl.FlatBonus = a.CooldownReduction_Ampl.FlatBonus = 0;
        Up_HP = Up_ATK = Up_DEF = Up_SPEED = Up_SIGHT = Up_AP = Up_HPP = Up_CDR = 0f;
        hp = atk = def = speed = sight = ap = hpp = cdr = 0f;
    }

    public void UpdateStatus()
    {
        if (a == null) return;

        a.HP_Ampl.FlatBonus += Up_HP - hp;
        a.ATK_Ampl.FlatBonus += Up_ATK - atk;
        a.DEF_Ampl.FlatBonus += Up_DEF - def;
        a.SPEED_Ampl.FlatBonus += Up_SPEED - speed;
        a.SIGHT_Ampl.FlatBonus += Up_SIGHT - sight;
        a.ATK_Ampl.PercBonus += Up_AP - ap;
        a.HP_Ampl.PercBonus += Up_HPP - hpp;
        a.CooldownReduction_Ampl.FlatBonus += Up_CDR - cdr;

        hp = Up_HP; atk = Up_ATK; def = Up_DEF; speed = Up_SPEED;
        sight = Up_SIGHT; ap = Up_AP; hpp = Up_HPP; cdr = Up_CDR;

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
        if (CDRShower != null) CDRShower.text = (a.CooldownReduction_Current * 100).ToString("F0") + "%";
    }
    #endregion

    #region Upgrader
    private const int DefaultPoint = 5;
    private const float Growth_HP = 20;
    private const float Growth_ATK = 2f;
    private const float Growth_DEF = 30f;
    private const float Growth_SPEED = 1f;
    private const float Growth_SIGHT = 1f;
    private const float Growth_AP = 0.02f;
    private const float Growth_HPP = 0.02f;
    private const float Growth_CDR = 0.02f;

    private const float Cap_SPEED = 10f;
    private const float Cap_SIGHT = 20f;
    private const float Cap_CDR = 0.6f;

    private int Up_Point;
    private float Up_HP;
    private float Up_ATK;
    private float Up_DEF;
    private float Up_SPEED;
    private float Up_SIGHT;
    private float Up_AP;
    private float Up_HPP;
    private float Up_CDR;

    private float hp, atk, def, speed, sight, ap, hpp, cdr;
    private int pity;
    private float interval = 3;

    private void GetPoint()
    {
        if (interval > Time.time) return;
        interval = Time.time + 3f;

        int reward = RollReward();
        float multiplier = Mathf.Max(reward, reward * DomainManager.instance.CurrentDifficulty * RNG.GetInt(0, 2));
        int finalReward = Mathf.Min(15, Mathf.FloorToInt(multiplier));

        Up_Point += finalReward;
        UpdateStatus();
    }

    private int RollReward()
    {
        float luck = RNG.GetPercent();
        if (pity >= 6) { pity = 0; return 6; }

        pity++;
        if (luck < 0.05f) { pity = 0; return 6; }
        if (luck < 0.20f) return 5;
        if (luck < 0.5f) return 4;
        return 3;
    }

    public void UpgradeHP()
    {
        if (Up_Point <= 0) return;
        Up_HP += Growth_HP * Mathf.Max(1, dm.CurrentDifficulty);
        Up_HPP += Growth_HPP;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeATK()
    {
        if (Up_Point <= 0) return;
        Up_ATK += Growth_ATK * Mathf.Max(1, dm.CurrentDifficulty);
        Up_AP += Growth_AP;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeDEF()
    {
        if (Up_Point <= 0) return;
        Up_DEF += Growth_DEF;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeSPEED()
    {
        if (Up_Point <= 0 || Up_SPEED >= Cap_SPEED) return;
        Up_SPEED = Mathf.Min(Up_SPEED + Growth_SPEED, Cap_SPEED);
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeSIGHT()
    {
        if (Up_Point <= 0 || Up_SIGHT >= Cap_SIGHT) return;
        Up_SIGHT = Mathf.Min(Up_SIGHT + Growth_SIGHT, Cap_SIGHT);
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeCooldownReduction()
    {
        if (Up_Point <= 0 || Up_CDR >= Cap_CDR) return;
        Up_CDR = Mathf.Min(Up_CDR + Growth_CDR, Cap_CDR);
        Up_Point--;
        UpdateStatus();
    }
    #endregion

    #region Debugger
    [ContextMenu("AddStars")]
    private void AddStars() => Up_Point += 99;
    [ContextMenu("Auto Assign Point")]
    private void Assign()
    {
        while (Up_Point > 0)
        {
            UpgradeHP();
            UpgradeATK();
            UpgradeSIGHT();
            UpgradeSPEED();
            UpgradeCooldownReduction();
        }
    }
    #endregion
}