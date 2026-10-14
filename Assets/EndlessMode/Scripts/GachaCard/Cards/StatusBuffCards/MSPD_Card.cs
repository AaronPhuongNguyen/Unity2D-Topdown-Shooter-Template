using UnityEngine;

[CreateAssetMenu(fileName = "MSPD Effect Card", menuName = "Endless/Card/MSPD")]
public class CardEffect_MSPD : CardEffect_Amp
{
    protected override Amplification GetAmplification(UnitAttribute a) => a.SPEED_Ampl;
}