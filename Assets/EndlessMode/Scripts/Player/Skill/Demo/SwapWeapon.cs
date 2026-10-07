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

    /// <summary>
    /// Cycles through Rifle -> Shotgun -> Sniper -> Rifle.
    /// </summary>
    public void Toggle()
    {
        WeaponType next = CurrentType switch
        {
            WeaponType.Rifle => WeaponType.Shotgun,
            WeaponType.Shotgun => WeaponType.Sniper,
            WeaponType.Sniper => WeaponType.Rifle,
            _ => WeaponType.Rifle
        };
        Equip(next);
        CamManager.instance.ZoomIn(99);
    }

    public void EquipRifle() => Equip(WeaponType.Rifle);
    public void EquipShotgun() => Equip(WeaponType.Shotgun);
    public void EquipSniper() => Equip(WeaponType.Sniper);
}