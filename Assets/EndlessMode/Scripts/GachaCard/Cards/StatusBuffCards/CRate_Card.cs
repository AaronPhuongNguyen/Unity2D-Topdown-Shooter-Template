using UnityEngine;

// CritRate is a plain value on UnitAttribute (not an Amplification), so it is added directly.
[CreateAssetMenu(fileName = "Crit Rate Effect Card", menuName = "Endless/Card/Crit Rate")]
public class CardEffect_CritRate : CardEffect
{
    [Tooltip("0.05 = +5% crit chance")]
    public float CritRateIncrease = 0.05f;

    [Tooltip("Crit rate is capped at this value.")]
    [Range(0f, 1f)] public float MaxCritRate = 1f;

    public override void DrawCard(UnitAttribute attribute)
    {
        if (attribute == null) return;
        attribute.CritRate = Mathf.Min(attribute.CritRate + CritRateIncrease, MaxCritRate);
        attribute.OnStatusChange?.Invoke();
    }
}