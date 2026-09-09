using UnityEngine;

[DefaultExecutionOrder(999)]
public class RecoverOverTime : MonoBehaviour
{
    #region Cache
    StoreManager sm => StoreManager.Instance;
    PlayerManager pm => PlayerManager.instance;

    float interval;
    #endregion
    private bool isActive()
    {
        if (pm == null) return false;
        if(sm == null) return false;
        if(sm.su == null) return false;
        if(sm.su.Recovery.UpgradeTimes <= 0) return false;
        return true;
    }
    private void Update()
    {
        if (!isActive()) return;
        if (interval > Time.time) return;
        interval = Time.time + 1.5f;

        TryHeal();
    }
    private void TryHeal()
    {
        float up = sm.su.Recovery.Growth * sm.su.Recovery.UpgradeTimes;
        float value = up * pm.attribute.HP_Max;

        pm.Heal(value);
    }
}