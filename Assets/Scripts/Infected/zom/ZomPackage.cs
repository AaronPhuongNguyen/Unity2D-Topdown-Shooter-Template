
using UnityEngine;

[CreateAssetMenu(menuName ="Package/Zom")]
public class ZomPackage:UnitPackage
{
    public int CurrencyAtKill = 12;
    public int AppearFromWave = 0;
    public float CountPerSpawner = 2f;
    public float _rateToAppear = 0.5f;
    public float _biteAccuracy = 0.5f;
    public float BiteAccuracy => Mathf.Clamp01(_biteAccuracy);
}