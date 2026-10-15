using UnityEngine;

public abstract class CardEffect : ScriptableObject
{
    public Sprite Icon;
    public string Name;
    [TextArea] public string Description;
    public Category Category;

    [Tooltip("Chance (0-1) that this card is accepted when it comes up in a draw. " +
             "1 = always accepted (common). 0.1 = accepted 10% of the time (rare). " +
             "Rejected cards are rerolled.")]
    [Range(0f, 1f)] public float Rarity = 1f;

    [Tooltip("If true, once the player picks this card it is never offered again for the rest of the run. " +
             "If false (default), it can come up again and stack.")]
    public bool UniquePerRun = false;

    public abstract void DrawCard(UnitAttribute attribute);

    // Cards that need ongoing logic override this one. Others don't need to change.
    public virtual void DrawCard(UnitAttribute attribute, GameObject holder) => DrawCard(attribute);
}

public enum Category
{
    Basic,
    Normal,
    Strong
}