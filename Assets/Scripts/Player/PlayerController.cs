using Server;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
public class PlayerController : MonoBehaviour
{
    [Header("Debugger")]
    [SerializeField] private bool isDebugMode = false;

    #region Cache
    private Vector2 m;
    private PlayerManager pm => PlayerManager.instance;
    private DomainManager dm => DomainManager.instance;
    #endregion

    #region Input
    // Legacy input (debug only)
    private void Update()
    {

        if (isDebugMode) m = PrimaryInput.GetInput();

        HandleMotion();
        HandleRotate();
    }

    // New Input System
    public void _Update(InputAction.CallbackContext ctx)
    {
        if (!isDebugMode) m = ctx.ReadValue<Vector2>();
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

        if (pm.IsAttacking && pm.Target != null)
            pm.Direction = (pm.Target.position - pm.Controlling.transform.position).normalized;
        else if (!pm.IsAttacking && m != Vector2.zero)
            pm.Direction = m;

        UnitMotion.RotateThisObject(pm.Controlling, pm.Direction);
    }
    #endregion
}