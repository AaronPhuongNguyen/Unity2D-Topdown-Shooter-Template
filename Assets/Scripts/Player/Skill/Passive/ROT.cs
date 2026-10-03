using UnityEngine;

[DefaultExecutionOrder(999)]
public class RecoverOverTime : MonoBehaviour, ITick
{
    #region Cache
    StoreManager sm => StoreManager.Instance;
    PlayerManager pm => PlayerManager.instance;

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
        if (pm == null) return false;
        if (sm == null) return false;
        if (sm.su == null) return false;
        if (sm.su.Recovery.UpgradeTimes <= 0) return false;
        return true;
    }

    public void Tick(float delta)
    {
        if (!isActive()) return;

        timer -= delta;
        if (timer > 0f) return;
        timer = 1.5f;

        TryHeal();
    }

    private void TryHeal()
    {
        float up = sm.su.Recovery.Growth * sm.su.Recovery.UpgradeTimes;
        float value = up * pm.attribute.HP_Max;

        pm.Heal(value);
    }
}