using UnityEngine;

[CreateAssetMenu(fileName = "ASPD Effect Card", menuName = "Endless/Card/ASPD")]
public class CardEffect_ASPD : CardEffect_Amp
{
    protected override Amplification GetAmplification(UnitAttribute a) => a.ASPD_Ampl;
}