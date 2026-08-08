using Server;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Package/Audio")]
public class AudioPackage : ScriptableObject
{
    public List<AudioClip> AttackSounds;
    public List<AudioClip> HitSounds;
    public List<AudioClip> DeathSounds;
    public List<AudioClip> MiscSounds;

    public AudioClip GetAttackSound() => GetRandom(AttackSounds);
    public AudioClip GetHitSound() => GetRandom(HitSounds);
    public AudioClip GetDeathSound() => GetRandom(DeathSounds);
    public AudioClip GetMiscSound() => GetRandom(MiscSounds);

    private AudioClip GetRandom(List<AudioClip> clips) =>
        clips == null || clips.Count == 0 ? null : clips[RNG.GetInt(0, clips.Count)];
}