using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorldScriptReader : MonoBehaviour
{
    public const uint MinSize = 300;
    public const uint MaxSize = 1500;
    public const uint DefaultSize = 900;

    // Difficulty multipliers, indexed by dropdown order: Easy, Medium, Hard, Insane
    private static readonly float[] DifficultyValues = { 0.5f, 1f, 2f, 4f};

    [Header("World Scripted")]
    [SerializeField] private WorldScript ws = new WorldScript();

    [Header("Create World")]
    [SerializeField] private bool loadAfterCreate = true;

    [Header("UI References")]
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private TMP_InputField seedField;
    [SerializeField] private TMP_InputField sizeField;
    [SerializeField] private Toggle keepInventoryToggle;
    [SerializeField] private TMP_Dropdown difficultyDropdown; // order: Easy, Medium, Hard

    private void Awake()
    {
        if (ws == null) ws = new WorldScript();
        SyncUIFromData();
    }

    #region Script

    // Hook to the name field's OnValueChanged
    public void InputName()
    {
        string value = nameField.text.Trim();
        ws.WorldName = string.IsNullOrEmpty(value) ? "World" : value;
    }

    public void InputSeed()
    {
        // Empty or invalid input keeps the previous seed.
        if (uint.TryParse(seedField.text, out uint seed))
            ws.Seed = seed;
    }

    public void InputWorldSize() // 300 means a 300x300 world.
    {
        uint size = DefaultSize;
        if (uint.TryParse(sizeField.text, out uint parsed))
            size = (uint)Mathf.Clamp((int)System.Math.Min(parsed, int.MaxValue),
                                     (int)MinSize, (int)MaxSize);

        ws.WorldSize = size;
    }

    // Hook this to the size field's OnEndEdit so the text shows the clamped value.
    public void RefreshWorldSizeText()
    {
        InputWorldSize();
        sizeField.SetTextWithoutNotify(ws.WorldSize.ToString());
    }

    public void InputKeepInventory()
    {
        ws.KeepInventory = keepInventoryToggle.isOn;
    }

    public void InputDifficulty()
    {
        int i = Mathf.Clamp(difficultyDropdown.value, 0, DifficultyValues.Length - 1);
        ws.Difficulty = DifficultyValues[i];
    }

    public void SetNewGame()
    {
        ws.IsNew = true;
    }

    // Hook to the ConfirmCreateWorld button's OnClick.
    public void ConfirmCreateWorld()
    {
        if (WorldManager.instance == null)
        {
            Debug.LogError("WorldScriptReader: no WorldManager in the scene.");
            return;
        }

        // Make sure ws matches the UI even if the player never left a field.
        InputName();
        InputSeed();
        RefreshWorldSizeText();   // also runs InputWorldSize()
        InputKeepInventory();
        InputDifficulty();
        SetNewGame();

        WorldManager.instance.CreateWorld(ws);

        if (loadAfterCreate)
            WorldManager.instance.Continue();
    }
    #endregion

    // Push the WorldScript values into the UI without firing the change events.
    private void SyncUIFromData()
    {
        if (nameField != null) nameField.SetTextWithoutNotify(ws.WorldName);
        if (seedField != null) seedField.SetTextWithoutNotify(ws.Seed.ToString());
        if (sizeField != null) sizeField.SetTextWithoutNotify(ws.WorldSize.ToString());
        if (keepInventoryToggle != null) keepInventoryToggle.SetIsOnWithoutNotify(ws.KeepInventory);
        if (difficultyDropdown != null)
            difficultyDropdown.SetValueWithoutNotify(DifficultyIndex(ws.Difficulty));
    }

    private static int DifficultyIndex(float difficulty)
    {
        for (int i = 0; i < DifficultyValues.Length; i++)
            if (Mathf.Approximately(DifficultyValues[i], difficulty))
                return i;
        return 0;
    }

}