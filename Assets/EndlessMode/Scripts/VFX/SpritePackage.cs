using Server;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Package/Sprites")]
public class SpritePackage : ScriptableObject
{
    public List<Sprite> sprites;

    public Sprite GetRandomSprite()
    {
        if (sprites == null || sprites.Count == 0) return null;
        return sprites[RNG.GetInt(0, sprites.Count)];
    }
}