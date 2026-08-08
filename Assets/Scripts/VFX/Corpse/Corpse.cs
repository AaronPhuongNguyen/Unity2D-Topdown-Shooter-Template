using Server;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(600)]
public class Corpse : MonoBehaviour, ITick
{
    #region Settings
    [Header("Corpse Fall")]
    [SerializeField] private float fallTiltAngle = 30f;
    [SerializeField] private float fallDuration = 0.2f;

    [Header("Slide Inertia")]
    [SerializeField] private float slideDrag = 6f; // higher = stops faster

    [Header("Blood")]
    [SerializeField] private int bloodSplatCount = 8;
    [SerializeField] private float bloodScatterRadius = 1f;
    [SerializeField] private Vector2 bloodScaleRange = new Vector2(0.5f, 1.2f);

    // Fade only actually changes anything in the last this-many seconds of
    // life (see UpdateFade's alpha calc) - matches the 2f divisor there.
    private const float FadeWindow = 2f;
    #endregion

    #region Cache
    private float life;
    private bool isCreated;

    private SpriteRenderer corpseRenderer;
    private readonly List<SpriteRenderer> bloodRenderers = new List<SpriteRenderer>();

    private float fallTimer;
    private bool isFalling;
    private float fallFacingAngle;

    private Vector2 slideVelocity;
    #endregion

    private void OnEnable()
    {
        EventBus.OnGameRestart += ClearSelf;
    }
    private void OnDisable()
    {
        EventBus.OnGameRestart -= ClearSelf;
    }
    private void ClearSelf()
    {
        if (life > 0) life = 0f;
        else Corrupt();
    }
    public void StartCorrupt(float time, SpritePackage corpse, SpritePackage blood, Vector2? slideDirection = null, float slideForce = 0f)
    {
        isCreated = true;
        life = time;
        fallTimer = 0f;
        isFalling = true;

        Vector2 dir = (slideDirection.HasValue && slideDirection.Value != Vector2.zero) ? slideDirection.Value.normalized : Vector2.down;
        fallFacingAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        slideVelocity = dir * slideForce;

        SpawnCorpseSprite(corpse);
        SpawnBloodSprites(blood);

        // Register with the central ticker instead of running our own
        // Update() - see CorpseTicker for why. Safe to call every spawn:
        // Register() no-ops if already present (e.g. pooled reuse edge case).
        if (CorpseTicker.instance != null) CorpseTicker.instance.Register(this);
    }

    #region Spawning
    private void SpawnCorpseSprite(SpritePackage corpsePackage)
    {
        Sprite sprite = corpsePackage != null ? corpsePackage.GetRandomSprite() : null;
        if (sprite == null) return;

        if (corpseRenderer == null)
        {
            GameObject go = new GameObject("CorpseSprite");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            corpseRenderer = go.AddComponent<SpriteRenderer>();
        }

        corpseRenderer.sprite = sprite;
        corpseRenderer.color = Color.white;
        corpseRenderer.transform.localRotation = Quaternion.identity;
        corpseRenderer.gameObject.SetActive(true);
    }

    private void SpawnBloodSprites(SpritePackage bloodPackage)
    {
        if (bloodPackage == null || bloodPackage.sprites == null || bloodPackage.sprites.Count == 0) return;

        while (bloodRenderers.Count < bloodSplatCount)
        {
            GameObject go = new GameObject("Blood");
            go.transform.SetParent(transform);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -1;
            bloodRenderers.Add(sr);
        }

        for (int i = 0; i < bloodRenderers.Count; i++)
        {
            SpriteRenderer sr = bloodRenderers[i];
            bool active = i < bloodSplatCount;
            sr.gameObject.SetActive(active);
            if (!active) continue;

            sr.sprite = bloodPackage.GetRandomSprite();

            Vector2 offset = RNG.GetInsideCircle(bloodScatterRadius);
            sr.transform.localPosition = new Vector3(offset.x, offset.y, 0.01f);

            float scale = RNG.GetFloat(bloodScaleRange.x, bloodScaleRange.y);
            sr.transform.localScale = Vector3.one * scale;
            sr.transform.localRotation = Quaternion.Euler(0, 0, RNG.GetFloat(0, 360f));

            Color c = sr.color;
            c.a = 1f;
            sr.color = c;
        }
    }
    #endregion

    #region Lifecycle
    // Driven by CorpseTicker instead of Unity's own Update() dispatch -
    // same reasoning as Zombrain/HiveBrain: one central loop over all
    // active corpses is cheaper on mobile than N separate MonoBehaviour
    // Update() calls, especially since corpses can pile up with a 120s
    // default lifetime.
    public void Tick(float dt)
    {
        if (!isCreated) return;

        if (isFalling) UpdateFall(dt);

        UpdateSlide(dt);

        if (life > 0)
        {
            life -= dt;

            // Skip the fade loop entirely until we're actually inside the
            // fade window - UpdateFade's alpha calc clamps to 1 the whole
            // time before that anyway, so running it every frame for a
            // corpse's full 120s lifetime was pure wasted work (a loop
            // over every blood-splat renderer, per corpse, per frame, for
            // most of its life, doing nothing visible).
            if (life <= FadeWindow) UpdateFade();
        }
        else
        {
            Corrupt();
        }
    }

    /// <summary>Tilts the corpse toward its death-facing direction, easing in over fallDuration.</summary>
    private void UpdateFall(float dt)
    {
        fallTimer += dt;
        float t = Mathf.Clamp01(fallTimer / fallDuration);
        float eased = 1f - Mathf.Pow(1f - t, 2f); // ease-out

        if (corpseRenderer != null)
        {
            float currentTilt = Mathf.Lerp(0f, fallTiltAngle, eased);
            corpseRenderer.transform.localRotation = Quaternion.Euler(0, 0, fallFacingAngle - 90f + currentTilt);
        }

        if (t >= 1f) isFalling = false;
    }

    /// <summary>Manually decays slide velocity and moves the corpse, giving a short "skid to a stop" feel.</summary>
    private void UpdateSlide(float dt)
    {
        if (slideVelocity.sqrMagnitude < 0.0001f) return;

        transform.position += (Vector3)(slideVelocity * dt);

        slideVelocity = Vector2.Lerp(slideVelocity, Vector2.zero, slideDrag * dt);

        if (slideVelocity.sqrMagnitude < 0.01f) slideVelocity = Vector2.zero;
    }

    private void UpdateFade()
    {
        float alpha = Mathf.Clamp01(life / FadeWindow);

        if (corpseRenderer != null)
        {
            Color c = corpseRenderer.color;
            c.a = alpha;
            corpseRenderer.color = c;
        }

        for (int i = 0; i < bloodRenderers.Count; i++)
        {
            var sr = bloodRenderers[i];
            if (!sr.gameObject.activeSelf) continue;
            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }
    }
    #endregion

    #region Corruption
    private void Corrupt()
    {
        isCreated = false;
        isFalling = false;
        slideVelocity = Vector2.zero;

        if (corpseRenderer != null) corpseRenderer.gameObject.SetActive(false);
        foreach (var sr in bloodRenderers) sr.gameObject.SetActive(false);
        if (CorpseTicker.instance != null) CorpseTicker.instance.Unregister(this);

        PoolingSystem.instance.RemoveToPool(this.gameObject);
    }
    #endregion
}