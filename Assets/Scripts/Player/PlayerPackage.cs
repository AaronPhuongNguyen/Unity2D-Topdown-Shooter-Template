using UnityEngine;

[CreateAssetMenu(menuName ="Package/Player")]
public class PlayerPackage:UnitPackage
{
    public float ShootAccuracy = 0.9f;
    public float MaxMagazine = 30f;
    public float Reloadtime=3f;
}