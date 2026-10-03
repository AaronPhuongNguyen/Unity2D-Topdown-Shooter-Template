using UnityEngine;

public class PlayerAnimation : MonoBehaviour, ITick
{
    public Animator anim;

    #region Cache
    public PlayerManager pm => PlayerManager.instance;

    private static readonly int IsAttackHash = Animator.StringToHash("IsAttacking");
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");
    #endregion

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
        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        if (anim == null) return;
        if (pm == null) return;

        anim.SetBool(IsWalkingHash, pm.IsMoving);
        anim.SetBool(IsIdleHash, pm.IsIdle);
        anim.SetBool(IsAttackHash, pm.IsAttacking);
    }
}