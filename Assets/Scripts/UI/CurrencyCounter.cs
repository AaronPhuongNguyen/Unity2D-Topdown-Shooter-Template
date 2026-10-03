using TMPro;
using UnityEngine;

public class CurrencyCounter : MonoBehaviour, ITick
{
    public TextMeshProUGUI currencyShower;
    private DomainManager dm => DomainManager.instance;

    private float currency = 1;

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

    public void Tick(float delta)
    {
        if (!gameObject.activeSelf) return;
        if (currencyShower == null) return;
        if (dm == null) return;
        if (dm.Currency == currency) return;

        currency = Mathf.Lerp(currency, dm.Currency, 2 * delta);

        currencyShower.text = currency.ToString("F0");
    }
}