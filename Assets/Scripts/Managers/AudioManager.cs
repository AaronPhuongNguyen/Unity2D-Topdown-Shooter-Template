using Server;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance { get; private set; }

    #region SFX
    [Header("SFX Speaker")]
    [SerializeField] private AudioSource speaker;
    [SerializeField] private AudioMixerGroup mixer;
    [Range(0, 1)] public float volume = 1;
    #endregion

    #region Music
    [Header("Music Player")]
    [SerializeField] private AudioSource musicSpeaker;
    [SerializeField] private AudioMixerGroup musicMixer;
    [SerializeField] private List<AudioClip> musicList = new List<AudioClip>();
    [SerializeField] private bool shuffle = true;
    [SerializeField] private bool autoPlayMusicOnStart = true;
    [Range(0, 1)] public float musicVolume = 0.5f;

    private int currentMusicIndex = -1;
    #endregion

    private float checkInterval;

    #region Singleton
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        SetupMusicSpeaker();
    }
    #endregion

    private void Start()
    {
        if (speaker == null) speaker = gameObject.AddComponent<AudioSource>();
        if (mixer != null) speaker.outputAudioMixerGroup = mixer;

        EnableSpeaker(true);
        SetLoop(false);
        SetPlayOnAwake(false);
        SetPriority(16);

        SyncAttribute();

        if (autoPlayMusicOnStart && musicList.Count > 0) PlayNextMusic();
    }

    private void Update()
    {
        SyncAttribute();

        // Auto-advance to next track once current one finishes
        if (musicSpeaker != null && !musicSpeaker.isPlaying && currentMusicIndex >= 0 && musicList.Count > 0)
        {
            PlayNextMusic();
        }
    }

    #region Setup
    /// <summary>Creates a dedicated child GameObject + AudioSource for music, separate from the SFX speaker.</summary>
    private void SetupMusicSpeaker()
    {
        if (musicSpeaker != null) return;

        GameObject musicObj = new GameObject("Music Player");
        musicObj.transform.SetParent(transform);
        musicObj.transform.localPosition = Vector3.zero;

        musicSpeaker = musicObj.AddComponent<AudioSource>();
        if (musicMixer != null) musicSpeaker.outputAudioMixerGroup = musicMixer;

        musicSpeaker.loop = false; // handled manually so we can advance the playlist
        musicSpeaker.playOnAwake = false;
        musicSpeaker.priority = 0; // lower priority than SFX by default
    }
    #endregion

    #region SFX Functions
    public void PlayAudio(AudioClip clip, Vector3 position=default)
    {
        if (clip == null) return;
        if (speaker == null) return;

        if (PlayerManager.instance != null && PlayerManager.instance.Controlling != null)
        {
            float hearRange = PlayerManager.instance.attribute.SIGHT_Current;
            float dist = Vector2.Distance(position, PlayerManager.instance.Controlling.transform.position);
            if (dist > hearRange) return;
        }

        speaker.PlayOneShot(clip);
    }

    public void EnableSpeaker(bool v) => speaker.enabled = v;
    public void SetLoop(bool v) => speaker.loop = v;
    public void SetPlayOnAwake(bool v) => speaker.playOnAwake = v;
    public void SetPriority(int v) => speaker.priority = v;
    public void SetVolume(float v) => speaker.volume = v;
    #endregion

    #region Music Functions
    public void PlayMusic(int index)
    {
        if (musicList.Count == 0) return;
        if (index < 0 || index >= musicList.Count) return;

        currentMusicIndex = index;
        musicSpeaker.clip = musicList[currentMusicIndex];
        musicSpeaker.Play();
    }

    public void PlayNextMusic()
    {
        if (musicList.Count == 0) return;

        int nextIndex = shuffle
            ? RNG.GetInt(0, musicList.Count)
            : (currentMusicIndex + 1) % musicList.Count;

        PlayMusic(nextIndex);
    }

    public void StopMusic()
    {
        if (musicSpeaker == null) return;
        musicSpeaker.Stop();
        currentMusicIndex = -1;
    }

    public void PauseMusic() => musicSpeaker?.Pause();
    public void ResumeMusic() => musicSpeaker?.UnPause();
    public void SetMusicVolume(float v) => musicVolume = Mathf.Clamp01(v);
    #endregion

    #region Misc Features
    private void SyncAttribute()
    {
        if (Time.time < checkInterval) return;
        checkInterval = Time.time + 0.5f;

        if (speaker.volume != volume) speaker.volume = volume;
        if (musicSpeaker != null && musicSpeaker.volume != musicVolume) musicSpeaker.volume = musicVolume;
    }
    #endregion
}