using UnityEngine;

/// <summary>
/// Base class for map supplies (heal pads, buff zones, etc.) that
/// periodically scan nearby HurtBoxes and apply an effect to eligible
/// units. Subclasses (HealBuffer, etc.) only need to implement
/// ApplyEffect() - all the scanning/filtering/timing/cooldown is handled here.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BufferCore : MonoBehaviour, ITick
{
    #region Inspector
    [Header("Detection")]
    [SerializeField] protected float checkRadius = 1.5f;
    [SerializeField] protected float checkInterval = 0.5f;
    [SerializeField] protected LayerMask unitMask;

    [Header("Cooldown")]
    [Tooltip("After being used by a Friendly unit, the supply becomes unavailable for this many seconds.")]
    [SerializeField] protected float cooldownDuration = 60f;

    [Header("Visual")]
    [SerializeField] protected SpriteRenderer sr;
    #endregion

    #region Cache
    private Collider2D[] hitBuffer = new Collider2D[16];
    private float checkTimer;
    private float cooldownRemaining;

    public bool IsOnCooldown => cooldownRemaining > 0f;
    public float CooldownRemaining => cooldownRemaining;
    public float CooldownDuration => cooldownDuration;
    #endregion

    #region Lifecycle
    protected virtual void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);

        UpdateVisual();
    }

    protected virtual void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);
    }

    public void Tick(float delta)
    {
        TickCooldown(delta);

        checkTimer -= delta;
        if (checkTimer > 0f) return;
        checkTimer = checkInterval;

        if (IsOnCooldown) return;
        ScanAndApply();
    }
    #endregion

    #region Scan
    private void ScanAndApply()
    {
        int count = Physics2D.OverlapCircleNonAlloc(transform.position, checkRadius, hitBuffer, unitMask);

        if (count >= hitBuffer.Length)
        {
            hitBuffer = new Collider2D[hitBuffer.Length * 2];
            ScanAndApply();
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (IsOnCooldown) return; // a previous hit this scan already triggered cooldown

            Collider2D col = hitBuffer[i];
            if (col == null) continue;
            if (!col.TryGetComponent(out HurtBox hurtBox)) continue;

            UnitPackage package = hurtBox.Access();
            if (package?.attribute == null) continue;
            if (package.attribute.tag == UnitTag.Aggressive) continue;
            if (!CanAffect(package, hurtBox)) continue;

            ApplyEffect(package, hurtBox);

            if (package.attribute.tag == UnitTag.Friendly)
                StartCooldown();
        }
    }

    /// <summary>Default: Friendly units only. Override to widen eligibility.</summary>
    protected virtual bool CanAffect(UnitPackage package, HurtBox hurtBox) => package.attribute.tag == UnitTag.Friendly;

    /// <summary>Subclasses implement the actual effect (heal, buff, etc.) here.</summary>
    protected virtual void ApplyEffect(UnitPackage package, HurtBox hurtBox) { }
    #endregion

    #region Cooldown
    private void TickCooldown(float delta)
    {
        if (cooldownRemaining <= 0f) return;

        cooldownRemaining -= delta;
        if (cooldownRemaining > 0f) return;

        cooldownRemaining = 0f;
        UpdateVisual(); // fires once, exactly on the frame cooldown ends
    }

    protected void StartCooldown()
    {
        cooldownRemaining = cooldownDuration;
        UpdateVisual();
    }

    [ContextMenu("Force Reset Cooldown")]
    public void ResetCooldown()
    {
        cooldownRemaining = 0f;
        UpdateVisual();
    }
    #endregion

    #region Visual
    private void UpdateVisual()
    {
        if (sr == null) return;
        sr.enabled = !IsOnCooldown;
    }
    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsOnCooldown ? Color.gray : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
#endif
}