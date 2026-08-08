using UnityEngine;

public class ShadowRenderer : MonoBehaviour
{
    public SpriteRenderer sr;
    public SpriteRenderer shadow_sr;
    public Vector3 Offset = new Vector3(0.3f, -0.1f, 0);
    public Color shadowColor = new Color(0,0,0,0.5f);

    private void Update()
    {
        if(sr==null ||  shadow_sr==null) return;

        if (sr.sprite != shadow_sr.sprite)
        {
            shadow_sr.sprite = sr.sprite;
        }
        if(sr.transform.position + Offset != shadow_sr.transform.position)
        {
            Vector3 v = sr.transform.position + Offset;
            shadow_sr.transform.position = v;
            
        }
        if(shadow_sr.transform.rotation != sr.transform.rotation) 
            shadow_sr.transform.rotation = sr.transform.rotation;
        if (shadow_sr.transform.localScale != sr.transform.localScale)
            shadow_sr.transform.localScale = sr.transform.localScale;
        if(shadow_sr.color != shadowColor)
            shadow_sr.color = shadowColor;
    }
}