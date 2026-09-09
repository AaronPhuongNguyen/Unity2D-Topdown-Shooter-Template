using UnityEngine;

public class StoreManager : MonoBehaviour
{
    public DomainManager dm => DomainManager.instance;
    public StoreUpgrade su;

    #region Singleton
    public static StoreManager Instance { get; private set; }
    private void DoSingleTon()
    {
        if(Instance!=null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (su == null) su = FindFirstObjectByType<StoreUpgrade>();
    }
    #endregion

    #region Unity
    private void Awake()
    {
        DoSingleTon();
    }
    #endregion

    #region Shopping
    public void PurchaseThisItem(int price, out bool purchased)
    {
        if (dm == null || dm.Currency < price)
        {
            purchased = false;
            return;
        }
        dm.CostCurrency(price);
        purchased = true;
    }
    public void SellThisItem(int price, out bool sold)
    {
        if (dm == null)
        {
            sold = false;
            return;
        }
        dm.AddCurrency(price * 0.75f);
        sold = true;
    }
    #endregion
}