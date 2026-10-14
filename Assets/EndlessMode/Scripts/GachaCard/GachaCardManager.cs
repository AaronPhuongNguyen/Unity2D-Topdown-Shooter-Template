using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GachaCardManager : MonoBehaviour
{
    [Header("Properties")]
    public Canvas canvas; //where show the selection UI
    public GachaCard card; // prefabs
    public CardDataBase cardDataBase; // where get the card

    // The card's target is resolved from PlayerManager at pick time (see OnCardPicked),
    // because PlayerManager re-clones its attribute on every Init/respawn.
    private static PlayerManager PM => PlayerManager.instance;

    [Header("Open Button")]
    [Tooltip("Opens the card selection. Interactable only while rewards are waiting in the queue. " +
             "Its onClick is wired automatically - don't also wire it in the Inspector. " +
             "Keep it OUTSIDE the selection canvas (or in _uiToHide) so it isn't clickable while picking.")]
    public Button openButton;
    [Tooltip("Optional: shows how many reward picks are waiting (e.g. 'x3'). Hidden when 0.")]
    public TextMeshProUGUI pendingCountText;

    [Header("Custom")]
    public Color BasicColor = Color.gray;                       // Basic = Gray
    public Color NormalColor = new Color(0.0f, 0.5f, 1f);       // Normal = Azure
    public Color StrongColor = new Color(1f, 0.84f, 0f);        // Strong = Golden

    [Header("Tray")] // on where position card selection UI will show
    public Transform tray1;
    public Transform tray2;
    public Transform tray3;

    [Header("Hide")]//In which UI will be hidden while canvas is shown.
    public GameObject[] _uiToHide;

    // Reward cycle (length 8), keyed to the level the player JUST reached.
    // Highest tier wins on overlap:
    //   level 2,6,10,... -> Basic | 4,12,... -> Normal | 8,16,... -> Strong
    private const int BasicEvery = 3;
    private const int NormalEvery = 6;
    private const int StrongEvery = 12;

    private readonly Queue<Category> _pending = new Queue<Category>(8);
    private readonly List<CardEffect> _drawn = new List<CardEffect>(3);
    private readonly List<GachaCard> _spawned = new List<GachaCard>(3);
    // Picked cards that have CardEffect.UniquePerRun = true. Passed to Draw as the exclusion set,
    // so those cards never come back this run. Cards without the flag are never added here.
    private readonly HashSet<CardEffect> _pickedUnique = new HashSet<CardEffect>();
    private Transform[] _trays;

    private Category _currentCategory;
    private bool _isOpen;
    private float _speedBeforePause = 1f;

    private RectTransform[] _rects;
    private Vector2[] _defaultPos;

    // The UnitLevel we are currently listening to (so we can always unsubscribe cleanly).
    private UnitLevel _levelSubscribed;

    private void Awake()
    {
        _trays = new[] { tray1, tray2, tray3 };

        // Remember the layout set up in the scene (e.g. 0, -500, 500) so it can be re-arranged per card count.
        _rects = new RectTransform[_trays.Length];
        _defaultPos = new Vector2[_trays.Length];
        for (int i = 0; i < _trays.Length; i++)
        {
            _rects[i] = _trays[i] as RectTransform;
            if (_rects[i] != null) _defaultPos[i] = _rects[i].anchoredPosition;
        }

        if (canvas != null) canvas.enabled = false;

        if (openButton != null)
            openButton.onClick.AddListener(OnOpenClicked);
    }

    private void Start()
    {
        // Fallback in case PlayerManager wasn't ready during OnEnable.
        SubscribeToLevel();
        RefreshButton();
    }

    private void OnDestroy()
    {
        if (openButton != null)
            openButton.onClick.RemoveListener(OnOpenClicked);
    }

    /// <summary>
    /// Returns which trays to use, left to right, and positions them nicely:
    /// 1 card  -> centered (tray1's default spot)
    /// 2 cards -> tray2/tray3 pulled in toward the center (halfway to their defaults), so the pair looks balanced
    /// 3 cards -> default layout, ordered left, center, right.
    /// </summary>
    private int[] LayoutFor(int count)
    {
        // Reset everything to the scene defaults first.
        for (int i = 0; i < _rects.Length; i++)
            if (_rects[i] != null) _rects[i].anchoredPosition = _defaultPos[i];

        switch (count)
        {
            case 1:
                return new[] { 0 };
            case 2:
                if (_rects[1] != null) _rects[1].anchoredPosition = Vector2.Lerp(_defaultPos[0], _defaultPos[1], 0.5f);
                if (_rects[2] != null) _rects[2].anchoredPosition = Vector2.Lerp(_defaultPos[0], _defaultPos[2], 0.5f);
                return new[] { 1, 2 };
            default:
                return new[] { 1, 0, 2 };
        }
    }

    private void OnEnable()
    {
        EventBus.OnGameRestart += ResetRun;
        SubscribeToLevel();
        RefreshButton();
    }

    private void OnDisable()
    {
        EventBus.OnGameRestart -= ResetRun;
        UnsubscribeFromLevel();
        if (_isOpen) Close(); // never leave the game paused
    }

    #region Level Subscription
    // PlayerManager.Level is created once and survives respawns / new games (Reset() keeps the
    // delegates), so one subscription is enough for the whole session.
    private void SubscribeToLevel()
    {
        UnitLevel level = PM != null ? PM.Level : null;
        if (level == null || level == _levelSubscribed) return;

        UnsubscribeFromLevel();
        level.OnLevelUp += HandleLevelUp;
        _levelSubscribed = level;
    }

    private void UnsubscribeFromLevel()
    {
        if (_levelSubscribed == null) return;
        _levelSubscribed.OnLevelUp -= HandleLevelUp;
        _levelSubscribed = null;
    }
    #endregion

    private void ResetRun()
    {
        _pickedUnique.Clear();
        _pending.Clear();
        if (_isOpen) Close();
        RefreshButton();
    }

    // UnitLevel increments Level_Current BEFORE invoking OnLevelUp, so this is the level just reached.
    // Several level-ups from one EXP gain fire this once per level, each with its own value.
    private void HandleLevelUp()
    {
        if (_levelSubscribed == null) return;

        int reached = _levelSubscribed.Level_Current;
        if (reached <= 1) return;

        Category? reward = null;
        if (reached % StrongEvery == 0) reward = Category.Strong;
        else if (reached % NormalEvery == 0) reward = Category.Normal;
        else if (reached % BasicEvery == 0) reward = Category.Basic;

        if (!reward.HasValue) return;

        // Only queue it. The player opens the selection with the button.
        _pending.Enqueue(reward.Value);
        RefreshButton();
    }

    #region Button
    private void OnOpenClicked()
    {
        if (_isOpen || _pending.Count == 0) return;
        ShowNext();
    }

    /// <summary>Button.interactable = there is at least one reward waiting (and nothing is open).</summary>
    private void RefreshButton()
    {
        int count = _pending.Count;

        if (openButton != null)
            openButton.interactable = count > 0 && !_isOpen;

        if (pendingCountText != null)
        {
            pendingCountText.gameObject.SetActive(count > 0);
            pendingCountText.text = $"x{count}";
        }
    }
    #endregion

    private void ShowNext()
    {
        while (_pending.Count > 0)
        {
            Category cat = _pending.Dequeue();
            // Draw returns only DISTINCT cards: if just one is available,
            // exactly one is shown instead of three copies.
            if (cardDataBase.Draw(cat, 3, _drawn, _pickedUnique) > 0)
            {
                _currentCategory = cat;
                Open();
                return;
            }
            Debug.LogWarning($"GachaCardManager: no available {cat} cards, skipping reward.");
        }

        RefreshButton(); // queue drained without opening anything
    }

    #region Debugger (right-click the component header in Play Mode)
    private void DebugReward(Category cat)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("GachaCardManager: enter Play Mode to test.");
            return;
        }
        _pending.Enqueue(cat); // queued only - press the button to open
        RefreshButton();
    }

    [ContextMenu("Test/Queue Basic")]
    private void TestBasic() => DebugReward(Category.Basic);

    [ContextMenu("Test/Queue Normal")]
    private void TestNormal() => DebugReward(Category.Normal);

    [ContextMenu("Test/Queue Strong")]
    private void TestStrong() => DebugReward(Category.Strong);

    [ContextMenu("Test/Queue Basic + Normal + Strong")]
    private void TestAll()
    {
        DebugReward(Category.Basic);
        DebugReward(Category.Normal);
        DebugReward(Category.Strong);
    }

    [ContextMenu("Test/Clear Unique Picked Cards")]
    private void TestClearPicked()
    {
        _pickedUnique.Clear();
        Debug.Log("GachaCardManager: unique-picked cards cleared.");
    }
    #endregion

    private Color ColorFor(Category cat)
    {
        switch (cat)
        {
            case Category.Strong: return StrongColor;
            case Category.Normal: return NormalColor;
            default: return BasicColor;
        }
    }

    private void Open()
    {
        _isOpen = true;

        // Pause through TickSystem, remembering the speed to restore.
        _speedBeforePause = TickSystem.GameSpeed > 0f ? TickSystem.GameSpeed : 1f;
        TickSystem.PauseGame();

        for (int i = 0; i < _uiToHide.Length; i++)
            if (_uiToHide[i] != null) _uiToHide[i].SetActive(false);

        canvas.enabled = true;

        Color bg = ColorFor(_currentCategory);
        int[] slots = LayoutFor(Mathf.Min(_drawn.Count, _trays.Length));
        for (int i = 0; i < slots.Length; i++)
        {
            GachaCard c = Instantiate(card, _trays[slots[i]]);
            c.Setup(_drawn[i], bg, OnCardPicked);
            _spawned.Add(c);
        }

        RefreshButton();
    }

    private void OnCardPicked(CardEffect effect)
    {
        UnitAttribute attr = PM != null ? PM.attribute : null;
        if (attr == null)
        {
            Debug.LogWarning("GachaCardManager: PlayerManager or its attribute is missing, card not applied.");
        }
        else
        {
            if (effect.UniquePerRun) _pickedUnique.Add(effect); // only cards flagged UniquePerRun are locked out
            effect.DrawCard(attr);
        }
        Close();
        ShowNext(); // if several rewards are queued, keep going with the next one
    }

    private void Close()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
            if (_spawned[i] != null) Destroy(_spawned[i].gameObject);
        _spawned.Clear();

        canvas.enabled = false;

        for (int i = 0; i < _uiToHide.Length; i++)
            if (_uiToHide[i] != null) _uiToHide[i].SetActive(true);

        TickSystem.SetGameSpeed(_speedBeforePause);
        _isOpen = false;

        RefreshButton();
    }
}