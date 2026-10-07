using System;
using System.Collections.Generic;


[Serializable]
public class SaveData
{
    public int SaveVersion = SaveSystem.CurrentVersion;

    public WorldScript WorldScript;
}