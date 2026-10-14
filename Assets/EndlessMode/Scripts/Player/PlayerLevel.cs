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
    public float EXP_ToUpLevel => 100 * (1 + Mathf.Log(Mathf.Max(1, Level_Current - 1)) * 0.75f);

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
    [Header("Growth per level (added as flat bonus)")]
    public float Growth_HP=5f;
    public float Growth_ATK=0.5f;
    public float Growth_DEF=5;
    public float Growth_SPEED=0.1f;
    public float Growth_SIGHT=0.1f;
    public float Growth_CDR=0.01f;

    // Max values - only DEF, CDR, SPEED and SIGHT are limited (HP and ATK are not).
    [Header("Max (final stat value)")]
    public float Max_DEF = 1000f;
    public float Max_CDR = 0.5f;   // 50%
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

    /// <summary>Applies one level's worth of growth to the unit, respecting the max values.</summary>
    public void Apply(UnitAttribute a)
    {
        if (a == null) return;
        if (a.IsNewUnit) a.Reset(); // make sure amplifiers exist

        // HP: no cap. Heal by the amount gained so leveling is felt immediately.
        float hpBefore = a.HP_Max;
        a.HP_Ampl.FlatBonus += Growth_HP;
        a.Heal(Mathf.Max(0f, a.HP_Max - hpBefore));

        // ATK: no cap.
        a.ATK_Ampl.FlatBonus += Growth_ATK;

        // Capped stats.
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

    /// <summary>
    /// The growth was already added. If the final value went past <paramref name="max"/>,
    /// take back just the part that overshoots (all of it if already over the max).
    /// Works whatever multipliers sit on top of the flat bonus, and never lowers a value
    /// that is over the max because of something else (e.g. a card).
    /// </summary>
    private static void Cap(Amplification amp, float growth, float before, float after, float max)
    {
        if (after <= max || after <= before) return;

        float keep = Mathf.Clamp01((max - before) / (after - before));
        amp.FlatBonus -= growth * (1f - keep);
    }
}