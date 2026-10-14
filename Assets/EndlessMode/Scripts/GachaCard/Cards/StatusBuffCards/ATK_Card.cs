using UnityEngine;

[CreateAssetMenu(fileName = "ATK Effect Card", menuName = "Endless/Card/ATK")]
public class CardEffect_ATK : CardEffect_Amp
{
    protected override Amplification GetAmplification(UnitAttribute a) => a.ATK_Ampl;
}