using TMPro;
using UnityEngine;

public class CurrencyCounter : MonoBehaviour
{
    public TextMeshProUGUI currencyShower;
    private DomainManager dm => DomainManager.instance;

    private float currency=1;

    private void Update()
    {
        Currency();
    }
    private void Currency()
    {
        if (!gameObject.activeSelf) return;
        if (currencyShower == null) return;
        if (dm.Currency == currency) return;
        currency = Mathf.Lerp(currency,dm.Currency,2*Time.deltaTime);

        currencyShower.text = currency.ToString("F0");
    }
}