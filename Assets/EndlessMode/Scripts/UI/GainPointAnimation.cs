using UnityEngine;

public class GPAnimation : MonoBehaviour
{
    public Animator anim;

    private static readonly int play = Animator.StringToHash("Play");

    private void OnEnable()
    {
        EventBus.OnWaveCleared += PlayAnimation;
    }
    private void OnDisable()
    {
        EventBus.OnWaveCleared -= PlayAnimation;
    }
    void PlayAnimation()
    {
        if (anim != null) anim.SetTrigger(play);
    }
}