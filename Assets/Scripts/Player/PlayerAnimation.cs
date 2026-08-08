using Unity.VisualScripting;
using UnityEngine;

public class PlayerAnimation : MonoBehaviour
{
    public Animator anim;

    #region Cache
    public PlayerManager pm => PlayerManager.instance;

    private static readonly int IsAttackHash = Animator.StringToHash("IsAttacking");
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    private static readonly int IsIdleHash = Animator.StringToHash("IsIdle");

    #endregion

    private void Update()
    {
        UpdateAnimator();
    }
    private void UpdateAnimator()
    {
        if (anim == null) return;

        anim.SetBool(IsWalkingHash, pm.IsMoving);
        anim.SetBool(IsIdleHash,pm.IsIdle);
        anim.SetBool(IsAttackHash,pm.IsAttacking);
    }
}