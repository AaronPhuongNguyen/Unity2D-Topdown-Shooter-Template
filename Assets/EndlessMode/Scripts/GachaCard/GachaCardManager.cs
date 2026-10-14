using System.Collections.Generic;
using UnityEngine;

public class GachaCardManager : MonoBehaviour
{
    [Header("Properties")]
    public Canvas canvas; //where show the selection UI
    public GachaCard card; // prefabs
    public CardDataBase cardDataBase; // where get the card

    // The card's target is resolved from PlayerManager at pick time (see OnCardPicked),
    // because PlayerManager re-clones its attribute on every Init/respawn.
    private static PlayerManager PM => PlayerManager.instance;

    [Header("Custom")]
    public Color BasicColor = Color.gray;                       // Basic = Gray
    public Color NormalColor = new Color(0.0f, 0.5f, 1f);       // Normal = Azure
    public Color StrongColor = new Color(1f, 0.84f, 0f);        // Strong = Golden

    [Header("Rules")]
    [Tooltip("If true, a card you already picked this run is never offered again. " +
             "Cards are never duplicated inside one selection either way.")]
    public bool uniquePerRun = true;

    [Header("Tray")] // on where position card selection UI will show
    public Transform tray1;
    public Transform tray2;
    public Transform tray3;

    [Header("Hide")]//In which UI will be hidden while canvas is shown.
    public GameObject[] _uiToHide;

    // Reward cycle (length 8, counted from wave 1), highest tier wins on overlap:
    //   completed wave 2,6,10,... -> Basic | 4,12,... -> Normal | 8,16,... -> Strong
    private const int BasicEvery = 2;
    private const int NormalEvery = 4;
    private const int StrongEvery = 8;

    private readonly Queue<Category> _pending = new Queue<Category>(4);
    private readonly List<CardEffect> _drawn = new List<CardEffect>(3);
    private readonly List<GachaCard> _spawned = new List<GachaCard>(3);
    private readonly HashSet<CardEffect> _picked = new HashSet<CardEffect>();
    private Transform[] _trays;

    private Category _currentCategory;
    private bool _isOpen;
    private float _speedBeforePause = 1f;

    private RectTransform[] _rects;
    private Vector2[] _defaultPos;

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
        EventBus.OnWaveCleared += HandleWaveCleared;
        EventBus.OnGameRestart += ResetRun;
    }

    private void OnDisable()
    {
        EventBus.OnWaveCleared -= HandleWaveCleared;
        EventBus.OnGameRestart -= ResetRun;
        if (_isOpen) Close(); // never leave the game paused
    }

    private void ResetRun()
    {
        _picked.Clear();
        _pending.Clear();
        if (_isOpen) Close();
    }

    // DomainManager calls HandleWave(1) BEFORE raising WaveCleared,
    // so the wave that was just completed is CurrentWave - 1.
    private void HandleWaveCleared()
    {
        var dm = DomainManager.instance;
        if (dm == null) return;

        int completed = dm.CurrentWave - 1;
        if (completed <= 0) return;

        Category? reward = null;
        if (completed % StrongEvery == 0) reward = Category.Strong;
        else if (completed % NormalEvery == 0) reward = Category.Normal;
        else if (completed % BasicEvery == 0) reward = Category.Basic;

        if (!reward.HasValue) return;

        _pending.Enqueue(reward.Value);
        if (!_isOpen) ShowNext();
    }

    private void ShowNext()
    {
        while (_pending.Count > 0)
        {
            Category cat = _pending.Dequeue();
            // Draw returns only DISTINCT cards: if just one is available,
            // exactly one is shown instead of three copies.
            if (cardDataBase.Draw(cat, 3, _drawn, uniquePerRun ? _picked : null) > 0)
            {
                _currentCategory = cat;
                Open();
                return;
            }
            Debug.LogWarning($"GachaCardManager: no available {cat} cards, skipping reward.");
        }
    }

    #region Debugger (right-click the component header in Play Mode)
    private void DebugReward(Category cat)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("GachaCardManager: enter Play Mode to test.");
            return;
        }
        _pending.Enqueue(cat);
        if (!_isOpen) ShowNext();
    }

    [ContextMenu("Test/Show Basic")]
    private void TestBasic() => DebugReward(Category.Basic);

    [ContextMenu("Test/Show Normal")]
    private void TestNormal() => DebugReward(Category.Normal);

    [ContextMenu("Test/Show Strong")]
    private void TestStrong() => DebugReward(Category.Strong);

    [ContextMenu("Test/Queue Basic + Normal + Strong")]
    private void TestAll()
    {
        DebugReward(Category.Basic);
        DebugReward(Category.Normal);
        DebugReward(Category.Strong);
    }

    [ContextMenu("Test/Clear Picked Cards")]
    private void TestClearPicked()
    {
        _picked.Clear();
        Debug.Log("GachaCardManager: picked cards cleared.");
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
            _picked.Add(effect);
            effect.DrawCard(attr);
        }
        Close();
        ShowNext(); // if several rewards queued up, show the next one
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
    }
}