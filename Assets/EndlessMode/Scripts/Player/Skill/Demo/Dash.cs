using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dash skill for a UI Button.
/// Dashes the player in the move direction (or the facing direction if not
/// moving) for DashDuration seconds, invincible the whole time.
/// Cooldown: 1s. Cost: 5 Energy (PlayerManager.Energy).
///
/// Setup: add this to any active GameObject, then either
///   - drag the Button into the "button" field (auto-wires onClick), OR
///   - add Dash() to the Button's OnClick in the Inspector (leave "button" empty).
/// Don't do both or the dash fires twice per press.
/// Optional: assign an Image (Filled type) to "UI" to show the cooldown.
/// </summary>
public class DashSkill : MonoBehaviour, ITick
{
    PlayerManager pm => PlayerManager.instance;
    DomainManager dm => DomainManager.instance;

    public const float DashDuration = 0.3f;
    public const float SkillCooldown = 1f;
    public const float EnergyCost = 5f;

    [Tooltip("Total distance covered over the dash duration (world units).")]
    [SerializeField] private float dashDistance = 5f;

    [Tooltip("Optional: auto-wires onClick to Dash().")]
    [SerializeField] private Button button;

    [Tooltip("Optional: Filled image showing the cooldown.")]
    public Image UI;

    private float CD;
    private float dashTimeLeft;
    private Vector2 dashDir;
    private bool isDashing;
    private bool invincibleApplied;

    private float TotalCooldown
    {
        get
        {
            float cdr = pm != null && pm.attribute != null ? pm.attribute.CooldownReduction_Current : 0f;
            return SkillCooldown * (1f - cdr);
        }
    }

    #region Input
    public void Dash()
    {
        if (pm == null || pm.Controlling == null || pm.attribute == null) return;
        if (isDashing || CD > 0f || pm.attribute.IsDead) return;

        // Move direction wins; otherwise dash the way the player is facing.
        Vector2 dir = pm.MoveInput != Vector2.zero ? pm.MoveInput : pm.Direction;
        if (dir.sqrMagnitude < 0.0001f) return;

        // Spend last, so a dash that can't start never costs energy.
        if (!pm.Energy.TrySpend(EnergyCost)) return;

        dashDir = dir.normalized;
        dashTimeLeft = DashDuration;
        CD = TotalCooldown;
        isDashing = true;

        // PlayerController skips its own movement while this is set.
        pm.IsDashing = true;

        pm.attribute.IsInvincible = true;
        invincibleApplied = true;

        if (UI != null) UI.fillAmount = 1f;
    }
    #endregion

    #region Lifecycle
    public void Tick(float delta)
    {
        if (pm == null) return;

        button.interactable = pm.Energy.CanAfford(EnergyCost);

        if (CD > 0f) CD -= delta;
        if (UI != null)
        {
            float total = TotalCooldown;
            UI.fillAmount = total > 0f ? Mathf.Clamp01(CD / total) : 0f;
        }

        if (!isDashing) return;

        if (pm.Controlling == null || pm.attribute == null)
        {
            EndDash();
            return;
        }

        float step = Mathf.Min(delta, dashTimeLeft);
        Transform t = pm.Controlling.transform;

        Vector2 p = (Vector2)t.position + (dashDir * pm.attribute.SPEED_Current / 9) * (dashDistance / DashDuration) * step;
        if (dm != null) p = dm.ClampToMap(p);
        t.position = new Vector3(p.x, p.y, t.position.z);

        dashTimeLeft -= delta;
        if (dashTimeLeft <= 0f) EndDash();
    }

    private void EndDash()
    {
        isDashing = false;
        dashTimeLeft = 0f;

        var m = pm;
        if (m == null) { invincibleApplied = false; return; }

        m.IsDashing = false;
        if (invincibleApplied && m.attribute != null)
            m.attribute.IsInvincible = false;
        invincibleApplied = false;
    }

    private void Awake()
    {
        EventBus.OnGameRestart += Refresh;
    }

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
        if (button != null)
            button.onClick.AddListener(Dash);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
        if (button != null)
            button.onClick.RemoveListener(Dash);

        // Never leave the player invincible / frozen if this is disabled mid-dash.
        if (isDashing) EndDash();
    }

    private void OnDestroy()
    {
        EventBus.OnGameRestart -= Refresh;
    }

    private void Refresh()
    {
        CD = 0f;
        EndDash();
    }
    #endregion
}