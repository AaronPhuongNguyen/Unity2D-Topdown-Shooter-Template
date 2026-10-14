using UnityEngine;

// CritDamage is a plain multiplier on UnitAttribute (default 2.0 = x2), so it is added directly.
[CreateAssetMenu(fileName = "Crit Damage Effect Card", menuName = "Endless/Card/Crit Damage")]
public class CardEffect_CritDamage : CardEffect
{
    [Tooltip("0.1 = +0.1 to the crit multiplier (x2.0 becomes x2.1)")]
    public float CritDamageIncrease = 0.1f;

    public override void DrawCard(UnitAttribute attribute)
    {
        if (attribute == null) return;
        attribute.CritDamage += CritDamageIncrease;
        attribute.OnStatusChange?.Invoke();
    }
}