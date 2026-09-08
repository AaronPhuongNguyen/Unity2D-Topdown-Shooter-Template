using System;
using UnityEngine;

[Serializable]
public class Amplification
{
    public float FlatBonus = 0f;
    public float PercBonus = 1.0f;
    public float TotalBonus = 1.0f;

    public Amplification() { }

    public Amplification Clone()
    {
        return new Amplification
        {
            FlatBonus = FlatBonus,
            PercBonus = PercBonus,
            TotalBonus = TotalBonus
        };
    }
}

[Serializable]
public class UnitAttribute
{
    public UnitAttributeTemplate template;

    #region Current Status
    public float HP_Current;
    public float HP_Lost => (HP_Max - HP_Current) / HP_Max;
    public bool IsFullHP => (HP_Current >= HP_Max);

    public float HP_Max => Server.Calculation.FinalValue(template.HP_Base, HP_Ampl);
    public float ATK_Current => Server.Calculation.FinalValue(template.ATK_Base, ATK_Ampl);
    public float SPEED_Current => Server.Calculation.FinalValue(template.SPEED_Base, SPEED_Ampl);
    public float DEF_Current => Server.Calculation.FinalValue(template.DEF_Base, DEF_Ampl);
    public float ASPD_Current => Server.Calculation.FinalValue(template.ASPD_Base, ASPD_Ampl);
    public float SIGHT_Current => Server.Calculation.FinalValue(template.SIGHT_Base, SIGHT_Ampl);
    #endregion

    #region Misc Status


    public float ArmourPenetration_Perc = 0f;
    public float DamageReduction_Extra = 0f;
    public float CooldownReduction_Extra = 0f;
    public float DealtDamage_Extra = 0f;
    #endregion

    #region Amplifier
    public Amplification HP_Ampl;
    public Amplification ATK_Ampl;
    public Amplification SPEED_Ampl;
    public Amplification DEF_Ampl;
    public Amplification ASPD_Ampl;
    public Amplification SIGHT_Ampl;
    #endregion

    #region Flags
    public bool IsNewUnit =>
        HP_Ampl == null || ATK_Ampl == null || SPEED_Ampl == null ||
        DEF_Ampl == null || ASPD_Ampl == null || SIGHT_Ampl == null;

    public bool IsDead => HP_Current <= 0;
    #endregion

    #region Events
    public Action OnDeath, OnSpawn, OnStatusChange;
    public Action<float> OnTakeDamage, OnDealDamage;
    #endregion

    #region Lifecycle
    public void Reset()
    {
        if (IsNewUnit) InitializeAmplifiers();
        Respawn();
    }

    private void InitializeAmplifiers()
    {
        HP_Ampl ??= new Amplification();
        ATK_Ampl ??= new Amplification();
        SPEED_Ampl ??= new Amplification();
        DEF_Ampl ??= new Amplification();
        ASPD_Ampl ??= new Amplification();
        SIGHT_Ampl ??= new Amplification();
    }
    public void Respawn()
    {
        HP_Current = HP_Max;
        OnSpawn?.Invoke();
    }
    #endregion

    #region Combat
    public void TakeDamage(float damage)
    {
        HP_Current -= damage * (1 - DamageReduction_Extra);
        DeathHandler();
    }

    private void DeathHandler()
    {
        if (!IsDead) return;
        OnDeath?.Invoke();
    }
    #endregion

    #region Cloning
    public UnitAttribute Clone()
    {
        return new UnitAttribute
        {
            template = template,
            HP_Current = HP_Current,
            HP_Ampl = HP_Ampl?.Clone(),
            ATK_Ampl = ATK_Ampl?.Clone(),
            SPEED_Ampl = SPEED_Ampl?.Clone(),
            DEF_Ampl = DEF_Ampl?.Clone(),
            ASPD_Ampl = ASPD_Ampl?.Clone(),
            SIGHT_Ampl = SIGHT_Ampl?.Clone(),
        };
    }
    #endregion
}