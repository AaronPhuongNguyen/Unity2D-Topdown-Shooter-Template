using System;
using TMPro;
using UnityEngine;

public class StoreUpgrade : MonoBehaviour
{
    StoreManager sm => StoreManager.Instance;
    PlayerManager pm => PlayerManager.instance;

    #region Constant growth value
    public UpgradeValue HP = new(1500, 0.04f, 50);
    public UpgradeValue ATK = new(1500, 0.04f, 50);

    public UpgradeValue Recovery = new(5000, 0.005f, 10);
    public UpgradeValue Money = new(2000, 1, 25);

    public event Action OnUpgraded, OnReset;
    #endregion

    #region Cache
    #endregion

    #region Visual
    public TextMeshProUGUI HPShower;
    #endregion

    #region Upgrade
    #region Reset
    [ContextMenu("Reset")]
    public void ResetAll()
    {
        ResetSingle(HP);
        ResetSingle(ATK);
        ResetSingle(Recovery);
        ResetSingle(Money);

        if (pm != null)
        {
            pm.attribute.HP_Ampl.TotalBonus -= HP.lastGrowth;
            pm.attribute.ATK_Ampl.TotalBonus -= ATK.lastGrowth;
        }

        HP.lastGrowth = ATK.lastGrowth = Recovery.lastGrowth = Money.lastGrowth = 0;

        HP.UpgradeTimes = ATK.UpgradeTimes = Recovery.UpgradeTimes = Money.UpgradeTimes = 0;

        OnReset?.Invoke();
    }

    void ResetSingle(UpgradeValue uv)
    {
        
    }
    #endregion
    public void Upgrade(UpgradeValue uv, out bool upgraded)
    {
        upgraded = false;

        if (sm == null) return;

        if (uv.UpgradeTimes >= uv.MaxUpgrade)
        {
            upgraded = false;
            return;
        }

        sm.PurchaseThisItem(uv.PriceDefault, out upgraded);
        OnUpgraded?.Invoke();
    }
    public void UpgradeHP()
    {
        if (pm == null) return;
        Upgrade(HP, out bool succeeded);
        if (!succeeded) return;

        HP.UpgradeTimes++;
        float Up = HP.Growth * HP.UpgradeTimes;

        pm.attribute.HP_Ampl.TotalBonus += Up - HP.lastGrowth;
        HP.lastGrowth = Up;
    }
    public void UpgradeATK()
    {
        if (pm == null) return;
        Upgrade(ATK, out bool succeeded);
        if (!succeeded) return;

        ATK.UpgradeTimes++;
        float Up = ATK.Growth * ATK.UpgradeTimes;

        pm.attribute.ATK_Ampl.TotalBonus += Up - ATK.lastGrowth;
        ATK.lastGrowth = Up;
    }
    public void UpgradeRecovery()
    {
        if (pm == null) return;
        Upgrade(Recovery, out bool succeeded);
        if (!succeeded) return;

        Recovery.UpgradeTimes++;
        //handled by other class
    }
    public void UpgradeMoney()
    {
        if (pm == null) return;
        Upgrade(Money, out bool succeeded);
        if (!succeeded) return;

        Money.UpgradeTimes++;
    }
    #endregion

    #region Unity
    private void OnEnable()
    {
        EventBus.OnGameRestart += ResetAll;
    }
    private void OnDisable()
    {
        EventBus.OnGameRestart -= ResetAll;
    }
    private void Start()
    {
        ResetAll();
    }
    #endregion

    #region Debug
    [ContextMenu("Up Heal")]
    private void UpHeal() => Recovery.UpgradeTimes = Recovery.MaxUpgrade;
    [ContextMenu("Up Money")]
    private void UpMoney() => Money.UpgradeTimes = Money.MaxUpgrade;
    [ContextMenu("Up HP")]
    private void UpHP()
    {
        sm.dm.AddCurrency(HP.PriceDefault * HP.MaxUpgrade);
        for (int i = 0; i < HP.MaxUpgrade; i++) UpgradeHP();
    }
    [ContextMenu("Up ATK")]
    private void UpATK()
    {
        sm.dm.AddCurrency(ATK.PriceDefault * ATK.MaxUpgrade);
        for (int i = 0; i < ATK.MaxUpgrade; i++) UpgradeATK();
    }
    #endregion
}

public class UpgradeValue
{
    public int PriceDefault;
    public int MaxUpgrade;
    public int UpgradeTimes;

    public float Growth;
    public float lastGrowth;

    public UpgradeValue(int price, float growthValue, int maxUpgrade)
    {
        PriceDefault = price;
        MaxUpgrade = maxUpgrade;
        Growth = growthValue;  
        UpgradeTimes = 0;
        lastGrowth = 0;
    }
}