using System;
using System.Collections;
using System.Collections.Generic;
using Server;
using UnityEngine;

/// <summary>
/// Kafka, the first NPC. Inherits everything from NpcBrain (following,
/// fighting, fleeing, hit stagger, stat matching, magazine) and adds:
///   - a voice: greeting, one random line per new wave (boss pool optional),
///   - SKILL: throws grenades at her target when it is closer than grenadeRange.
///     Grenades ACCUMULATE: +1 every grenadeRegenInterval seconds (up to
///     maxGrenades), and there is a short throwCooldown between two throws.
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

    [Header("Skill: Grenade - Stock")]
    [SerializeField] private bool useGrenade = true;
    [Tooltip("Grenades she has when she spawns.")]
    [SerializeField, Min(0)] private int startGrenades = 1;
    [Tooltip("She can never carry more than this.")]
    [SerializeField, Min(1)] private int maxGrenades = 5;
    [Tooltip("Seconds to produce one new grenade.")]
    [SerializeField, Min(0.1f)] private float grenadeRegenInterval = 15f;
    [Tooltip("Seconds between two throws.")]
    [SerializeField, Min(0f)] private float throwCooldown = 0.5f;

    [Header("Skill: Grenade - Throw")]
    [SerializeField] private GameObject grenadePrefab;
    [Tooltip("Throws only when the target is closer than this.")]
    [SerializeField] private float grenadeRange = 20f;
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

    // Grenade stock
    private int grenadeCount;
    private float regenTimer;
    private float throwTimer;

    #region Grenade API
    /// <summary>Fired when the stock changes: (current, max). Cleared when she is removed.</summary>
    public event Action<int, int> OnGrenadesChanged;

    public int GrenadeCount => grenadeCount;
    public int MaxGrenades => maxGrenades;

    /// <summary>Seconds until the next grenade is produced (0 when the stock is full).</summary>
    public float GrenadeRegenRemaining => grenadeCount >= maxGrenades ? 0f : Mathf.Max(0f, grenadeRegenInterval - regenTimer);

    /// <summary>Regen progress 0..1 toward the next grenade. Handy for a UI bar.</summary>
    public float GrenadeRegenProgress => grenadeCount >= maxGrenades ? 1f : Mathf.Clamp01(regenTimer / grenadeRegenInterval);

    /// <summary>Adds grenades (clamped to max). Returns how many were really added.</summary>
    public int AddGrenades(int amount)
    {
        if (amount <= 0) return 0;
        int before = grenadeCount;
        SetGrenades(grenadeCount + amount);
        return grenadeCount - before;
    }

    /// <summary>Changes the stock limit at runtime (e.g. from a card).</summary>
    public void SetMaxGrenades(int newMax)
    {
        maxGrenades = Mathf.Max(1, newMax);
        SetGrenades(Mathf.Min(grenadeCount, maxGrenades));
    }

    /// <summary>Changes the production time at runtime (e.g. from a card).</summary>
    public void SetGrenadeRegenInterval(float seconds) => grenadeRegenInterval = Mathf.Max(0.1f, seconds);

    private void SetGrenades(int value)
    {
        value = Mathf.Clamp(value, 0, maxGrenades);
        if (value == grenadeCount) return;

        grenadeCount = value;
        OnGrenadesChanged?.Invoke(grenadeCount, maxGrenades);
    }
    #endregion

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

        regenTimer = 0f;
        throwTimer = 0f;
        grenadeCount = Mathf.Clamp(startGrenades, 0, maxGrenades);
        OnGrenadesChanged?.Invoke(grenadeCount, maxGrenades);

        Speak(spawnLine);
    }

    protected override void OnRemoved()
    {
        StopAllCoroutines();

        // Pooled object: don't keep old subscribers (UI etc.) between lives.
        OnGrenadesChanged = null;

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

        // Throw cooldown
        if (throwTimer > 0f) throwTimer -= dt;

        // Accumulate: +1 grenade every grenadeRegenInterval seconds.
        if (grenadeCount < maxGrenades)
        {
            regenTimer += dt;
            while (regenTimer >= grenadeRegenInterval && grenadeCount < maxGrenades)
            {
                regenTimer -= grenadeRegenInterval;
                SetGrenades(grenadeCount + 1);
            }
            if (grenadeCount >= maxGrenades) regenTimer = 0f;
        }
        else
        {
            regenTimer = 0f;
        }

        // Throw
        if (grenadeCount <= 0 || throwTimer > 0f) return;
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

        if (g == null) return; // no prefab / pool problem: don't spend a grenade

        SetGrenades(grenadeCount - 1);
        throwTimer = throwCooldown;
        Speak(grenadeThrowLine);
    }

    [ContextMenu("Test Add Grenade")]
    private void Test_Grenade() => AddGrenades(1);
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