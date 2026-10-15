using Server;
using UnityEngine;

/// <summary>Added to GachaCardManager's holder by a card. The manager calls Tick; Stop() undoes and removes it.</summary>
public abstract class CardBehaviour : MonoBehaviour
{
    protected UnitAttribute A;
    private bool _stopped;

    public bool Active => !_stopped && A != null;
    protected static PlayerManager PM => PlayerManager.instance;

    public void Begin(UnitAttribute a)
    {
        A = a;
        A.OnDealDamage += HandleDealDamage;
        OnBegin();
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        if (A != null) A.OnDealDamage -= HandleDealDamage;
        OnEnd();
        A = null;
        Destroy(this);
    }

    public virtual void Tick(float dt) { }
    protected virtual void OnBegin() { }
    protected virtual void OnEnd() { }      // undo anything that lives OUTSIDE the attribute
    protected virtual void OnDealDamage(float damage) { }

    private void HandleDealDamage(float damage)
    {
        if (!Active || A.IsDead) return;
        OnDealDamage(damage);
    }

    /// <summary>Call after changing Max HP: heal the gained amount, or clamp current HP if max dropped.</summary>
    protected static void SyncHP(UnitAttribute a, float maxBefore)
    {
        if (a.IsDead) return;
        float diff = a.HP_Max - maxBefore;
        if (diff > 0f) a.Heal(diff);
        else if (a.HP_Current > a.HP_Max) a.HP_Current = a.HP_Max;
    }

    /// <summary>Moves 'applied' to 'target' and returns the change to add to the stat.</summary>
    protected static float Delta(ref float applied, float target)
    {
        float d = target - applied;
        applied = target;
        return d;
    }
}