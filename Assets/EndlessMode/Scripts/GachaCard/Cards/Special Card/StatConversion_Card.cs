using Server;
using UnityEngine;

public enum ConversionSource { ATK, MoveSpeed }

[CreateAssetMenu(fileName = "Stat Conversion Card", menuName = "Endless/Card/Stat Conversion")]
public class CardEffect_StatConversion : CardEffect_Runtime
{
    public ConversionSource Source = ConversionSource.ATK;
    [Header("Gained per 1 point of the source stat (flat)")]
    public float ToATK = 0f;
    public float ToDEF = 0.5f;
    public float ToHP = 0f;

    public override void DrawCard(UnitAttribute attribute, GameObject holder)
    {
        var b = Add<ConversionBehaviour>(holder);
        b.FromSpeed = Source == ConversionSource.MoveSpeed;
        b.ToATK = b.FromSpeed ? ToATK : 0f; // ATK -> ATK would feed on itself
        b.ToDEF = ToDEF;
        b.ToHP = ToHP;
        b.Begin(attribute);
    }
}

public class ConversionBehaviour : CardBehaviour
{
    public bool FromSpeed;
    public float ToATK, ToDEF, ToHP;

    private static readonly Amplification scratch = new Amplification();
    private float apATK, apDEF, apHP, timer;

    // Speed WITHOUT TotalBonus, so temporary slows (attacking, stagger) don't make ATK/HP flicker.
    private float StableSpeed()
    {
        Amplification s = A.SPEED_Ampl;
        scratch.FlatBonus = s.FlatBonus;
        scratch.PercBonus = s.PercBonus;
        scratch.TotalBonus = 1f;
        return Calculation.FinalValue(A.template.SPEED_Base, scratch);
    }

    public override void Tick(float dt)
    {
        timer += dt;
        if (timer < 0.25f) return;
        timer = 0f;
        if (A.IsDead) return;

        float source = FromSpeed ? StableSpeed() : A.ATK_Current;

        A.ATK_Ampl.FlatBonus += Delta(ref apATK, source * ToATK);
        A.DEF_Ampl.FlatBonus += Delta(ref apDEF, source * ToDEF);

        float before = A.HP_Max;
        A.HP_Ampl.FlatBonus += Delta(ref apHP, source * ToHP);
        SyncHP(A, before);
    }
}