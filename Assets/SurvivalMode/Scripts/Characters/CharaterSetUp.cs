using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Named colors for buttons. The numbers are what you type in the OnClick argument box.</summary>
public enum PresetColor
{
    White = 0, Black = 1, Gray = 2,
    Brown = 3, DarkBrown = 4, Blonde = 5, Ginger = 6,
    Red = 7, Orange = 8, Yellow = 9,
    Green = 10, Blue = 11, Purple = 12, Pink = 13,
    Tan = 14
}

public class CharacterSetUp : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private VisualDataBase db;
    [Tooltip("Starting gender. Unisex = no filter.")]
    [SerializeField] private ValidGender gender = ValidGender.Unisex;
    [Tooltip("Head color follows the body color (same skin on both).")]
    [SerializeField] private bool syncHeadWithBody = true;

    [Header("UI")]
    [SerializeField] private TMP_InputField nameField;

    [Header("Preview (UI Images, optional)")]
    [SerializeField] private Image bodyImage;
    [SerializeField] private Image headImage;
    [SerializeField] private Image hairImage;
    [SerializeField] private FacingDirection previewFacing = FacingDirection.Front;

    private PlayerData player;
    private int bodyIndex, headIndex, hairIndex;
    private int bodyColorIndex, headColorIndex, hairColorIndex;

    private void Awake()
    {
        player = new PlayerData
        {
            Name = "Player",
            Gender = gender,
            BodyColor = new PlayerData.ColorBodyPart(),
            HeadColor = new PlayerData.ColorBodyPart(),
            HairColor = new PlayerData.ColorBodyPart()
        };

        if (db == null)
        {
            Debug.LogError("CharacterSetUp: VisualDataBase is not assigned.", this);
            return;
        }

        // Pick the first valid option of everything, then draw once.
        InitDefaults();
        Refresh();

        if (nameField != null) nameField.SetTextWithoutNotify(player.Name);
    }

    /// <summary>Sets real IDs and colors directly, so no ID is ever left at 0.</summary>
    private void InitDefaults()
    {
        PickFirst(VisualSlot.Body, ref bodyIndex, ref player.BodyID);
        PickFirst(VisualSlot.Head, ref headIndex, ref player.HeadID);
        PickFirst(VisualSlot.Hair, ref hairIndex, ref player.HairID);

        bodyColorIndex = headColorIndex = hairColorIndex = 0;
        player.BodyColor = ToPart(db.SelectSkinColor(0));
        player.HeadColor = ToPart(db.SelectSkinColor(0));
        player.HairColor = ToPart(db.SelectHairColor(0));
    }

    private void PickFirst(VisualSlot slot, ref int index, ref int id)
    {
        index = 0;
        VisualPack pack = db.Select(slot, 0, gender);
        if (pack != null) id = pack.ID;
    }

    #region Name
    // Hook to the name field's OnValueChanged.
    public void SetName()
    {
        if (nameField == null) return;
        string value = nameField.text.Trim();
        player.Name = string.IsNullOrEmpty(value) ? "Player" : value;
    }
    #endregion

    #region Gender
    // Hook to a dropdown's OnValueChanged (0 = Male, 1 = Female, 2 = Unisex)
    // or to buttons with a typed argument.
    public void ChooseGender(int value)
    {
        if (db == null || player == null) return;

        gender = (ValidGender)Mathf.Clamp(value, 0, 2);
        player.Gender = gender;

        // Keep the current part if it is still valid for the new gender,
        // otherwise move to the next valid one.
        ReselectPart(VisualSlot.Body, ref bodyIndex, ref player.BodyID);
        ReselectPart(VisualSlot.Head, ref headIndex, ref player.HeadID);
        ReselectPart(VisualSlot.Hair, ref hairIndex, ref player.HairID);
        Refresh();
    }

    public void ChooseMale() => ChooseGender((int)ValidGender.Male);
    public void ChooseFemale() => ChooseGender((int)ValidGender.Female);

    private void ReselectPart(VisualSlot slot, ref int index, ref int id)
    {
        VisualPack pack = db.FindNearestValid(slot, id, gender);
        if (pack == null) return;

        id = pack.ID;
        index = Mathf.Max(0, db.IndexOfID(slot, id, gender));   // keep the cycling index in sync
    }
    #endregion

    #region Parts (hook buttons with -1 / +1)
    public void ChooseBody(int step)
    {
        if (db == null) return;
        ChoosePart(VisualSlot.Body, ref bodyIndex, ref player.BodyID, step);
        Refresh();
    }

    public void ChooseHead(int step)
    {
        if (db == null) return;
        ChoosePart(VisualSlot.Head, ref headIndex, ref player.HeadID, step);
        Refresh();
    }

    public void ChooseHair(int step)
    {
        if (db == null) return;
        ChoosePart(VisualSlot.Hair, ref hairIndex, ref player.HairID, step);
        Refresh();
    }

    private void ChoosePart(VisualSlot slot, ref int index, ref int id, int step)
    {
        index += step;
        VisualPack pack = db.Select(slot, index, gender);
        if (pack == null) return;

        id = pack.ID;

        // Keep the index inside 0..count-1 so it never drifts.
        int count = db.Count(slot, gender);
        if (count > 0) index = ((index % count) + count) % count;
    }
    #endregion

    #region Colors: palette cycling (hook buttons with -1 / +1)
    public void ChooseBodyColor(int step)
    {
        if (db == null) return;
        if (db.SkinColorCount < 2)
            Debug.LogWarning("CharacterSetUp: Skin Colors has fewer than 2 entries, so cycling can't change anything.", db);
        bodyColorIndex += step;
        SetBodyColor(db.SelectSkinColor(bodyColorIndex), false);
    }

    public void ChooseHeadColor(int step)
    {
        if (db == null || syncHeadWithBody) return;   // follows the body when synced
        headColorIndex += step;
        SetHeadColor(db.SelectSkinColor(headColorIndex), false);
    }

    public void ChooseHairColor(int step)
    {
        if (db == null) return;
        hairColorIndex += step;
        SetHairColor(db.SelectHairColor(hairColorIndex), false);
    }
    #endregion

    #region Colors: named presets (White, Black, Brown, Green, Blue, Red, Blonde, ...)
    // int versions: type the PresetColor number in the button's OnClick argument.
    public void ChooseBodyColorPreset(int preset) => SetBodyColor(ToColor(ClampPreset(preset)), true);
    public void ChooseHeadColorPreset(int preset) => SetHeadColor(ToColor(ClampPreset(preset)), true);
    public void ChooseHairColorPreset(int preset) => SetHairColor(ToColor(ClampPreset(preset)), true);

    // string versions: type the name ("Blonde", "blue", ...). Unknown names are ignored.
    public void ChooseBodyColorByName(string colorName)
    {
        if (TryPreset(colorName, out PresetColor p)) SetBodyColor(ToColor(p), true);
    }

    public void ChooseHeadColorByName(string colorName)
    {
        if (TryPreset(colorName, out PresetColor p)) SetHeadColor(ToColor(p), true);
    }

    public void ChooseHairColorByName(string colorName)
    {
        if (TryPreset(colorName, out PresetColor p)) SetHairColor(ToColor(p), true);
    }

    // Code-only overloads (UnityEvents can't pass a Color). Also usable from a color picker.
    public void SetBodyColor(Color color) => SetBodyColor(color, true);
    public void SetHeadColor(Color color) => SetHeadColor(color, true);
    public void SetHairColor(Color color) => SetHairColor(color, true);

    private void SetBodyColor(Color color, bool syncIndex)
    {
        if (player == null) return;
        player.BodyColor = ToPart(color);
        if (syncIndex) SyncIndex(ColorSlot.Skin, color, ref bodyColorIndex);

        if (syncHeadWithBody)
        {
            player.HeadColor = ToPart(color);
            headColorIndex = bodyColorIndex;
        }
        Refresh();
    }

    private void SetHeadColor(Color color, bool syncIndex)
    {
        if (player == null || syncHeadWithBody) return;
        player.HeadColor = ToPart(color);
        if (syncIndex) SyncIndex(ColorSlot.Skin, color, ref headColorIndex);
        Refresh();
    }

    private void SetHairColor(Color color, bool syncIndex)
    {
        if (player == null) return;
        player.HairColor = ToPart(color);
        if (syncIndex) SyncIndex(ColorSlot.Hair, color, ref hairColorIndex);
        Refresh();
    }

    /// <summary>If the color exists in the palette, move the cycling index to it so next/prev continue from there.</summary>
    private void SyncIndex(ColorSlot slot, Color color, ref int index)
    {
        if (db == null) return;
        int found = db.IndexOfColor(slot, color);
        if (found >= 0) index = found;
    }

    private static PresetColor ClampPreset(int value)
        => (PresetColor)Mathf.Clamp(value, 0, Enum.GetValues(typeof(PresetColor)).Length - 1);

    private static bool TryPreset(string colorName, out PresetColor preset)
    {
        preset = PresetColor.White;
        if (string.IsNullOrWhiteSpace(colorName)) return false;
        return Enum.TryParse(colorName.Trim(), true, out preset)
            && Enum.IsDefined(typeof(PresetColor), preset);
    }

    public static Color ToColor(PresetColor preset)
    {
        switch (preset)
        {
            case PresetColor.White: return new Color32(255, 255, 255, 255);
            case PresetColor.Black: return new Color32(35, 35, 38, 255);
            case PresetColor.Gray: return new Color32(140, 140, 145, 255);
            case PresetColor.Brown: return new Color32(110, 70, 40, 255);
            case PresetColor.DarkBrown: return new Color32(60, 38, 22, 255);
            case PresetColor.Blonde: return new Color32(240, 205, 110, 255);
            case PresetColor.Ginger: return new Color32(190, 90, 40, 255);
            case PresetColor.Red: return new Color32(205, 40, 35, 255);
            case PresetColor.Orange: return new Color32(240, 140, 30, 255);
            case PresetColor.Yellow: return new Color32(245, 225, 60, 255);
            case PresetColor.Green: return new Color32(60, 175, 70, 255);
            case PresetColor.Blue: return new Color32(55, 100, 225, 255);
            case PresetColor.Purple: return new Color32(140, 70, 190, 255);
            case PresetColor.Pink: return new Color32(240, 130, 180, 255);
            case PresetColor.Tan: return new Color32(220, 180, 140, 255);
            default: return Color.white;
        }
    }
    #endregion

    #region Confirm
    // Hook to the "Confirm Character" button.
    public void ConfirmCharacter()
    {
        WorldManager wm = WorldManager.instance;
        if (wm == null)
        {
            Debug.LogError("CharacterSetUp: no WorldManager in the scene.");
            return;
        }

        SetName();   // make sure the name matches the field

        if (wm.data == null) wm.data = new SaveData();
        wm.data.PlayerData = player;
        SaveSystem.Save(wm.data, WorldManager.Slot);
    }
    #endregion

    #region Preview (UI Image) and color helpers
    private void Refresh()
    {
        if (db == null || player == null) return;

        // A bad ID (for example 0) falls back to the first valid pack instead of hiding the part.
        ApplyToImage(bodyImage, db.GetByIDOrDefault(VisualSlot.Body, player.BodyID, gender), ToColor(player.BodyColor));
        ApplyToImage(headImage, db.GetByIDOrDefault(VisualSlot.Head, player.HeadID, gender), ToColor(player.HeadColor));
        ApplyToImage(hairImage, db.GetByIDOrDefault(VisualSlot.Hair, player.HairID, gender), ToColor(player.HairColor));
    }

    /// <summary>
    /// Image has no flipX, so Left is shown by negating the RectTransform's X scale.
    /// The Image is hidden when the pack has no sprite for this direction.
    /// </summary>
    private void ApplyToImage(Image img, VisualPack pack, Color tint)
    {
        if (img == null) return;

        if (pack == null || pack.Visual == null)
        {
            img.enabled = false;
            return;
        }

        Sprite sprite = pack.Visual.Get(previewFacing, out bool flipX);

        img.sprite = sprite;
        img.color = tint;
        img.preserveAspect = true;
        img.enabled = sprite != null;

        RectTransform rt = img.rectTransform;
        Vector3 scale = rt.localScale;
        scale.x = flipX ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        rt.localScale = scale;
    }

    public static PlayerData.ColorBodyPart ToPart(Color c) => new PlayerData.ColorBodyPart
    {
        Red = c.r,
        Green = c.g,
        Blue = c.b,
        Alpha = c.a
    };

    /// <summary>A missing part (old save) becomes white, the default skin.</summary>
    public static Color ToColor(PlayerData.ColorBodyPart p)
    {
        if (p == null) return Color.white;
        if (p.Red == 0f && p.Green == 0f && p.Blue == 0f && p.Alpha == 0f) return Color.white;
        return new Color(p.Red, p.Green, p.Blue, p.Alpha);
    }
    #endregion
}