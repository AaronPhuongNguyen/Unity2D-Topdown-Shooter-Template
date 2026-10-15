using System;
using UnityEngine;

[Serializable]
public class UnitLevel
{
    public UnitAttribute attribute;
    public StatusBonusPerLevel SBPL = new StatusBonusPerLevel();

    public int Level_Current;

    public float EXP_Current;
    // Guarded: Log(0) is -Infinity, which made the old formula return -Infinity at level 1.
    [Header("EXP curve")]
    public float EXP_Base = 100f;       // cost of level 1 -> 2
    public float EXP_Growth = 20f;     // how much each extra level adds
    public float EXP_Exponent = 1.1f;  // >1 = gets steeper at high level

    public float EXP_ToUpLevel =>
        EXP_Base + EXP_Growth * Mathf.Pow(Mathf.Max(0, Level_Current - 1), EXP_Exponent);

    public Action<float> OnEXPAdded;
    public Action OnLevelUp;

    public UnitLevel()
    {
        Level_Current = 1;
        EXP_Current = 0;
    }

    /// <summary>Point this at the unit's CURRENT attribute (PlayerManager re-clones it on every respawn).</summary>
    public void Bind(UnitAttribute target) => attribute = target;

    /// <summary>Back to level 1 with no EXP. Does not undo stats already granted.</summary>
    public void Reset()
    {
        Level_Current = 1;
        EXP_Current = 0;
    }

    public void AddEXP(float exp)
    {
        if (exp <= 0f) return;

        EXP_Current += exp;
        OnEXPAdded?.Invoke(exp);

        // Cost is read BEFORE leveling, so each level is paid at its own price.
        while (true)
        {
            float needed = EXP_ToUpLevel;
            if (EXP_Current < needed) break;

            EXP_Current -= needed;
            UpLevel();
        }
    }

    private void UpLevel()
    {
        ++Level_Current;
        SBPL?.Apply(attribute);
        OnLevelUp?.Invoke();
    }
}

[Serializable]
public class StatusBonusPerLevel
{
    // Hard rule: any percent growth per level is clamped to this (0.01 = +1%).
    public const float MaxPctPerLevel = 0.01f;

    [Header("Flat growth per level")]
    public float Growth_HP = 5f;
    public float Growth_ATK = 0.5f;
    public float Growth_DEF = 5f;
    public float Growth_SPEED = 0.1f;
    public float Growth_SIGHT = 0.1f;
    public float Growth_CDR = 0.01f;

    [Header("Percent growth per level (PercBonus, max 0.01 = +1%)")]
    [Range(0f, MaxPctPerLevel)] public float Growth_HP_Pct = 0.01f;
    [Range(0f, MaxPctPerLevel)] public float Growth_ATK_Pct = 0.005f;

    [Header("Milestone (FLAT bonus every N levels, 0 = disabled)")]
    public int MilestoneEvery = 10;
    public float Milestone_HP_Flat = 25f;
    public float Milestone_ATK_Flat = 2f;

    [Header("Max (final stat value)")]
    public float Max_DEF = 1000f;
    public float Max_CDR = 0.5f;
    public float Max_SPEED = 8f;
    public float Max_SIGHT = 15f;

    public StatusBonusPerLevel() { }

    public StatusBonusPerLevel(float maxDef, float maxCdr, float maxSpeed, float maxSight)
    {
        Max_DEF = maxDef;
        Max_CDR = maxCdr;
        Max_SPEED = maxSpeed;
        Max_SIGHT = maxSight;
    }

    public void Apply(UnitAttribute a, int newLevel = 0)
    {
        if (a == null) return;
        if (a.IsNewUnit) a.Reset();

        bool milestone = MilestoneEvery > 0 && newLevel > 1 && newLevel % MilestoneEvery == 0;

        // HP: flat + percent (PercBonus). TotalBonus is never touched.
        float hpBefore = a.HP_Max;
        a.HP_Ampl.FlatBonus += Growth_HP + (milestone ? Milestone_HP_Flat : 0f);
        a.HP_Ampl.PercBonus += Mathf.Min(Growth_HP_Pct, MaxPctPerLevel);
        a.Heal(Mathf.Max(0f, a.HP_Max - hpBefore));

        // ATK: flat + percent (PercBonus).
        a.ATK_Ampl.FlatBonus += Growth_ATK + (milestone ? Milestone_ATK_Flat : 0f);
        a.ATK_Ampl.PercBonus += Mathf.Min(Growth_ATK_Pct, MaxPctPerLevel);

        // Capped stats: flat only.
        float before = a.DEF_Current;
        a.DEF_Ampl.FlatBonus += Growth_DEF;
        Cap(a.DEF_Ampl, Growth_DEF, before, a.DEF_Current, Max_DEF);

        before = a.SPEED_Current;
        a.SPEED_Ampl.FlatBonus += Growth_SPEED;
        Cap(a.SPEED_Ampl, Growth_SPEED, before, a.SPEED_Current, Max_SPEED);

        before = a.SIGHT_Current;
        a.SIGHT_Ampl.FlatBonus += Growth_SIGHT;
        Cap(a.SIGHT_Ampl, Growth_SIGHT, before, a.SIGHT_Current, Max_SIGHT);

        before = a.CooldownReduction_Current;
        a.CooldownReduction_Ampl.FlatBonus += Growth_CDR;
        Cap(a.CooldownReduction_Ampl, Growth_CDR, before, a.CooldownReduction_Current, Max_CDR);

        a.OnStatusChange?.Invoke();
    }

    private static void Cap(Amplification amp, float growth, float before, float after, float max)
    {
        if (after <= max || after <= before) return;

        float keep = Mathf.Clamp01((max - before) / (after - before));
        amp.FlatBonus -= growth * (1f - keep);
    }
}