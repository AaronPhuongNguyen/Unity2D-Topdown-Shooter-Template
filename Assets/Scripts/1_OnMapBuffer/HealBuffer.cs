using UnityEngine;

/// <summary>
/// Map supply: heals Friendly units that stay in range, checked every
/// checkInterval (default 0.5s, set on BufferCore). Ignores Aggressive
/// units entirely (handled by BufferCore's base filtering).
/// </summary>
public class HealBuffer : BufferCore
{

    protected override void ApplyEffect(UnitPackage package, HurtBox hurtBox)
    {
        Heal(package, package.attribute.HP_Max * 0.25f);
    }
    public void Heal(UnitPackage package, float value)
    {
        if (package?.attribute == null) return;
        package.attribute.Heal(value);
    }
}