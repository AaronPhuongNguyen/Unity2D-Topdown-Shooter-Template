using UnityEngine;

[DefaultExecutionOrder(999)]
public class MoneyOverTime : MonoBehaviour, ITick
{
    #region Cache
    StoreManager sm => StoreManager.Instance;
    DomainManager dm => DomainManager.instance;

    // Was Time.time-based (ignores GameSpeed/pause). Now a plain
    // accumulator driven by the delta passed into Tick.
    float timer;
    #endregion

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }

    private bool isActive()
    {
        if (dm == null) return false;
        if (sm == null) return false;
        if (sm.su == null) return false;
        if (sm.su.Money.UpgradeTimes <= 0) return false;
        return true;
    }

    public void Tick(float delta)
    {
        if (!isActive()) return;

        timer -= delta;
        if (timer > 0f) return;
        timer = 1.5f;

        TryGive();
    }

    private void TryGive()
    {
        float up = sm.su.Money.Growth * sm.su.Money.UpgradeTimes;
        dm.AddCurrency(up);
    }
}