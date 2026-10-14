using Server;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the respawn price while the player is dead.
/// - Panel is visible only when the player is dead.
/// - Price text turns red when the player can't afford it.
/// - Button is interactable only when affordable; it calls DomainManager.RespawnPlayer().
///
/// Setup: put this on an ALWAYS-ACTIVE object (e.g. the Canvas) and assign the dead-screen
/// panel to 'panel'. If this script sits on the panel itself, hiding the panel disables it,
/// unregisters it from the TickSystem, and it can never show the panel again.
/// The button's OnClick is wired automatically (don't wire it twice in the Inspector).
/// </summary>
public class RespawnPriceUI : MonoBehaviour, ITick
{
    [Header("References")]
    [Tooltip("Panel toggled on/off with player death. Must NOT be the object this script is on.")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Button respawnButton;
    [SerializeField] private TextMeshProUGUI priceText;
    [Tooltip("Optional: shows 'You have: X'.")]
    [SerializeField] private TextMeshProUGUI balanceText;
    [Tooltip("Optional: shows 'Next respawn: X'.")]
    [SerializeField] private TextMeshProUGUI nextPriceText;
    [Tooltip("Optional: shows 'Respawns used: N'.")]
    [SerializeField] private TextMeshProUGUI attemptsText;

    [Header("Colors")]
    [SerializeField] private Color affordableColor = Color.white;
    [SerializeField] private Color tooExpensiveColor = new Color(1f, 0.3f, 0.3f);

    // Cached values so text is only rebuilt when something actually changed.
    private int lastPrice = -1;
    private int lastBalance = -1;
    private int lastAttempts = -1;
    private bool lastAffordable;
    private bool lastVisible;
    private DomainManager subscribedTo;

    #region Init
    private void Awake()
    {
        if (respawnButton != null)
            respawnButton.onClick.AddListener(OnRespawnClicked);

        // Start hidden; Tick shows the panel once the player is dead.
        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (respawnButton != null)
            respawnButton.onClick.RemoveListener(OnRespawnClicked);
    }

    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ITick)this);

        // DomainManager (execution order -5) is normally ready by now, but guard anyway.
        subscribedTo = DomainManager.instance;
        if (subscribedTo != null)
            subscribedTo.OnRespawnPurchased += ForceRefresh;

        ForceRefresh();
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ITick)this);

        if (subscribedTo != null)
            subscribedTo.OnRespawnPurchased -= ForceRefresh;
        subscribedTo = null;
    }
    #endregion

    #region Cycle
    public void Tick(float delta)
    {
        DomainManager dm = DomainManager.instance;
        if (dm == null) return;

        PlayerManager pm = dm.PM;
        bool visible = pm != null && pm.IsDead;

        if (visible != lastVisible)
        {
            lastVisible = visible;
            if (panel != null) panel.SetActive(visible);
            if (visible) ForceRefresh();
        }

        if (!visible) return;

        // Currency can change while dead (e.g. delayed rewards), so poll cheaply.
        if (dm.PriceToRespawn != lastPrice ||
            dm.Currency != lastBalance ||
            dm.RespawnAttempts != lastAttempts)
        {
            Refresh(dm);
        }
    }
    #endregion

    #region Refresh
    private void ForceRefresh()
    {
        lastPrice = lastBalance = lastAttempts = -1;
        DomainManager dm = DomainManager.instance;
        if (dm != null) Refresh(dm);
    }

    private void Refresh(DomainManager dm)
    {
        lastPrice = dm.PriceToRespawn;
        lastBalance = dm.Currency;
        lastAttempts = dm.RespawnAttempts;
        lastAffordable = dm.CanAffordRespawn;

        if (priceText != null)
        {
            priceText.text = $"Respawn: {Format(lastPrice)}";
            priceText.color = lastAffordable ? affordableColor : tooExpensiveColor;
        }

        if (balanceText != null)
        {
            balanceText.text = $"You have: {Format(lastBalance)}";
            balanceText.color = lastAffordable ? affordableColor : tooExpensiveColor;
        }

        if (nextPriceText != null)
            nextPriceText.text = $"Next respawn: {Format(dm.NextPriceToRespawn)}";

        if (attemptsText != null)
            attemptsText.text = $"Respawns used: {lastAttempts}";

        if (respawnButton != null)
            respawnButton.interactable = lastAffordable;
    }
    #endregion

    #region Button
    private void OnRespawnClicked()
    {
        if (DomainManager.instance != null)
            DomainManager.instance.RespawnPlayer();
    }
    #endregion

    /// <summary>Exact value with thousands separators: 22500 -> "22,500".</summary>
    private static string Format(int value) => value.ToString("N0");
}