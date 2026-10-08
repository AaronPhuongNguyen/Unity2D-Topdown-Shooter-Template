using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Visual Package", menuName = "Survival/Package/Visual")]
public class VisualDataBase : ScriptableObject
{
    public VisualPack[] Body;
    public VisualPack[] Head;
    public VisualPack[] Hair;

    // ===== NEW: color palettes ==========================================
    // Sprites are tinted by multiplying, so white = the sprite's own colors.
    // Put your sprites' skin in the palest tone and let darker tints do the rest.
    public static readonly Color DefaultSkinColor = Color.white;   // human skin (sheer white)
    public static readonly Color DefaultHairColor = Color.white;

    [Header("Color Palettes")]
    public Color[] SkinColors = { Color.white };
    public Color[] HairColors = { Color.white };
    // =====================================================================

    // Lazy ID lookup per slot. Rebuilt whenever the asset changes.
    private Dictionary<int, VisualPack>[] idCache;

    private void OnEnable() => idCache = null;

    #region Select by index (cycles, any int is valid)
    public VisualPack Select(VisualSlot slot, int index, ValidGender gender = ValidGender.Unisex)
        => Pick(GetList(slot), index, gender);

    public VisualPack SelectBody(int index, ValidGender gender = ValidGender.Unisex) => Select(VisualSlot.Body, index, gender);
    public VisualPack SelectHead(int index, ValidGender gender = ValidGender.Unisex) => Select(VisualSlot.Head, index, gender);
    public VisualPack SelectHair(int index, ValidGender gender = ValidGender.Unisex) => Select(VisualSlot.Hair, index, gender);

    /// <summary>
    /// Keeps the pack if it is valid for the gender. Otherwise returns the next valid pack
    /// after it in the array, wrapping around. If the ID is unknown, starts from the top.
    /// </summary>
    public VisualPack FindNearestValid(VisualSlot slot, int id, ValidGender gender)
    {
        VisualPack[] list = GetList(slot);
        if (list == null || list.Length == 0) return null;

        int begin = 0;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] != null && list[i].ID == id) { begin = i; break; }
        }

        for (int step = 0; step < list.Length; step++)
        {
            VisualPack p = list[(begin + step) % list.Length];
            if (Matches(p, gender)) return p;   // step 0 = the current pack, kept if valid
        }
        return null;
    }
    #endregion

    #region NEW: Colors (palette cycling, save/load, apply)
    // ---- Cycle through the palette (any int is valid, empty palette -> default) ----
    public Color SelectColor(ColorSlot slot, int index)
    {
        Color[] palette = GetPalette(slot);
        if (palette == null || palette.Length == 0) return DefaultOf(slot);
        return palette[Wrap(index, palette.Length)];
    }

    public Color SelectSkinColor(int index) => SelectColor(ColorSlot.Skin, index);
    public Color SelectHairColor(int index) => SelectColor(ColorSlot.Hair, index);

    public int ColorCount(ColorSlot slot)
    {
        Color[] palette = GetPalette(slot);
        return palette == null ? 0 : palette.Length;
    }

    public int SkinColorCount => ColorCount(ColorSlot.Skin);
    public int HairColorCount => ColorCount(ColorSlot.Hair);

    /// <summary>Palette position of a color, or -1 if it is not in the palette.</summary>
    public int IndexOfColor(ColorSlot slot, Color color)
    {
        Color[] palette = GetPalette(slot);
        if (palette == null) return -1;

        for (int i = 0; i < palette.Length; i++)
            if (SameColor(palette[i], color)) return i;
        return -1;
    }

    // ---- Save / load as hex ("#RRGGBBAA"), small and safe in JSON ----
    public static string ColorToHex(Color color) => "#" + ColorUtility.ToHtmlStringRGBA(color);

    /// <summary>Parses "#RRGGBB" / "#RRGGBBAA". Returns the fallback if the text is empty or invalid.</summary>
    public static Color HexToColor(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        if (hex[0] != '#') hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
    }

    // Overloads: no fallback argument = the slot's default color.
    public static Color HexToSkinColor(string hex) => HexToColor(hex, DefaultSkinColor);
    public static Color HexToHairColor(string hex) => HexToColor(hex, DefaultHairColor);

    // ---- Apply to a renderer (overloads: without a color = default skin white) ----
    public static void Apply(SpriteRenderer sr, VisualPack pack, FacingDirection dir)
        => Apply(sr, pack, dir, DefaultSkinColor);

    public static void Apply(SpriteRenderer sr, VisualPack pack, FacingDirection dir, Color tint)
    {
        if (sr == null || pack == null || pack.Visual == null) return;

        sr.sprite = pack.Visual.Get(dir, out bool flipX);
        sr.flipX = flipX;
        sr.color = tint;
    }

    private Color[] GetPalette(ColorSlot slot) => slot switch
    {
        ColorSlot.Skin => SkinColors,
        ColorSlot.Hair => HairColors,
        _ => null
    };

    private static Color DefaultOf(ColorSlot slot)
        => slot == ColorSlot.Hair ? DefaultHairColor : DefaultSkinColor;

    private static bool SameColor(Color a, Color b)
    {
        const float tolerance = 0.002f;   // ~half of 1/255, survives hex round-trips
        return Mathf.Abs(a.r - b.r) < tolerance
            && Mathf.Abs(a.g - b.g) < tolerance
            && Mathf.Abs(a.b - b.b) < tolerance
            && Mathf.Abs(a.a - b.a) < tolerance;
    }
    #endregion

    #region Find by ID (use for save/load, ignores gender)
    public bool TryGetByID(VisualSlot slot, int id, out VisualPack pack)
    {
        EnsureCache();
        return idCache[(int)slot].TryGetValue(id, out pack);
    }

    /// <summary>Returns the pack with this ID, or null if it no longer exists.</summary>
    public VisualPack GetByID(VisualSlot slot, int id)
        => TryGetByID(slot, id, out var pack) ? pack : null;

    /// <summary>
    /// For loading saves. If the ID was deleted in an update, falls back to the
    /// first pack valid for the gender, so the character never ends up invisible.
    /// </summary>
    public VisualPack GetByIDOrDefault(VisualSlot slot, int id, ValidGender gender = ValidGender.Unisex)
    {
        if (TryGetByID(slot, id, out var pack)) return pack;

        Debug.LogWarning($"[VisualDataBase] {slot} ID {id} not found, using fallback.", this);
        return Pick(GetList(slot), 0, gender);
    }

    public VisualPack GetBodyByID(int id) => GetByID(VisualSlot.Body, id);
    public VisualPack GetHeadByID(int id) => GetByID(VisualSlot.Head, id);
    public VisualPack GetHairByID(int id) => GetByID(VisualSlot.Hair, id);
    #endregion

    #region Counts and index lookup (for character creator UI)
    public int Count(VisualSlot slot, ValidGender gender = ValidGender.Unisex)
        => Count(GetList(slot), gender);

    public int BodyCount(ValidGender gender = ValidGender.Unisex) => Count(VisualSlot.Body, gender);
    public int HeadCount(ValidGender gender = ValidGender.Unisex) => Count(VisualSlot.Head, gender);
    public int HairCount(ValidGender gender = ValidGender.Unisex) => Count(VisualSlot.Hair, gender);

    /// <summary>
    /// Position of an ID inside the gender-filtered list, or -1 if it is missing
    /// or filtered out. Use it to resume the creator's index from a saved ID.
    /// </summary>
    public int IndexOfID(VisualSlot slot, int id, ValidGender gender = ValidGender.Unisex)
    {
        VisualPack[] list = GetList(slot);
        if (list == null) return -1;

        int position = 0;
        for (int i = 0; i < list.Length; i++)
        {
            if (!Matches(list[i], gender)) continue;
            if (list[i].ID == id) return position;
            position++;
        }
        return -1;
    }
    #endregion

    #region Internals
    private VisualPack[] GetList(VisualSlot slot) => slot switch
    {
        VisualSlot.Body => Body,
        VisualSlot.Head => Head,
        VisualSlot.Hair => Hair,
        _ => null
    };

    private void EnsureCache()
    {
        if (idCache != null) return;

        idCache = new Dictionary<int, VisualPack>[3];
        for (int s = 0; s < idCache.Length; s++)
        {
            var dict = new Dictionary<int, VisualPack>();
            VisualPack[] list = GetList((VisualSlot)s);
            if (list != null)
            {
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] == null) continue;
                    // First entry wins on duplicate IDs (OnValidate warns about them).
                    if (!dict.ContainsKey(list[i].ID)) dict.Add(list[i].ID, list[i]);
                }
            }
            idCache[s] = dict;
        }
    }

    private static int Wrap(int index, int length) => ((index % length) + length) % length;

    /// <summary>
    /// Unisex argument = no filter. A specific gender matches that gender plus Unisex packs.
    /// </summary>
    private static bool Matches(VisualPack pack, ValidGender wanted)
    {
        if (pack == null) return false;
        return wanted == ValidGender.Unisex
            || pack.Gender == ValidGender.Unisex
            || pack.Gender == wanted;
    }

    private static int Count(VisualPack[] list, ValidGender gender)
    {
        if (list == null) return 0;
        int count = 0;
        for (int i = 0; i < list.Length; i++)
            if (Matches(list[i], gender)) count++;
        return count;
    }

    /// <summary>Returns the nth matching pack, wrapping around. No allocations.</summary>
    private static VisualPack Pick(VisualPack[] list, int index, ValidGender gender)
    {
        int count = Count(list, gender);
        if (count == 0) return null;

        int target = Wrap(index, count);   // safe for negative index

        for (int i = 0; i < list.Length; i++)
        {
            if (!Matches(list[i], gender)) continue;
            if (target-- == 0) return list[i];
        }
        return null;
    }
    #endregion

    #region Editor validation
#if UNITY_EDITOR
    private void OnValidate()
    {
        idCache = null;   // asset edited, so drop the cache
        Validate(Body, nameof(Body));
        Validate(Head, nameof(Head));
        Validate(Hair, nameof(Hair));

        if (SkinColors == null || SkinColors.Length == 0)
            Debug.LogWarning("[VisualDataBase] SkinColors is empty, the default will be used.", this);
        if (HairColors == null || HairColors.Length == 0)
            Debug.LogWarning("[VisualDataBase] HairColors is empty, the default will be used.", this);
    }

    private void Validate(VisualPack[] list, string label)
    {
        if (list == null) return;

        var seen = new HashSet<int>();
        for (int i = 0; i < list.Length; i++)
        {
            VisualPack p = list[i];
            if (p == null)
            {
                Debug.LogWarning($"[VisualDataBase] {label}[{i}] is null.", this);
                continue;
            }
            if (!seen.Add(p.ID))
                Debug.LogWarning($"[VisualDataBase] {label}[{i}] has duplicate ID {p.ID}.", this);
            if (p.Visual == null || (p.Visual.Front == null && p.Visual.Right == null && p.Visual.Back == null))
                Debug.LogWarning($"[VisualDataBase] {label}[{i}] (ID {p.ID}) has no sprites.", this);
        }
    }
#endif
    #endregion
}

[Serializable]
public class VisualPack
{
    public int ID;
    public ValidGender Gender;
    public SpritePack Visual = new SpritePack();
}

[Serializable]
public class SpritePack
{
    public Sprite Front;
    public Sprite Right;
    public Sprite Back;
    // Left is a flipped Right.

    /// <summary>Sprite for a facing direction. flipX is true for Left.</summary>
    public Sprite Get(FacingDirection dir, out bool flipX)
    {
        flipX = dir == FacingDirection.Left;
        return dir switch
        {
            FacingDirection.Front => Front,
            FacingDirection.Back => Back,
            _ => Right,   // Right and Left share the Right sprite
        };
    }
}

public enum VisualSlot { Body, Head, Hair }

public enum ColorSlot { Skin, Hair }

public enum FacingDirection { Front, Right, Back, Left }

public enum ValidGender
{
    Male,
    Female,
    Unisex
}