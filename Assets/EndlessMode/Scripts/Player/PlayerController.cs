using Server;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
public class PlayerController : MonoBehaviour, ITick
{
    [Header("Debugger")]
    [SerializeField] private bool isDebugMode = false;

    #region Cache
    private Vector2 m;
    private PlayerManager pm => PlayerManager.instance;
    private DomainManager dm => DomainManager.instance;
    #endregion

    #region Input
    public void _Update(InputAction.CallbackContext ctx)
    {
        if (!isDebugMode) m = ctx.ReadValue<Vector2>();
    }

    /// <summary>
    /// Attack joystick: works in BOTH AutoAttack states now. While
    /// AutoAttack is on, dragging this stick temporarily overrides
    /// auto-targeting (hybrid "lazy but can take manual control" playstyle);
    /// releasing it falls straight back to auto-target with no extra input
    /// needed. While AutoAttack is off, this stick is the only way to attack
    /// at all (pure manual playstyle).
    /// </summary>
    public void _Attack(InputAction.CallbackContext ctx)
    {
        if (pm == null) return;

        Vector2 stickValue = ctx.ReadValue<Vector2>();
        bool isPressed = stickValue.sqrMagnitude > 0.01f;

        pm.ManualAttacking = isPressed;
        if (isPressed)
            pm.ManualAttackDirection = stickValue.normalized;
    }
    #endregion

    #region Lifecycle
    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }

    public void Tick(float delta)
    {
        if (isDebugMode) m = PrimaryInput.GetInput();

        HandleMotion();
        HandleRotate();
    }
    #endregion

    #region Functions
    private void HandleMotion()
    {
        if (pm == null || pm.Controlling == null) return;

        pm.MoveInput = m;

        if (m != Vector2.zero) UnitMotion.MoveThisObject(pm.attribute, pm.Controlling, m);

        if (dm != null)
            pm.Controlling.transform.position = dm.ClampToMap(pm.Controlling.transform.position);
    }

    private void HandleRotate()
    {
        if (pm == null || pm.Controlling == null) return;

        // Manual stick, when held, ALWAYS wins - regardless of AutoAttack.
        // This is the "hybrid" behavior: lazy auto-aim by default, but
        // dragging the attack stick immediately takes priority.
        if (pm.ManualAttacking)
        {
            pm.Direction = pm.ManualAttackDirection;
        }
        else if (pm.AutoAttack && pm.Target != null)
        {
            pm.Direction = (pm.Target.position - pm.Controlling.transform.position).normalized;
        }
        else if (m != Vector2.zero)
        {
            pm.Direction = m;
        }

        UnitMotion.RotateThisObject(pm.Controlling, pm.Direction);
    }
    #endregion
}