using UnityEngine;

[CreateAssetMenu(menuName ="Package/Media")]
public class MediaPackage : ScriptableObject
{
    public SpritePackage Corpse;
    public SpritePackage Blood;
    public AudioPackage Audio;
}