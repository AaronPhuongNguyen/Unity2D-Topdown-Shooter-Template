using UnityEngine;

[CreateAssetMenu(fileName = "SIGHT Effect Card", menuName = "Endless/Card/SIGHT")]
public class CardEffect_SIGHT : CardEffect_Amp
{
    protected override Amplification GetAmplification(UnitAttribute a) => a.SIGHT_Ampl;
}