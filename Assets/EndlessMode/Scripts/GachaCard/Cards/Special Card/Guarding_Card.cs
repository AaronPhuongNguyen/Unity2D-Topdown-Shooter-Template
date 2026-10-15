using UnityEngine;

[CreateAssetMenu(fileName = "Guard Card", menuName = "Endless/Card/Guard")]
public class CardEffect_Guard : CardEffect
{
    [Tooltip("0.15 = +15% Damage Reduction")] public float DamageReduction = 0.15f;
    [Tooltip("0.05 = +5% Move Speed (Total Bonus)")] public float MoveSpeedPercent = 0.05f;

    public override void DrawCard(UnitAttribute attribute)
    {
        if (attribute == null) return;
        attribute.DamageReduction_Ampl.FlatBonus += DamageReduction;
        attribute.SPEED_Ampl.TotalBonus += MoveSpeedPercent;
        attribute.OnStatusChange?.Invoke();
    }
}