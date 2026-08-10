using Server;
using System;
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

    private void Refresh()
    {
        Up_Point = DefaultPoint;
        a.HP_Ampl.FlatBonus = a.ATK_Ampl.FlatBonus = a.DEF_Ampl.FlatBonus = a.SPEED_Ampl.FlatBonus = a.SIGHT_Ampl.FlatBonus = a.ArmourPenetration_Perc = 0;
        Up_HP = Up_ATK = Up_DEF = Up_SPEED = Up_SIGHT = Up_AP = 0f;
        hp = atk = def = speed = sight = ap = 0f;
    }

    public void UpdateStatus()
    {
        if (a == null) return;

        a.HP_Ampl.FlatBonus += Up_HP - hp;
        a.ATK_Ampl.FlatBonus += Up_ATK - atk;
        a.DEF_Ampl.FlatBonus += Up_DEF - def;
        a.SPEED_Ampl.FlatBonus += Up_SPEED - speed;
        a.SIGHT_Ampl.FlatBonus += Up_SIGHT - sight;
        a.ArmourPenetration_Perc += Up_AP - ap;

        hp = Up_HP; atk = Up_ATK; def = Up_DEF; speed = Up_SPEED; sight = Up_SIGHT; ap = Up_AP;

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
    private const int DefaultPoint = 5;
    private const float Growth_HP = 15;
    private const float Growth_ATK = 1.5f;
    private const float Growth_DEF = 30f;
    private const float Growth_SPEED = 0.5f;
    private const float Growth_SIGHT = 0.5f;
    private const float Growth_AP = 0.003f;

    private int Up_Point;
    private float Up_HP;
    private float Up_ATK;
    private float Up_DEF;
    private float Up_SPEED;
    private float Up_SIGHT;
    private float Up_AP;

    private float hp, atk, def, speed, sight,ap;
    private int pity;
    private float interval=6;

    private void GetPoint()
    {
        if (interval > Time.time) return;
        interval = Time.time + 6f;

        int reward = RollReward();
        float multiplier = Mathf.Max(reward, reward * DomainManager.instance.CurrentDifficulty/2);
        int finalReward = Mathf.Min(12, Mathf.FloorToInt(multiplier));

        Up_Point += finalReward;
        UpdateStatus();
    }

    private int RollReward()
    {
        float luck = RNG.GetPercent();
        if (pity >= 10) { pity = 0; return 5; }

        pity++;
        if (luck < 0.05f) { pity = 0; return 5; }
        if (luck < 0.20f) return 4;                  
        if (luck < 0.5f) return 3;
        return 2;
    }

    public void UpgradeHP()
    {
        if (Up_Point <= 0) return;
        Up_HP += Growth_HP * Mathf.Max(1, Mathf.Pow(dm.CurrentDifficulty,1.3f));
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeATK()
    {
        if (Up_Point <= 0) return;
        Up_ATK += Growth_ATK * Mathf.Max(1, Mathf.Pow(dm.CurrentDifficulty, 1.3f));
        Up_AP += Growth_AP;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeDEF()
    {
        if (Up_Point <= 0) return;
        Up_DEF+=Growth_DEF;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeSPEED()
    {
        if (Up_Point <= 0) return;
        if (Up_SPEED >= 5) return;
        Up_SPEED+=Growth_SPEED;
        Up_Point--;
        UpdateStatus();
    }
    public void UpgradeSIGHT()
    {
        if (Up_Point <= 0) return;
        if (Up_SIGHT >= 10) return;
        Up_SIGHT+=Growth_SIGHT;
        Up_Point--;
        UpdateStatus();
    }
    #endregion
}