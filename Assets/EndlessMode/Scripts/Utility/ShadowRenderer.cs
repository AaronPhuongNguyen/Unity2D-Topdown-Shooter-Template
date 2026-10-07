using UnityEngine;

public class ShadowRenderer : MonoBehaviour, ILateTick
{
    public SpriteRenderer sr;
    public SpriteRenderer shadow_sr;
    public Vector3 Offset = new Vector3(0.3f, -0.1f, 0);
    public Color shadowColor = new Color(0, 0, 0, 0.5f);
    public string shadowLayerName = "Shadow";

    bool isObjectStatic = false;

    // Mirrors sr's current transform/sprite/color - no time-integrated
    // math here, so it belongs after whatever moved sr this frame (motion,
    // camera, etc.) rather than racing it. Same reasoning as CamManager:
    // runs as a LateTick so it reads sr's final position for this frame.

    private void Reset()
    {
        if (!TryGetComponent<SpriteRenderer>(out shadow_sr))
            shadow_sr = gameObject.AddComponent<SpriteRenderer>();
        shadow_sr.sortingLayerName = shadowLayerName;
        isObjectStatic = gameObject.isStatic;
    }
    private void Awake()
    {
        if(sr!=null && shadow_sr !=null)
            shadow_sr.sortingOrder = sr.sortingOrder;

        isObjectStatic = gameObject.isStatic;
        gameObject.SetActive(true);
    }

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
        isObjectStatic = gameObject.isStatic;
        if (sr == null || shadow_sr == null) return;

        if (sr.sprite != shadow_sr.sprite)
        {
            shadow_sr.sprite = sr.sprite;
        }
        if (shadow_sr.color != shadowColor)
            shadow_sr.color = shadowColor;

        if (isObjectStatic) return;
        if (sr.transform.position + Offset != shadow_sr.transform.position)
        {
            Vector3 v = sr.transform.position + Offset;
            shadow_sr.transform.position = v;
        }
        if (shadow_sr.transform.rotation != sr.transform.rotation)
            shadow_sr.transform.rotation = sr.transform.rotation;
        if (shadow_sr.transform.localScale != sr.transform.localScale)
            shadow_sr.transform.localScale = sr.transform.localScale;
        
    }
}