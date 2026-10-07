using UnityEngine;

[CreateAssetMenu(menuName ="Template/Attribute")]
public class UnitAttributeTemplate:ScriptableObject
{
    [Header("Base Value")]
    public float HP_Base = 200;
    public float ATK_Base = 20;
    public float SPEED_Base = 10;
    public float DEF_Base = 100;
    public float ASPD_Base = 0.1f;
    public float SIGHT_Base = 25f;
}