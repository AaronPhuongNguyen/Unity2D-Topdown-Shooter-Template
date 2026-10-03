Shader "Custom/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineSize ("Outline Size (px)", Range(0,10)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color, _OutlineColor;
            float _OutlineSize;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                float2 d = _MainTex_TexelSize.xy * _OutlineSize;
                float a = tex2D(_MainTex, i.uv + float2( d.x, 0)).a;
                a += tex2D(_MainTex, i.uv + float2(-d.x, 0)).a;
                a += tex2D(_MainTex, i.uv + float2(0,  d.y)).a;
                a += tex2D(_MainTex, i.uv + float2(0, -d.y)).a;

                // Pixel trong suốt nhưng có hàng xóm có alpha -> vẽ outline
                float outline = saturate(a) * (1 - c.a);
                c.rgb = lerp(c.rgb, _OutlineColor.rgb, outline);
                c.a = max(c.a, outline * _OutlineColor.a);
                return c;
            }
            ENDCG
        }
    }
}