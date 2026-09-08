using UnityEngine;

public class SwapWeapon : MonoBehaviour
{
    [SerializeField] private WeaponType DefaultWeapon = WeaponType.Rifle;

    private PlayerManager pm => PlayerManager.instance;
    private Weapon wp => pm != null ? pm.pc?.Weapon : null;

    public WeaponType CurrentType { get; private set; }

    private void Start()
    {
        CurrentType = DefaultWeapon;
        Equip(DefaultWeapon);
    }

    public void Equip(WeaponType type)
    {
        if (wp == null)
        {
            Debug.LogWarning("SwapWeapon: Not found Weapon");
            return;
        }

        wp.SetType(type);
        CurrentType = type;
    }

    public void Toggle()
    {
        Equip(CurrentType == WeaponType.Rifle ? WeaponType.Shotgun : WeaponType.Rifle);
    }

    public void EquipRifle() => Equip(WeaponType.Rifle);
    public void EquipShotgun() => Equip(WeaponType.Shotgun);
}