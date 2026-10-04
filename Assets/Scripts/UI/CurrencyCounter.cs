using TMPro;
using UnityEngine;

public class CurrencyCounter : MonoBehaviour, IUnscaledTick
{
    public TextMeshProUGUI currencyShower;
    private DomainManager dm => DomainManager.instance;

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((IUnscaledTick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((IUnscaledTick)this);
    }

    public void UnscaledTick(float delta)
    {
        if (!gameObject.activeSelf) return;
        if (currencyShower == null) return;
        if (dm == null) return;

        currencyShower.text = dm.Currency.ToString("F0");
    }
}