using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class PlayerCombat : HurtBox, ITick
{
    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    private Weapon wp;
    private Detector d;
    #endregion
    public Weapon Weapon => wp;
    public override UnitPackage Access() => pm.clonedPack;

    // Not registered with TickSystem - driven explicitly from PlayerManager.Tick()
    // one frame, which already owns Controlling's lifecycle. Registering this too
    // would tick it twice (once via TickSystem, once via pm.pc?.Tick(delta)).
    public void Tick(float dt)
    {
        Check();
        wp?.Tick(dt);
        d?.Tick(dt);
    }

    public void Check()
    {
        if (pm.clonedPack == null) Debug.Log($"Cloned pack of {pm.name} is null");
        if (wp == null) wp = GetComponent<Weapon>() ? GetComponent<Weapon>() : gameObject.AddComponent<Weapon>();
        if (d == null) d = gameObject.AddComponent<Detector>();
    }
}