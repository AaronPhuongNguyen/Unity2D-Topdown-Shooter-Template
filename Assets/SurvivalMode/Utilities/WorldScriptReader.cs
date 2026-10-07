using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorldScriptReader : MonoBehaviour
{
    public const int MinSize = 300;
    public const int MaxSize = 1500;
    public const int DefaultSize = 900;

    [Header("World Scripted")]
    [SerializeField] private WorldScript ws;

    [Header("UI References")]
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private TMP_InputField seedField;
    [SerializeField] private TMP_InputField sizeField;
    [SerializeField] private Toggle keepInventoryToggle;
    [SerializeField] private TMP_Dropdown difficultyDropdown; // order: Easy, Medium, Hard

    GameManager gm;

    private void Awake()
    {
        ws = new WorldScript();
        SyncUIFromData();
    }

    #region Script
    //OnValueChanged
    // input field OnValueChanged
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

    public void InputWorldSize() //Int 300 will cast 300x300.
    {
        int size = DefaultSize;
        if (int.TryParse(sizeField.text, out int parsed))
            size = Mathf.Clamp(parsed, MinSize, MaxSize);

        ws.WorldSize = new Vector2(size, size);
    }

    // Hook this to the size field's OnEndEdit so the text shows the clamped value.
    public void RefreshWorldSizeText()
    {
        sizeField.SetTextWithoutNotify(((int)ws.WorldSize.x).ToString());
    }

    //Toggle
    public void InputKeepInventory()
    {
        ws.KeepInventory = keepInventoryToggle.isOn;
    }

    //Dropdown
    public void InputDifficulty()
    {
        ws.Difficulty = difficultyDropdown.value switch
        {
            0 => SurvivalDifficulty.Easy,
            1 => SurvivalDifficulty.Medium,
            _ => SurvivalDifficulty.Hardcode
        };
    }

    public void SetNewGame()
    {
        ws.IsNew = true;
    }

    #endregion

    // Push the WorldScript defaults into the UI without firing the change events.
    private void SyncUIFromData()
    {
        if(nameField!=null) nameField.SetTextWithoutNotify(ws.WorldName);
        if(seedField!=null) seedField.SetTextWithoutNotify(ws.Seed.ToString());
        if(sizeField!=null) sizeField.SetTextWithoutNotify(((int)ws.WorldSize.x).ToString());
        if(keepInventoryToggle!=null) keepInventoryToggle.SetIsOnWithoutNotify(ws.KeepInventory);
        if(difficultyDropdown !=null) difficultyDropdown.SetValueWithoutNotify((int)ws.Difficulty - 1);
    }
}