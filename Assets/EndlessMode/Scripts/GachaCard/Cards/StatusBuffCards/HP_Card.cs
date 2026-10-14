using UnityEngine;

[CreateAssetMenu(fileName = "HP Effect Card", menuName = "Endless/Card/HP")]
public class CardEffect_HP : CardEffect_Amp
{
    protected override Amplification GetAmplification(UnitAttribute a) => a.HP_Ampl;

    // Raising max HP also heals by the amount gained, so the card is felt immediately.
    public override void DrawCard(UnitAttribute attribute)
    {
        if (attribute == null) return;
        float before = attribute.HP_Max;
        base.DrawCard(attribute);
        attribute.Heal(Mathf.Max(0f, attribute.HP_Max - before));
    }
}