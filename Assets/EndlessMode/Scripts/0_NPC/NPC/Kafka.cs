using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Server;

/// <summary>
/// Kafka, the first NPC. Put this next to NpcBrain on her prefab.
/// NpcBrain handles following, fighting, fleeing and the wave bonus;
/// this component only gives her a voice:
///   - a greeting when she appears (optional),
///   - one random line on every new wave (never the same line twice in a row),
///   - an optional separate pool of lines for boss waves.
/// She stays silent while she is not active (dead / game restarted).
/// </summary>
[RequireComponent(typeof(NpcBrain))]
public class Kafka : MonoBehaviour
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

    private NpcBrain npc;
    private AudioClip lastClip;

    private void Awake()
    {
        npc = GetComponent<NpcBrain>();
    }

    private void OnEnable()
    {
        EventBus.OnNewWave += OnNewWave;
        // NpcBrain.Init() and the spawn position are set right after the object
        // is enabled by the pool, so greet one frame later.
        StartCoroutine(GreetNextFrame());
    }

    private void OnDisable()
    {
        EventBus.OnNewWave -= OnNewWave;
        StopAllCoroutines();
    }

    private IEnumerator GreetNextFrame()
    {
        yield return null;
        if (npc != null && npc.IsActive) Speak(spawnLine);
    }

    private void OnNewWave(int wave)
    {
        if (npc == null || !npc.IsActive) return;
        if (speakChance < 1f && RNG.GetPercent() > speakChance) return;

        bool isBossWave = BossManager.instance != null && BossManager.instance.IsBossWaveNumber(wave);
        List<AudioClip> pool = isBossWave && HasAny(bossWaveLines) ? bossWaveLines : waveLines;

        AudioClip clip = Pick(pool);
        if (clip == null) return;

        StartCoroutine(SpeakAfterDelay(clip));
    }

    private IEnumerator SpeakAfterDelay(AudioClip clip)
    {
        if (speakDelay > 0f) yield return new WaitForSeconds(speakDelay);
        if (npc != null && npc.IsActive) Speak(clip);
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
}