Shader "Survival/WaterFlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Tile Texture", 2D) = "white" {}
        _Color ("Tint (alpha = opacity)", Color) = (0.25, 0.55, 0.95, 1)

        [Header(Flow)]
        _FlowDir ("Flow Direction (XY)", Vector) = (1, -0.3, 0, 0)
        _FlowSpeed ("Flow Speed", Float) = 0.6
        _Scale ("Noise Scale (per world unit)", Float) = 0.9
        _Stretch ("Stretch Along Flow", Float) = 3
        _Wobble ("Side Wobble", Range(0, 1)) = 0.35

        [Header(Look)]
        _ShadeAmount ("Light/Dark Variation", Range(0, 1)) = 0.35
        _HighlightColor ("Highlight Color (alpha = strength)", Color) = (0.85, 0.95, 1, 0.8)
        _HighlightThreshold ("Highlight Threshold", Range(0.3, 0.9)) = 0.62
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _FlowDir;
            float _FlowSpeed, _Scale, _Stretch, _Wobble;
            float _ShadeAmount, _HighlightThreshold;
            fixed4 _HighlightColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;      // Tilemap color
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float2 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xy;   // world space = no seams between tiles
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    v += vnoise(p) * a;
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v / 0.875;   // 0..1
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseTex = tex2D(_MainTex, i.uv);

                // Flow basis: u runs along the flow, v across it.
                float2 dir = normalize(_FlowDir.xy + float2(1e-5, 0));
                float2 perp = float2(-dir.y, dir.x);
                float2 q = float2(dot(i.worldPos, dir), dot(i.worldPos, perp));

                float t = _Time.y * _FlowSpeed;

                // Layer 1: long streaks sliding downstream.
                float2 p1 = float2(q.x / _Stretch - t, q.y);
                p1.y += sin(q.x * 0.7 + t * 2.0) * _Wobble;
                float n1 = fbm(p1 * _Scale);

                // Layer 2: faster, finer, different offset (parallax = feels like depth).
                float2 p2 = float2(q.x / _Stretch * 1.7 - t * 1.6 + 17.3, q.y * 1.4 - 5.1);
                p2.y += sin(q.x * 1.3 - t * 3.0) * _Wobble * 0.6;
                float n2 = fbm(p2 * _Scale);

                float n = n1 * 0.6 + n2 * 0.4;

                // Soft light/dark movement + bright streak highlights.
                float3 col = baseTex.rgb * _Color.rgb * i.color.rgb;
                col += (n - 0.5) * _ShadeAmount;

                float hl = smoothstep(_HighlightThreshold, _HighlightThreshold + 0.12, n);
                col = lerp(col, _HighlightColor.rgb, hl * _HighlightColor.a);

                float alpha = baseTex.a * _Color.a * i.color.a;
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}