using TMPro;
using UnityEngine;

/// <summary>
/// One floating number. Pure view + tiny state: the manager drives Tick(),
/// so there is no Update / coroutine on this object.
/// Prefab needs a TMP text (this object or a child). CanvasGroup is added automatically.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DamagePopup : MonoBehaviour
{
    private RectTransform rect;
    private CanvasGroup group;
    private TMP_Text label;

    private Vector2 pos, vel;
    private float age, life;
    private float startScale, endScale;

    /// <summary>Called once when the popup is created.</summary>
    public void Build()
    {
        rect = (RectTransform)transform;
        label = GetComponentInChildren<TMP_Text>(true);
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();

        // Position is set in canvas-local space around the canvas center.
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    /// <summary>Called each time the popup is shown.</summary>
    public void Setup(Vector2 startPos, Vector2 velocity, string text, Color color,
                      float scaleStart, float scaleEnd, float lifetime)
    {
        pos = startPos;
        vel = velocity;
        age = 0f;
        life = Mathf.Max(0.05f, lifetime);
        startScale = scaleStart;
        endScale = scaleEnd;

        if (label != null)
        {
            label.text = text;
            label.color = color;
        }

        rect.anchoredPosition = pos;
        rect.localScale = Vector3.one * startScale;
        group.alpha = 1f;
    }

    /// <summary>Returns false when the popup is finished.</summary>
    public bool Tick(float dt, float gravity, float drag, float fadeStart)
    {
        age += dt;
        float t = age / life;
        if (t >= 1f) return false;

        // Jump arc
        vel.y -= gravity * dt;
        vel.x -= vel.x * Mathf.Min(1f, drag * dt);
        pos += vel * dt;
        rect.anchoredPosition = pos;

        // Starts big, slowly shrinks
        rect.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, t);

        // Holds, then fades
        group.alpha = t <= fadeStart ? 1f : 1f - (t - fadeStart) / (1f - fadeStart);
        return true;
    }
}