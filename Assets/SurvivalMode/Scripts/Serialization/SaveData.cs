using System;


[Serializable]
public class SaveData
{
    public int SaveVersion = SaveSystem.CurrentVersion;

    public WorldScript WorldScript;
    public PlayerData PlayerData;
}

[Serializable]
public class PlayerData
{
    public string Name;
    public ValidGender Gender;
    public int BodyID, HeadID, HairID;
    public ColorBodyPart BodyColor, HeadColor, HairColor;

    [Serializable]
    public class ColorBodyPart
    {
        public float Red;
        public float Green;
        public float Blue;
        public float Alpha;
    }
}