using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StoreUpgrade : MonoBehaviour
{
    StoreManager sm => StoreManager.Instance;
    PlayerManager pm => PlayerManager.instance;

    #region Constant growth value
    public UpgradeValue HP = new(1250, 0.03f, 50);
    public UpgradeValue ATK = new(1250, 0.03f, 50);

    public UpgradeValue Recovery = new(5000, 0.01f, 15);
    public UpgradeValue Money = new(5000, 1, 50);

    public event Action OnUpgraded, OnReset;
    #endregion

    #region Cache
    #endregion

    #region Visual
    public TextMeshProUGUI HPShower;
    public Slider HPSlider;
    public TextMeshProUGUI HPPriceShower;

    public TextMeshProUGUI ATKShower;
    public Slider ATKSlider;
    public TextMeshProUGUI ATKPriceShower;

    public TextMeshProUGUI RecoveryShower;
    public Slider RecoverySlider;
    public TextMeshProUGUI RecoveryPriceShower;

    public TextMeshProUGUI MoneyShower;
    public Slider MoneySlider;
    public TextMeshProUGUI MoneyPriceShower;

    public void UpdateVisual()
    {
        UpdateSingleVisual(HP, HPShower, HPSlider, HPPriceShower);
        UpdateSingleVisual(ATK, ATKShower, ATKSlider, ATKPriceShower);
        UpdateSingleVisual(Recovery, RecoveryShower, RecoverySlider, RecoveryPriceShower);
        UpdateSingleVisual(Money, MoneyShower, MoneySlider, MoneyPriceShower);
    }

    void UpdateSingleVisual(UpgradeValue uv, TextMeshProUGUI shower, Slider slider, TextMeshProUGUI priceShower)
    {
        if (shower != null)
            shower.text = (uv.MaxUpgrade > 0 ? (uv.UpgradeTimes / (float)uv.MaxUpgrade * 100f) : 0f).ToString("F0") + " %";

        if (slider != null)
        {
            slider.maxValue = uv.MaxUpgrade;
            slider.value = uv.UpgradeTimes;
        }

        if (priceShower != null)
        {
            if (uv.UpgradeTimes >= uv.MaxUpgrade)
            {
                priceShower.text = "MAX";
                priceShower.color = Color.yellow;
            }
            else
            {
                int price = uv.GetCurrentPrice();
                priceShower.text = FormatPrice(price);

                bool canAfford = sm != null && sm.dm != null && sm.dm.Currency >= price;
                priceShower.color = canAfford ? Color.white : Color.red;
            }
        }
    }

    string FormatPrice(int price)
    {
        if (price >= 1_000_000)
            return "$ " + (price / 1_000_000f).ToString("0.#") + "M";
        if (price >= 1_000)
            return "$ " + (price / 1_000f).ToString("0.#") + "K";
        return price.ToString();
    }
    #endregion

    #region Upgrade
    public void Upgrade(UpgradeValue uv, out bool upgraded)
    {
        upgraded = false;

        if (sm == null) return;

        if (uv.UpgradeTimes >= uv.MaxUpgrade)
        {
            upgraded = false;
            return;
        }

        int price = uv.GetCurrentPrice();
        sm.PurchaseThisItem(price, out upgraded);
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

        OnUpgraded?.Invoke();
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

        OnUpgraded?.Invoke();
    }

    public void UpgradeRecovery()
    {
        if (pm == null) return;
        Upgrade(Recovery, out bool succeeded);
        if (!succeeded) return;

        Recovery.UpgradeTimes++;
        //handled by other class

        OnUpgraded?.Invoke();
    }

    public void UpgradeMoney()
    {
        if (pm == null) return;
        Upgrade(Money, out bool succeeded);
        if (!succeeded) return;

        Money.UpgradeTimes++;

        OnUpgraded?.Invoke();
    }
    #endregion

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
        OnUpgraded?.Invoke();
    }

    void ResetSingle(UpgradeValue uv)
    {
        uv.UpgradeTimes = 0;
        uv.lastGrowth = 0;
    }
    #endregion

    #region Unity
    private void OnEnable()
    {
        UpdateVisual();
    }

    private void OnDisable()
    {
        UpdateVisual();
    }

    private void Start()
    {
        ResetAll();
        EventBus.OnGameRestart += ResetAll;
        OnUpgraded += UpdateVisual;
    }
    private void OnDestroy()
    {
        EventBus.OnGameRestart -= ResetAll;
        OnUpgraded -= UpdateVisual;
    }
    #endregion

    #region Debug
    [ContextMenu("Add Full Money")]
    private void AddMoney() => DomainManager.instance.AddCurrency(999999);

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

    public float PriceGrowthRate = 1.05f;

    public UpgradeValue(int price, float growthValue, int maxUpgrade)
    {
        PriceDefault = price;
        MaxUpgrade = maxUpgrade;
        Growth = growthValue;
        UpgradeTimes = 0;
        lastGrowth = 0;
    }

    public int GetCurrentPrice()
    {
        return Mathf.RoundToInt(PriceDefault * Mathf.Pow(PriceGrowthRate, UpgradeTimes));
    }
}