using UnityEngine;

public class ShadowRenderer : MonoBehaviour, ILateTick
{
    public SpriteRenderer sr;
    public SpriteRenderer shadow_sr;
    public Vector3 Offset = new Vector3(0.3f, -0.1f, 0);
    public Color shadowColor = new Color(0, 0, 0, 0.5f);

    // Mirrors sr's current transform/sprite/color - no time-integrated
    // math here, so it belongs after whatever moved sr this frame (motion,
    // camera, etc.) rather than racing it. Same reasoning as CamManager:
    // runs as a LateTick so it reads sr's final position for this frame.
    private void OnEnable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Register((ILateTick)this);
    }

    private void OnDisable()
    {
        if (TickSystem.Instance != null)
            TickSystem.Unregister((ILateTick)this);
    }

    public void LateTick(float delta)
    {
        if (sr == null || shadow_sr == null) return;

        if (sr.sprite != shadow_sr.sprite)
        {
            shadow_sr.sprite = sr.sprite;
        }
        if (sr.transform.position + Offset != shadow_sr.transform.position)
        {
            Vector3 v = sr.transform.position + Offset;
            shadow_sr.transform.position = v;
        }
        if (shadow_sr.transform.rotation != sr.transform.rotation)
            shadow_sr.transform.rotation = sr.transform.rotation;
        if (shadow_sr.transform.localScale != sr.transform.localScale)
            shadow_sr.transform.localScale = sr.transform.localScale;
        if (shadow_sr.color != shadowColor)
            shadow_sr.color = shadowColor;
    }
}