using UnityEngine;

// Its base value is 0, so the Flat bonus is the one that matters here.
[CreateAssetMenu(fileName = "Armour Penetration Effect Card", menuName = "Endless/Card/Armour Penetration")]
public class CardEffect_ArmourPen : CardEffect_Amp
{
    protected override Amplification GetAmplification(UnitAttribute a) => a.ArmourPenetration_Ampl;
}