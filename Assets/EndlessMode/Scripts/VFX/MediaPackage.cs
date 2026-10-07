using UnityEngine;

[CreateAssetMenu(menuName ="Endless/Package/Media")]
public class MediaPackage : ScriptableObject
{
    public SpritePackage Corpse;
    public SpritePackage Blood;
    public AudioPackage Audio;
    public GameObject HitEffect;
}