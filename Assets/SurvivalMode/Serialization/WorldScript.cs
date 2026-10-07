using System;
using UnityEngine;

[Serializable]
public class WorldScript
{
    public string WorldName;
    public uint Seed;
    public Vector2 WorldSize = new Vector2(300,300);
    public SurvivalDifficulty Difficulty;
    public bool KeepInventory;
    public bool IsNew = true;
}

public enum SurvivalDifficulty
{
    Easy = 1,
    Medium = 2,
    Hardcode = 3
}