using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class PlayerCombat : HurtBox,ITick
{
    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    private Weapon wp;
    private Detector d;
    private Rigidbody2D rb;
    #endregion

    public override UnitPackage Access() => pm.clonedPack;

    #region Lifecycle
    public void Tick(float dt)
    {
        Check();
        wp?.Tick(dt);
        d?.Tick(dt);
    }
    #endregion

    #region Feature
    public void Check()
    {
        if (pm.clonedPack == null) Debug.Log($"Cloned pack of {pm.name} is null");
        if(wp == null) wp = GetComponent<Weapon>() ? GetComponent<Weapon>() : gameObject.AddComponent<Weapon>();
        if(d==null) d = gameObject.AddComponent<Detector>();
    }
    
    #endregion
    
}