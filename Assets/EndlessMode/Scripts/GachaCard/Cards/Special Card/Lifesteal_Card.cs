using UnityEngine;

[CreateAssetMenu(fileName = "Lifesteal Card", menuName = "Endless/Card/Lifesteal")]
public class CardEffect_Lifesteal : CardEffect_Runtime
{
    [Tooltip("0.1 = recover 10% of damage dealt")] public float Lifesteal = 0.1f;
    [Tooltip("0.001 = recover 0.1% Max HP on every damage dealt")] public float HealMaxHPPerHit = 0.001f;

    public override void DrawCard(UnitAttribute attribute, GameObject holder)
    {
        var b = Add<LifestealBehaviour>(holder);
        b.Lifesteal = Lifesteal;
        b.MaxHPPerHit = HealMaxHPPerHit;
        b.Begin(attribute);
    }
}

public class LifestealBehaviour : CardBehaviour
{
    public float Lifesteal, MaxHPPerHit;

    protected override void OnDealDamage(float damage)
    {
        A.Heal(damage * Lifesteal + A.HP_Max * MaxHPPerHit);
    }
}