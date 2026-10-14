using UnityEngine;

/// <summary>
/// Base for cards that boost one Amplification on a UnitAttribute.
/// Not creatable on its own - use the concrete cards (ATK, HP, DEF, ...).
/// Both bonuses are applied additively, so cards stack:
///   FlatBonus += FlatIncrease      (e.g. +20 ATK)
///   PercBonus += PercentIncrease   (e.g. 0.1 = +10%)
/// </summary>
public abstract class CardEffect_Amp : CardEffect
{
    [Header("Bonus")]
    public float FlatIncrease = 0f;
    [Tooltip("0.1 = +10%")]
    public float PercentIncrease = 0f;
    [Tooltip("Total Increase includes both Flat & Percent")]
    public float TotalIncrease = 0f;

    protected abstract Amplification GetAmplification(UnitAttribute attribute);

    public override void DrawCard(UnitAttribute attribute)
    {
        if (attribute == null) return;

        Amplification amp = GetAmplification(attribute);
        if (amp == null)
        {
            Debug.LogWarning($"{name}: attribute has no amplifier yet (call UnitAttribute.Reset() first).");
            return;
        }

        amp.FlatBonus += FlatIncrease;
        amp.PercBonus += PercentIncrease;
        amp.TotalBonus += TotalIncrease;
        attribute.OnStatusChange?.Invoke();
    }
}