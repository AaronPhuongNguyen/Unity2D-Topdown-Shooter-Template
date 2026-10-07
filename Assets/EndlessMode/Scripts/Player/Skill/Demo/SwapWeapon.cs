using UnityEngine;

public class SwapWeapon : MonoBehaviour
{
    [SerializeField] private WeaponType DefaultWeapon = WeaponType.Sniper;

    private PlayerManager pm => PlayerManager.instance;
    private Weapon wp => pm != null ? pm.pc?.Weapon : null;

    public WeaponType CurrentType { get; private set; }

    private bool hasEquipped;

    private void Update()
    {
        // Weapon is created lazily (first PlayerCombat.Tick), so it may not
        // exist yet on this object's own Start(). Keep retrying each frame
        // until the very first successful Equip, then stop - this is what
        // was missing: previously a single failed Start()-time attempt
        // still advanced CurrentType, permanently desyncing it from the
        // weapon's real (Sniper-default) state.
        if (hasEquipped) return;
        if (wp == null) return;

        CurrentType = DefaultWeapon;
        Equip(DefaultWeapon);
        hasEquipped = true;
    }

    public void Equip(WeaponType type)
    {
        if (wp == null)
        {
            Debug.LogWarning("SwapWeapon: Weapon not found.");
            return;
        }

        wp.SetType(type);
        CurrentType = type; // only commit the tracked state once the equip actually succeeded
    }
    private int _i;

    public void Toggle()
    {
        _i = (_i + 1) % 3;

        WeaponType next = _i switch
        {
            0 => WeaponType.Rifle,
            1 => WeaponType.Shotgun,
            2 => WeaponType.Sniper,
            _ => WeaponType.Rifle // fallback, won't hit if %3 is correct
        };

        Equip(next);
        CamManager.instance.ZoomIn(99);
    }

    public void EquipRifle() => Equip(WeaponType.Rifle);
    public void EquipShotgun() => Equip(WeaponType.Shotgun);
    public void EquipSniper() => Equip(WeaponType.Sniper);
}