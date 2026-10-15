using UnityEngine;

public abstract class CardEffect_Runtime : CardEffect
{
    public sealed override void DrawCard(UnitAttribute attribute)
    {
        Debug.LogWarning($"{name}: ongoing card needs GachaCardManager's holder.");
    }

    public abstract override void DrawCard(UnitAttribute attribute, GameObject holder);

    protected static T Add<T>(GameObject holder) where T : CardBehaviour => holder.AddComponent<T>();
}