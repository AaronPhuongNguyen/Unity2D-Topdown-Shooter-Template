#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VisualDatabaseBuilder : EditorWindow
{
    private VisualDataBase db;
    private string root = "Assets/SurvivalMode/Sprites";   // contains Body / Head / Hair
    private int startID = 1;                               // first ID given to a brand-new database
    private const int SpritesPerPack = 3;                  // Right, Back, Front

    [MenuItem("Survival/Visual Database Builder")]
    private static void Open() => GetWindow<VisualDatabaseBuilder>("Visual DB Builder");

    private void OnEnable()
    {
        if (db != null) return;
        string[] guids = AssetDatabase.FindAssets("t:VisualDataBase");
        if (guids.Length > 0)
            db = AssetDatabase.LoadAssetAtPath<VisualDataBase>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Sprites in each folder are sorted by name (natural order: 2 before 10).\n" +
            "Every 3 sprites = 1 pack: Right, Back, Front.\n" +
            "Existing packs keep their ID and Gender (matched by the Right sprite). New packs get a new ID and Unisex.",
            MessageType.Info);

        db = (VisualDataBase)EditorGUILayout.ObjectField("Database", db, typeof(VisualDataBase), false);
        root = EditorGUILayout.TextField("Sprites Root", root);
        startID = EditorGUILayout.IntField("First ID (new packs)", startID);

        EditorGUI.BeginDisabledGroup(db == null);
        if (GUILayout.Button("Build ALL (Body + Head + Hair)", GUILayout.Height(30)))
        {
            Build(VisualSlot.Body);
            Build(VisualSlot.Head);
            Build(VisualSlot.Hair);
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Body")) Build(VisualSlot.Body);
        if (GUILayout.Button("Head")) Build(VisualSlot.Head);
        if (GUILayout.Button("Hair")) Build(VisualSlot.Hair);
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private void Build(VisualSlot slot)
    {
        string folder = $"{root}/{slot}";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError($"[VisualDB] Folder not found: {folder}");
            return;
        }

        List<Sprite> sprites = CollectSprites(folder);
        if (sprites.Count == 0)
        {
            Debug.LogWarning($"[VisualDB] No sprites in {folder}. {slot} was left unchanged.");
            return;
        }

        int packCount = sprites.Count / SpritesPerPack;
        int leftover = sprites.Count % SpritesPerPack;
        if (leftover != 0)
            Debug.LogWarning($"[VisualDB] {slot}: {sprites.Count} sprites is not a multiple of 3. " +
                             $"The last {leftover} sprite(s) were ignored.");

        VisualPack[] current = GetArray(slot) ?? new VisualPack[0];

        // Existing packs, matched by their Right sprite so IDs and Gender survive a rebuild.
        var byRight = new Dictionary<Sprite, VisualPack>();
        var usedIDs = new HashSet<int>();
        foreach (VisualPack p in current)
        {
            if (p == null) continue;
            usedIDs.Add(p.ID);
            if (p.Visual != null && p.Visual.Right != null && !byRight.ContainsKey(p.Visual.Right))
                byRight.Add(p.Visual.Right, p);
        }

        int nextID = usedIDs.Count == 0 ? startID : usedIDs.Max() + 1;
        var result = new List<VisualPack>(packCount);
        int added = 0, kept = 0;

        for (int i = 0; i < packCount; i++)
        {
            Sprite right = sprites[i * 3];
            Sprite back = sprites[i * 3 + 1];
            Sprite front = sprites[i * 3 + 2];

            if (byRight.TryGetValue(right, out VisualPack pack))
            {
                kept++;
            }
            else
            {
                pack = new VisualPack { ID = nextID++, Gender = ValidGender.Unisex };
                added++;
            }

            pack.Visual = new SpritePack { Right = right, Back = back, Front = front };
            result.Add(pack);
        }

        int removed = current.Count(p => p != null) - kept;

        Undo.RecordObject(db, $"Build {slot} visuals");
        SetArray(slot, result.ToArray());
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();

        Debug.Log($"[VisualDB] {slot}: {packCount} packs ({kept} kept, {added} new, {removed} removed).", db);
    }

    // ---- sprite collection -------------------------------------------------
    // Works for separate image files and for sliced sprite sheets.
    private static List<Sprite> CollectSprites(string folder)
    {
        var result = new List<Sprite>();

        List<string> paths = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .ToList();
        paths.Sort(NaturalCompare);

        foreach (string path in paths)
        {
            List<Sprite> inFile = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();
            inFile.Sort((a, b) => NaturalCompare(a.name, b.name));
            result.AddRange(inFile);
        }
        return result;
    }

    // "Hair2" < "Hair10"
    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                string na = a.Substring(si, i - si).TrimStart('0');
                string nb = b.Substring(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                int c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    // ---- database access ----------------------------------------------------
    private VisualPack[] GetArray(VisualSlot slot) => slot switch
    {
        VisualSlot.Body => db.Body,
        VisualSlot.Head => db.Head,
        _ => db.Hair
    };

    private void SetArray(VisualSlot slot, VisualPack[] value)
    {
        switch (slot)
        {
            case VisualSlot.Body: db.Body = value; break;
            case VisualSlot.Head: db.Head = value; break;
            default: db.Hair = value; break;
        }
    }
}
#endif