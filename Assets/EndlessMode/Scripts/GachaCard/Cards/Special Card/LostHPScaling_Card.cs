using UnityEngine;

[CreateAssetMenu(fileName = "Lost HP Scaling Card", menuName = "Endless/Card/Lost HP Scaling")]
public class CardEffect_LostHPScaling : CardEffect_Runtime
{
    [Header("Per 1% of lost HP")]
    [Tooltip("0.01 = +1% Dealt Damage")] public float DealtDamagePerPercent = 0.01f;
    [Tooltip("0.01 = +1% Move Speed")] public float MoveSpeedPerPercent = 0.01f;
    [Tooltip("0.001 = +0.1% Lifesteal")] public float LifestealPerPercent = 0.001f;

    public override void DrawCard(UnitAttribute attribute, GameObject holder)
    {
        var b = Add<LostHPBehaviour>(holder);
        b.DmgPerPct = DealtDamagePerPercent;
        b.SpeedPerPct = MoveSpeedPerPercent;
        b.LifestealPerPct = LifestealPerPercent;
        b.Begin(attribute);
    }
}

public class LostHPBehaviour : CardBehaviour
{
    public float DmgPerPct, SpeedPerPct, LifestealPerPct;
    private float apDmg, apSpeed, lifesteal, timer;

    public override void Tick(float dt)
    {
        timer += dt;
        if (timer < 0.2f) return; // interval, not every frame
        timer = 0f;
        if (A.IsDead) return;

        float pct = Mathf.Floor(Mathf.Clamp01(A.HP_Lost) * 100f); // whole 1% steps
        A.DealtDamage_Ampl.TotalBonus += Delta(ref apDmg, pct * DmgPerPct);
        A.SPEED_Ampl.TotalBonus += Delta(ref apSpeed, pct * SpeedPerPct);
        lifesteal = pct * LifestealPerPct;
    }

    protected override void OnDealDamage(float damage)
    {
        if (lifesteal > 0f) A.Heal(damage * lifesteal);
    }
}