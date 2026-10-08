using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Your save payload. Add new fields here and bump SaveSystem.CurrentVersion.
/// Old saves missing the new keys are patched automatically on load.
/// </summary>


public static class SaveSystem
{
    public const int CurrentVersion = 1;

    // ---- Custom encoding -------------------------------------------------
    // UTF-8 (no BOM) + XOR obfuscation. This stops casual editing only;
    // it is NOT real encryption.
    private static readonly Encoding TextEncoding = new UTF8Encoding(false);
    private static readonly byte[] XorKey = Encoding.ASCII.GetBytes("ChangeThisKey");

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.None,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Include,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    private static string GetPath(string slot) =>
        Path.Combine(Application.persistentDataPath, slot + ".sav");

    // ---- Public API ------------------------------------------------------
    public static bool Exists(string slot = "save") => File.Exists(GetPath(slot));

    public static void Save(SaveData data, string slot = "save")
    {
        try
        {
            data.SaveVersion = CurrentVersion;
            string json = JsonConvert.SerializeObject(data, JsonSettings);
            WriteAtomic(GetPath(slot), Encode(json));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Save failed: {e}");
        }
    }

    public static SaveData Load(string slot = "save")
    {
        string path = GetPath(slot);
        if (!File.Exists(path)) return new SaveData();

        try
        {
            string json = Decode(File.ReadAllBytes(path));
            JObject root = JObject.Parse(json);

            int version = root["SaveVersion"]?.Value<int>() ?? 0;

            if (version > CurrentVersion)
                Debug.LogWarning($"[SaveSystem] Save v{version} is newer than game v{CurrentVersion}. Loading anyway.");

            if (version < CurrentVersion)
            {
                // Legacy save: add any missing keys (numbers default to 0).
                JObject template = JObject.FromObject(new SaveData());
                MergeMissingKeys(root, template);
                root["SaveVersion"] = CurrentVersion;
                Debug.Log($"[SaveSystem] Migrated save v{version} -> v{CurrentVersion}");
            }

            SaveData data = root.ToObject<SaveData>(JsonSerializer.Create(JsonSettings)) ?? new SaveData();

            // Persist the migrated file so migration only runs once.
            if (version < CurrentVersion) Save(data, slot);

            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Load failed, returning fresh data: {e}");
            return new SaveData();
        }
    }

    public static void Delete(string slot = "save")
    {
        string path = GetPath(slot);
        if (File.Exists(path)) File.Delete(path);
    }

    // ---- Version migration -----------------------------------------------
    /// <summary>
    /// Recursively adds keys that exist in the template but not in the target.
    /// Numeric keys are initialised to 0; other types keep their default
    /// (false / "" / empty collection) so deserialization never throws.
    /// </summary>
    private static void MergeMissingKeys(JObject target, JObject template)
    {
        foreach (var prop in template.Properties())
        {
            JToken existing = target[prop.Name];

            if (existing == null || existing.Type == JTokenType.Null)
            {
                target[prop.Name] = prop.Value.Type switch
                {
                    JTokenType.Integer => new JValue(0),
                    JTokenType.Float => new JValue(0),
                    _ => prop.Value.DeepClone()
                };
            }
            else if (existing is JObject existingObj && prop.Value is JObject templateObj)
            {
                MergeMissingKeys(existingObj, templateObj);
            }
        }
    }

    // ---- Encoding helpers ------------------------------------------------
    private static byte[] Encode(string text)
    {
        byte[] bytes = TextEncoding.GetBytes(text);
        Xor(bytes);
        return bytes;
    }

    private static string Decode(byte[] bytes)
    {
        byte[] copy = (byte[])bytes.Clone();
        Xor(copy);
        return TextEncoding.GetString(copy);
    }

    private static void Xor(byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
            data[i] ^= XorKey[i % XorKey.Length];
    }

    // Write to a temp file first so a crash mid-write can't corrupt the save.
    private static void WriteAtomic(string path, byte[] bytes)
    {
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}