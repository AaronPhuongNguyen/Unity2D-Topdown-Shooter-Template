using UnityEngine;

[DefaultExecutionOrder(999)]
public class MoneyOverTime : MonoBehaviour
{
    #region Cache
    StoreManager sm => StoreManager.Instance;
    DomainManager dm => DomainManager.instance;

    float interval;
    #endregion
    private bool isActive()
    {
        if (dm == null) return false;
        if(sm == null) return false;
        if(sm.su == null) return false;
        if(sm.su.Money.UpgradeTimes <= 0) return false;
        return true;
    }
    private void Update()
    {
        if (!isActive()) return;
        if (interval > Time.time) return;
        interval = Time.time + 1.5f;

        TryGive();
    }
    private void TryGive()
    {
        float up = sm.su.Money.Growth * sm.su.Money.UpgradeTimes;
        dm.AddCurrency(up);        
    }
}