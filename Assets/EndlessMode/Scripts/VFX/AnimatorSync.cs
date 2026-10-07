using UnityEngine;

/// <summary>
/// Drives an Animator's playback speed from TickSystem instead of Unity's
/// own Update(), so animations scale with GameSpeed and fully stop when
/// the game is paused (GameSpeed == 0) - instead of continuing to animate
/// via Animator's own internal clock, which ignores GameSpeed entirely.
/// </summary>
public class TickAnimator : MonoBehaviour, ITick
{
    #region Inspector
    [SerializeField] private Animator anim;
    #endregion

    #region Cache
    private float lastDelta;
    #endregion

    #region Lifecycle
    private void Reset()
    {
        if (anim == null) anim = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);

        // Component going inactive shouldn't leave the Animator stuck at
        // whatever speed it last had (e.g. 0 from a pause).
        if (anim != null) anim.speed = 1f;
    }

    public void Tick(float delta)
    {
        if (anim == null) return;
        if (delta == lastDelta) return; // skip redundant writes to Animator.speed
        lastDelta = delta;

        // delta is already Time.deltaTime * GameSpeed (see TickSystem), so
        // reconstructing GameSpeed and setting it as Animator.speed makes
        // the Animator's own clock track GameSpeed directly: speed 0 fully
        // freezes the animation (no interpolation, no clip advance at all),
        // not just "very slow".
        anim.speed = TickSystem.GameSpeed;
    }
    #endregion

    #region Public API
    /// <summary>Manually force a specific playback speed multiplier on top of GameSpeed, if ever needed (e.g. a slow debuff on one unit only).</summary>
    public void SetSpeedMultiplier(float multiplier)
    {
        if (anim == null) return;
        anim.speed = TickSystem.GameSpeed * multiplier;
    }
    #endregion
}