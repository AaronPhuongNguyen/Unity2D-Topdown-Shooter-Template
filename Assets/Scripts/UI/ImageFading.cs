using UnityEngine;
using TMPro;
using UnityEngine.UI;

[DefaultExecutionOrder(500)]
public class ImageFading : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Image text;

    [Header("Fade Settings")]
    [SerializeField] private float minAlpha = 0.1f;
    [SerializeField] private float maxAlpha = 1f;
    [SerializeField] private float speed = 1f;
    [SerializeField] private bool useUnscaledTime = false;


    private void Awake()
    {
        if (text == null) text = GetComponent<Image>();
    }

    private void Update()
    {
        if (text == null) return;

        float t = useUnscaledTime ? Time.unscaledTime : Time.time;

        float alpha01 = (Mathf.Sin(t * speed) + 1f) * 0.5f;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, alpha01);

        SetAlpha(alpha);
    }

    private void SetAlpha(float alpha)
    {
        Color c = text.color;
        c.a = alpha;
        text.color = c;
    }
}