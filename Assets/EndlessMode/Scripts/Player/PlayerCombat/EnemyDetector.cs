using Unity.Mathematics;
using UnityEngine;

public class Detector : MonoBehaviour, ITick
{
    #region Cache
    private PlayerManager pm => PlayerManager.instance;
    private Collider2D[] targets = new Collider2D[16];
    private Transform currentTarget;
    private int foundTarget;

    [SerializeField] private float searchInterval = 0.3f;
    private float searchTimer;
    #endregion

    #region Tick
    public void Tick(float delta)
    {
        if (pm == null) return;

        // Detector's job is purely auto-targeting - it should stay idle
        // whenever AutoAttack is off (pure manual mode) OR the player is
        // actively overriding with the manual stick (hybrid mode), so it
        // never fights pm.Target against the joystick's intent, and never
        // burns a search while its result would be ignored anyway.
        if (!pm.AutoAttack || pm.ManualAttacking)
        {
            if (currentTarget != null)
            {
                currentTarget = null;
                pm.Target = null;
            }
            return;
        }

        PerformSearch(delta);
    }
    private void OnDisable()
    {
        RemoveSightPenalty();
    }
    #endregion

    #region Functions
    [Header("Weather Sight Penalty")]
    [Tooltip("Added to the player's SIGHT TotalBonus while it rains, is night, or lightning flashes. -0.5 = 50% less sight.")]
    [SerializeField] private float weatherSightPenalty = -0.5f;

    private WeatherManager wm => WeatherManager.instance;   // property, so it is never stale/null
    private float appliedSightPenalty;                      // exactly what we added to the attribute
    private UnitAttribute penaltyOwner;                     // the attribute we added it to

    private void PerformSearch(float delta)
    {
        if (searchInterval > 0f)
        {
            searchTimer -= delta;
            if (searchTimer > 0f) return;
        }

        SearchTarget();
        pm.Target = FindNearestTarget();
        searchTimer = searchInterval;

        UpdateSightPenalty();
    }

    private void UpdateSightPenalty()
    {
        UnitAttribute attr = pm != null ? pm.attribute : null;

        // The player was recreated (new game): the old clone is gone, nothing to subtract from the new one.
        if (attr != penaltyOwner)
        {
            penaltyOwner = attr;
            appliedSightPenalty = 0f;
        }
        if (attr == null) return;

        bool bad = wm != null && (wm.IsRaining || wm.IsNight || wm.IsLightningFlashing);
        float desired = bad ? weatherSightPenalty : 0f;

        if (Mathf.Approximately(desired, appliedSightPenalty)) return;

        attr.SIGHT_Ampl.TotalBonus += desired - appliedSightPenalty;
        appliedSightPenalty = desired;
    }

    private void RemoveSightPenalty()
    {
        if (penaltyOwner != null && appliedSightPenalty != 0f)
            penaltyOwner.SIGHT_Ampl.TotalBonus -= appliedSightPenalty;

        appliedSightPenalty = 0f;
        penaltyOwner = null;
    }

    private void SearchTarget()
    {
        foundTarget = Physics2D.OverlapCircleNonAlloc
        (
            pm.Controlling.transform.position,
            pm.attribute.SIGHT_Current,
            targets,
            pm.EnemyMask
        );

        if (foundTarget >= targets.Length)
        {
            targets = new Collider2D[targets.Length * 2];
            SearchTarget();
        }
    }

    private Transform FindNearestTarget()
    {
        if (foundTarget <= 0)
        {
            currentTarget = null;
            return null;
        }

        float2 originPos = new float2(pm.Controlling.transform.position.x, pm.Controlling.transform.position.y);

        float nearestSqr = float.MaxValue;
        int nearestIdx = -1;

        for (int i = 0; i < foundTarget; i++)
        {
            Collider2D col = targets[i];
            if (col == null) continue;
            if (!col.gameObject.TryGetComponent<HurtBox>(out var t))
                continue;
            if(t==null) continue;
            if (t.Access() == null || t.Access().attribute == null) continue;
            if (t.Access().attribute.IsUntargetable) continue;

            Vector2 p = col.transform.position;
            float2 diff = new float2(p.x, p.y) - originPos;
            float distSqr = math.dot(diff, diff);

            if (distSqr < nearestSqr)
            {
                nearestSqr = distSqr;
                nearestIdx = i;
            }
        }

        if (nearestIdx < 0)
        {
            currentTarget = null;
            return null;
        }

        currentTarget = targets[nearestIdx].transform;
        return currentTarget;
    }
    #endregion
}