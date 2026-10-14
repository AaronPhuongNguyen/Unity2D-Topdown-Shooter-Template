using UnityEngine;

[CreateAssetMenu(fileName = "Dealt Damage Effect Card", menuName = "Endless/Card/Dealt Damage")]
public class CardEffect_DealtDamage : CardEffect_Amp
{
    protected override Amplification GetAmplification(UnitAttribute a) => a.DealtDamage_Ampl;
}