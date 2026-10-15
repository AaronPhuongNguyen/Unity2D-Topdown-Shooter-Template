using UnityEngine;

[CreateAssetMenu(fileName = "Periodic Boost Card", menuName = "Endless/Card/Periodic Boost")]
public class CardEffect_PeriodicBoost : CardEffect_Runtime
{
    [Min(0.1f)] public float EverySeconds = 30f;
    [Tooltip("0.05 = +5% Max HP and ATK (Total Bonus) each time")] public float Percent = 0.05f;
    [Tooltip("0 = unlimited")] public int MaxStacks = 0;

    public override void DrawCard(UnitAttribute attribute, GameObject holder)
    {
        var b = Add<PeriodicBoostBehaviour>(holder);
        b.Period = Mathf.Max(0.1f, EverySeconds);
        b.Percent = Percent;
        b.MaxStacks = MaxStacks;
        b.Begin(attribute);
    }
}

public class PeriodicBoostBehaviour : CardBehaviour
{
    public float Period, Percent;
    public int MaxStacks;
    private float elapsed;
    private int stacks;

    public override void Tick(float dt)
    {
        if (MaxStacks > 0 && stacks >= MaxStacks) return;

        elapsed += dt;
        while (elapsed >= Period && (MaxStacks <= 0 || stacks < MaxStacks))
        {
            elapsed -= Period;
            stacks++;

            float before = A.HP_Max;
            A.HP_Ampl.TotalBonus += Percent;
            A.ATK_Ampl.TotalBonus += Percent;
            SyncHP(A, before);
        }
    }
}