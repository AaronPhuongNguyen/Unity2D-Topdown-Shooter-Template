using System.Collections;
using System.Collections.Generic;
using Server;
using UnityEngine;

/// <summary>
/// Kafka, the first NPC. Inherits everything from NpcBrain (following,
/// fighting, fleeing, hit stagger, wave bonus) and adds a voice:
///   - a greeting when she appears (optional),
///   - one random line on every new wave (never the same line twice in a row),
///   - an optional separate pool of lines for boss waves.
/// She stays silent while she is not active (dead / game restarted).
///
/// Put this on her prefab INSTEAD of NpcBrain (it is an NpcBrain).
/// </summary>
public class Kafka : NpcBrain
{
    [Header("Voice")]
    [Tooltip("Played once when she joins the fight (optional).")]
    [SerializeField] private AudioClip spawnLine;

    [Tooltip("One random line is spoken on every new wave.")]
    [SerializeField] private List<AudioClip> waveLines = new List<AudioClip>();

    [Tooltip("Optional: used instead of waveLines on boss waves.")]
    [SerializeField] private List<AudioClip> bossWaveLines = new List<AudioClip>();

    [Tooltip("Chance to speak on a new wave. 1 = every wave.")]
    [SerializeField, Range(0f, 1f)] private float speakChance = 1f;

    [Tooltip("Small delay so the line doesn't clash with the new-wave sound.")]
    [SerializeField] private float speakDelay = 0.4f;

    private AudioClip lastClip;

    #region NpcBrain hooks
    protected override void OnDisable()
    {
        StopAllCoroutines();
        base.OnDisable();
    }

    /// <summary>Init() has finished and the position is already set: greet right away.</summary>
    protected override void OnSpawned()
    {
        base.OnSpawned();
        lastClip = null;
        Speak(spawnLine);
    }

    protected override void OnRemoved()
    {
        StopAllCoroutines();
        base.OnRemoved();
    }

    protected override void OnNewWave(int wave)
    {
        base.OnNewWave(wave); // keep the wave bonus refresh

        if (!IsActive) return;
        if (speakChance < 1f && RNG.GetPercent() > speakChance) return;

        bool isBossWave = BossManager.instance != null && BossManager.instance.IsBossWaveNumber(wave);
        List<AudioClip> pool = isBossWave && HasAny(bossWaveLines) ? bossWaveLines : waveLines;

        AudioClip clip = Pick(pool);
        if (clip == null) return;

        StartCoroutine(SpeakAfterDelay(clip));
    }
    #endregion

    #region Voice
    private IEnumerator SpeakAfterDelay(AudioClip clip)
    {
        if (speakDelay > 0f) yield return new WaitForSeconds(speakDelay);
        if (IsActive) Speak(clip);
    }

    private void Speak(AudioClip clip)
    {
        if (clip == null || AudioManager.instance == null) return;
        lastClip = clip;
        AudioManager.instance.PlayAudio(clip, transform.position);
    }

    private static bool HasAny(List<AudioClip> list)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) return true;
        return false;
    }

    /// <summary>Random non-null clip, avoiding an immediate repeat when there is a choice.</summary>
    private AudioClip Pick(List<AudioClip> list)
    {
        if (list == null || list.Count == 0) return null;

        for (int i = 0; i < 8; i++)
        {
            AudioClip c = list[RNG.GetInt(0, list.Count)];
            if (c == null) continue;
            if (c == lastClip && list.Count > 1) continue;
            return c;
        }

        // Unlucky rolls (or mostly empty slots): fall back to the first usable clip.
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) return list[i];
        return null;
    }

    [ContextMenu("Test Speak")]
    private void Test_Speak() => Speak(Pick(waveLines));
    #endregion
}