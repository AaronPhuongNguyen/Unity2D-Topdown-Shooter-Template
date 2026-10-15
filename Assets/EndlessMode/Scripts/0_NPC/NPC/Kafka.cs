using System.Collections;
using System.Collections.Generic;
using Server;
using UnityEngine;

/// <summary>
/// Kafka, the first NPC. Inherits everything from NpcBrain (following,
/// fighting, fleeing, hit stagger, stat matching) and adds:
///   - a voice: greeting, one random line per new wave (boss pool optional),
///   - SKILL: throws a grenade at her target when it is closer than grenadeRange.
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

    [Header("Skill: Grenade")]
    [SerializeField] private bool useGrenade = true;
    [SerializeField] private GameObject grenadePrefab;
    [Tooltip("Throws only when the target is closer than this.")]
    [SerializeField] private float grenadeRange = 20f;
    [SerializeField] private float grenadeCooldown = 10f;
    [Tooltip("Grenade damage = her ATK x this.")]
    [SerializeField] private float grenadeDamageMultiplier = 3f;
    [SerializeField] private float grenadeRadius = 5f;
    [Tooltip("Damage multiplier at the very edge of the blast (1 = no falloff).")]
    [SerializeField, Range(0f, 1f)] private float grenadeEdgeDamage = 0.4f;
    [Tooltip("Seconds the grenade stays in the air.")]
    [SerializeField] private float grenadeFlightTime = 0.6f;
    [Tooltip("Where it leaves her hand. Falls back to her position.")]
    [SerializeField] private Transform throwPoint;
    [SerializeField] private AudioClip grenadeThrowLine;

    private AudioClip lastClip;
    private float grenadeTimer;

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
        grenadeTimer = 0f; // ready to throw
        Speak(spawnLine);
    }

    protected override void OnRemoved()
    {
        StopAllCoroutines();
        base.OnRemoved();
    }

    protected override void OnNewWave(int wave)
    {
        base.OnNewWave(wave);

        if (!IsActive) return;
        if (speakChance < 1f && RNG.GetPercent() > speakChance) return;

        bool isBossWave = BossManager.instance != null && BossManager.instance.IsBossWaveNumber(wave);
        List<AudioClip> pool = isBossWave && HasAny(bossWaveLines) ? bossWaveLines : waveLines;

        AudioClip clip = Pick(pool);
        if (clip == null) return;

        StartCoroutine(SpeakAfterDelay(clip));
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        TickGrenade(dt);
    }
    #endregion

    #region Skill: Grenade
    private void TickGrenade(float dt)
    {
        if (!useGrenade || TickSystem.GameSpeed <= 0f) return;
        if (!IsActive || attribute == null || attribute.IsDead) return;

        if (grenadeTimer > 0f)
        {
            grenadeTimer -= dt;
            return;
        }

        if (Target == null || !Target.gameObject.activeInHierarchy) return;

        Vector2 from = throwPoint != null ? (Vector2)throwPoint.position : (Vector2)transform.position;
        Vector2 to = Target.position;
        if ((to - from).sqrMagnitude >= grenadeRange * grenadeRange) return;

        ThrowGrenade(from, to);
    }

    private void ThrowGrenade(Vector2 from, Vector2 to)
    {
        Grenade g = Grenade.Throw(
            grenadePrefab, from, to, attribute,
            attribute.ATK_Current * grenadeDamageMultiplier,
            grenadeRadius, grenadeEdgeDamage,
            pm.EnemyMask, grenadeFlightTime);

        if (g == null) return; // no prefab / pool problem: don't burn the cooldown

        grenadeTimer = grenadeCooldown;
        Speak(grenadeThrowLine);
    }

    [ContextMenu("Test Throw Grenade")]
    private void Test_Grenade() => grenadeTimer = 0f;
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

        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) return list[i];
        return null;
    }

    [ContextMenu("Test Speak")]
    private void Test_Speak() => Speak(Pick(waveLines));
    #endregion
}