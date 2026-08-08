using UnityEngine;
public class SoundPlayer : MonoBehaviour
{
    [Header("Sound")]
    [SerializeField] private AudioClip clip;
    public void Play()
    {
        if (clip == null || AudioManager.instance == null) return;
        AudioManager.instance.PlayAudio(clip);
    }
    public void PlayClip(AudioClip overrideClip)
    {
        if (overrideClip == null || AudioManager.instance == null) return;
        AudioManager.instance.PlayAudio(overrideClip);
    }
}