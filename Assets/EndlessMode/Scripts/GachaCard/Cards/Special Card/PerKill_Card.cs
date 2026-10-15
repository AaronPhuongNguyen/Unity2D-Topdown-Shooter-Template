using UnityEngine;

public enum PerKillStat { DealtDamage, MaxHP }

[CreateAssetMenu(fileName = "Per Kill Card", menuName = "Endless/Card/Per Kill")]
public class CardEffect_PerKill : CardEffect_Runtime
{
    public PerKillStat Stat = PerKillStat.DealtDamage;
    public float PercentPerKill = 0.0005f;

    public override void DrawCard(UnitAttribute attribute, GameObject holder)
    {
        var b = Add<PerKillBehaviour>(holder);
        b.Stat = Stat;
        b.PerKill = PercentPerKill;
        b.Begin(attribute);
    }
}

public class PerKillBehaviour : CardBehaviour
{
    public PerKillStat Stat;
    public float PerKill;
    private int lastKilled;

    protected override void OnBegin() => lastKilled = Killed();
    private static int Killed() => DomainManager.instance != null ? DomainManager.instance.Killed : 0;

    public override void Tick(float dt)
    {
        int now = Killed();
        int diff = now - lastKilled;
        lastKilled = now;
        if (diff <= 0) return;

        if (Stat == PerKillStat.MaxHP)
        {
            float before = A.HP_Max;
            A.HP_Ampl.TotalBonus += PerKill * diff;
            SyncHP(A, before);
        }
        else A.DealtDamage_Ampl.TotalBonus += PerKill * diff;
    }
}