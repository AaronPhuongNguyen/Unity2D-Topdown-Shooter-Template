using Server;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Endless/Package/Audio")]
public class AudioPackage : ScriptableObject
{
    public List<AudioClip> AttackSounds;
    public List<AudioClip> HitTargetSounds;
    public List<AudioClip> DeathSounds;
    public List<AudioClip> MiscSounds;

    public AudioClip GetAttackSound() => GetRandom(AttackSounds);
    public AudioClip GetHitSound() => GetRandom(HitTargetSounds);
    public AudioClip GetDeathSound() => GetRandom(DeathSounds);
    public AudioClip GetMiscSound() => GetRandom(MiscSounds);

    private AudioClip GetRandom(List<AudioClip> clips) =>
        clips == null || clips.Count == 0 ? null : clips[RNG.GetInt(0, clips.Count)];
}