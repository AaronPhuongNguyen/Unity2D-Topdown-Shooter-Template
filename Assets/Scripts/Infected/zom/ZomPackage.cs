
using UnityEngine;

[CreateAssetMenu(menuName ="Package/Zom")]
public class ZomPackage:UnitPackage
{
    public int AppearFromWave = 0;
    public float _rateToAppear = 0.75f;
    public float _biteAccuracy = 0.75f;
    public float BiteAccuracy => Mathf.Clamp01(_biteAccuracy);
}