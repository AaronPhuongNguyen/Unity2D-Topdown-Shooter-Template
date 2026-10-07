// Windows-style progress bar shine: a soft white band sweeps across the slider fill
// from RIGHT to LEFT, pauses, then repeats.
// Uses _UnscaledTime (set by LoadingHandle) so it keeps animating while Time.timeScale = 0.
Shader "UI/SliderShine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Shine)]
        _ShineColor ("Shine Color", Color) = (1,1,1,1)
        _ShineWidth ("Shine Width", Range(0.02, 1)) = 0.25
        _Intensity ("Intensity", Range(0, 1)) = 0.6
        _Speed ("Cycles Per Second", Float) = 0.6
        _Pause ("Pause Between Sweeps", Range(0, 0.9)) = 0.3
        _Skew ("Diagonal Skew", Range(-1, 1)) = 0.3

        // Required by UI (Mask / RectMask2D support)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 uv            : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _ShineColor;
            float4 _ClipRect;
            half _ShineWidth;
            half _Intensity;
            half _Speed;
            half _Pause;
            half _Skew;

            // Global, set from C# (LoadingHandle). Unaffected by Time.timeScale.
            float _UnscaledTime;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                // Position across the bar (0 = left, 1 = right), tilted for a diagonal band.
                float x = i.uv.x + (i.uv.y - 0.5) * _Skew;

                // 0..1 progress of the current sweep; the last part of each cycle is idle.
                float phase = frac(_UnscaledTime * _Speed);
                float t = saturate(phase / max(1.0 - _Pause, 0.001));

                // Band centre travels from just past the right edge to just past the left edge.
                float ext = _ShineWidth + abs(_Skew) * 0.5;
                float pos = lerp(1.0 + ext, -ext, t);

                // Soft-edged band.
                float s = saturate(1.0 - abs(x - pos) / _ShineWidth);
                s = s * s * (3.0 - 2.0 * s);

                c.rgb = lerp(c.rgb, _ShineColor.rgb, s * _Intensity * _ShineColor.a);

                c.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                return c;
            }
            ENDCG
        }
    }
}
