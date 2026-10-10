using System;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(99)]
public class AudioAdjuster : MonoBehaviour
{
    [SerializeField] AudioSettings settings;
    [SerializeField] Slider SFX_Slider;
    [SerializeField] Slider Music_Slider;

    bool isSFXPerformed, isMusicPerformed;

    AudioManager am => AudioManager.instance;

    private void Start()
    {
        if(am != null)
        {
            am.SetVolume(settings.SFX);
            am.SetMusicVolume(settings.Music);
        }
        SyncAudio();
    }
    void SyncAudio()
    {
        if (SFX_Slider != null && !isSFXPerformed)
        {
            SFX_Slider.minValue = settings.MinValue;
            SFX_Slider.maxValue = settings.MaxValue;
            isSFXPerformed = true;
            if (am != null) am.SetVolume(SFX_Slider.value);
        }
        if (Music_Slider != null && !isMusicPerformed)
        {
            Music_Slider.minValue = settings.MinValue;
            Music_Slider.maxValue = settings.MaxValue;
            isMusicPerformed = true;
            if (am != null) am.SetMusicVolume(Music_Slider.value);
        }
    }

    public void SFX_Update()
    {
        if (SFX_Slider != null)
        {
            isSFXPerformed = false;
            SyncAudio();
        }
    }
    public void Music_Update()
    {
        if(Music_Slider != null)
        {
            isMusicPerformed = false;
            SyncAudio();
        }
    }
}

[Serializable]
public class AudioSettings
{
    public float SFX = 1;
    public float Music = 1;

    public float MaxValue = 1f;
    public float MinValue = 0f;
}