#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CardDataBase))]
public class CardDataBaseEditor : Editor
{
    private const string CardsFolder = "Assets/EndlessMode/Gacha/Cards";

    private static bool clearFirst = true;

    public override void OnInspectorGUI()
    {
        // Tool first, so it's visible even with a long card list.
        EditorGUILayout.LabelField("Import Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox($"Imports every CardEffect found in:\n{CardsFolder}", MessageType.Info);

        clearFirst = EditorGUILayout.ToggleLeft("Remove all current cards first", clearFirst);

        if (GUILayout.Button("Reimport All Cards", GUILayout.Height(28)))
            Reimport((CardDataBase)target, clearFirst);

        EditorGUILayout.Space(10);
        DrawDefaultInspector();
    }

    // Right-click the component header / asset in the Inspector.
    [MenuItem("CONTEXT/CardDataBase/Reimport All Cards (Clear First)")]
    private static void CtxReimport(MenuCommand cmd) => Reimport((CardDataBase)cmd.context, true);

    // Works without selecting anything: finds the first CardDataBase in the project.
    [MenuItem("Tools/Cards/Reimport All Cards (Clear First)")]
    private static void MenuReimport()
    {
        string[] guids = AssetDatabase.FindAssets("t:CardDataBase");
        if (guids.Length == 0)
        {
            Debug.LogError("[CardDataBase] No CardDataBase asset found in the project.");
            return;
        }
        if (guids.Length > 1)
            Debug.LogWarning($"[CardDataBase] {guids.Length} databases found, using the first one.");

        var db = AssetDatabase.LoadAssetAtPath<CardDataBase>(AssetDatabase.GUIDToAssetPath(guids[0]));
        Reimport(db, true);
    }

    public static void Reimport(CardDataBase db, bool clear)
    {
        if (!AssetDatabase.IsValidFolder(CardsFolder))
        {
            Debug.LogError($"[CardDataBase] Folder not found: {CardsFolder}");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:CardEffect", new[] { CardsFolder });
        var found = new List<CardEffect>(guids.Length);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var card = AssetDatabase.LoadAssetAtPath<CardEffect>(path);
            if (card != null) found.Add(card);
        }

        found.Sort((a, b) => string.CompareOrdinal(
            AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));

        Undo.RecordObject(db, "Reimport Cards");

        int before = db.cards.Count;
        if (clear) db.cards.Clear();
        else db.cards.RemoveAll(c => c == null);

        int added = 0;
        foreach (CardEffect c in found)
        {
            if (db.cards.Contains(c)) continue;
            db.cards.Add(c);
            added++;
        }

        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CardDataBase] {(clear ? "Cleared" : "Kept")} {before} old entries, added {added}. Total: {db.cards.Count}.");
    }
}
#endif