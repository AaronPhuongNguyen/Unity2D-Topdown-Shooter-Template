using UnityEngine;

/// <summary>
/// Picks one random sprite from a list and applies it to a SpriteRenderer
/// on Awake - useful for visual variety on spawned/pooled objects (rocks,
/// grass clumps, debris, corpses, etc.) without needing separate prefab
/// variants per sprite.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class RandomSpritePicker : MonoBehaviour
{
    #region Inspector
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] sprites;
    #endregion

    #region Lifecycle
    private void Reset()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
    }

    private void Awake()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        PickRandom();
    }
    #endregion

    #region Functions
    /// <summary>
    /// Re-rolls a new random sprite. Public so pooled objects can call this
    /// on reuse (e.g. from OnEnable) instead of only ever picking once on
    /// the very first Awake.
    /// </summary>
    public void PickRandom()
    {
        if (sr == null || sprites == null || sprites.Length == 0) return;
        sr.sprite = sprites[Random.Range(0, sprites.Length)];
    }
    #endregion
}