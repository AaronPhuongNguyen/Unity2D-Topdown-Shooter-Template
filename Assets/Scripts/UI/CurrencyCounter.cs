using TMPro;
using UnityEngine;

public class CurrencyCounter : MonoBehaviour
{
    public TextMeshProUGUI currencyShower;
    private DomainManager dm => DomainManager.instance;

    private float currency;

    private void Update()
    {
        Currency();
    }
    private void Currency()
    {
        if (currencyShower == null) return;
        if (dm.Currency == currency) return;
        currency = Mathf.Lerp(currency,dm.Currency,10*Time.deltaTime);

        currencyShower.text = currency.ToString("F0");
    }
}