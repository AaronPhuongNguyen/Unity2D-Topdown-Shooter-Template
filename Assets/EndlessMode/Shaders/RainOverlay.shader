// Full-screen rain / lens-drops / fog overlay for a UI Image (Screen Space Overlay or Camera canvas).
// Layers: 3 parallax rain streak layers (wind slanted), sliding lens droplets with wet trails,
// tiny static droplets, soft drifting fog, and a lightning flash.
Shader "UI/RainOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Rain ("Rain", Range(0,1)) = 0
        _Wind ("Wind", Range(-1,1)) = 0
        _Fog ("Fog", Range(0,1)) = 0
        _Flash ("Flash", Range(0,1)) = 0
        _Ambient ("Ambient", Range(0,1)) = 1
        _T ("Time", Float) = 0
        _FlipY ("Flip Y", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Rain, _Wind, _Fog, _Flash, _Ambient, _T, _FlipY;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float4 sp : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.sp = ComputeScreenPos(o.pos);
                return o;
            }

            // ---------- hashes ----------
            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
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

            // premultiplied "over" blend
            void Over(inout float3 pc, inout float a, float3 c, float a2)
            {
                a2 = saturate(a2);
                pc = c * a2 + pc * (1.0 - a2);
                a = a2 + a * (1.0 - a2);
            }

            // ---------- falling rain streaks (one layer) ----------
            // cols: columns per screen height, dens: streak cells per screen height, len: tail length in cells.
            float Streaks(float2 uv, float aspect, float cols, float dens, float speed,
                          float len, float width, float wind, float seed, float amount)
            {
                float2 p = uv;
                p.x = p.x * aspect + p.y * wind;          // shear = wind slant

                float x = p.x * cols;
                float id = floor(x);
                float fx = frac(x) - 0.5;

                float h = hash11(id + seed);
                float xo = (h - 0.5) * 0.5;

                float y = p.y * dens + _T * speed * (0.75 + h * 0.5) + h * 37.0;
                float cy = floor(y);
                float fy = frac(y);

                float h2 = hash21(float2(id + seed, cy));
                float present = step(h2, amount);

                float w = smoothstep(width, 0.0, abs(fx - xo));
                float tail = smoothstep(len, 0.0, fy) * smoothstep(0.0, 0.03, fy);

                return present * w * tail * (0.5 + 0.5 * h2);
            }

            // ---------- lens droplets ----------
            // Square cells. Each cell: a drop grows, rests, then (if slideOn) slides down leaving a wet trail.
            // returns (body, highlight, rim, trail)
            float4 Drops(float2 uv, float aspect, float scale, float amount, float seed, float slideOn)
            {
                float2 p = float2(uv.x * aspect, uv.y) * scale;
                float2 id = floor(p);
                float2 f = frac(p);

                float2 r = hash22(id + seed);
                float rr = hash21(id * 1.37 + seed + 11.3);
                float on = step(rr, amount);

                float phase = frac(_T * (0.055 + 0.05 * r.y) + r.x * 13.7);
                float grow = smoothstep(0.0, 0.1, phase);
                float slideT = saturate((phase - 0.72) / 0.28) * slideOn;
                float slide = slideT * slideT;               // gravity: slow start, faster end
                float fadeOut = 1.0 - smoothstep(0.92, 1.0, phase);

                float px0 = 0.28 + 0.44 * r.x;
                float py0 = 0.55 + 0.30 * r.y;
                float rad = max((0.09 + 0.07 * r.y) * grow * (1.0 - 0.35 * slide) * fadeOut, 1e-4);

                float py = py0 - (py0 - 0.12) * slide;
                float px = px0 + sin(py * 14.0 + r.x * 6.0) * 0.03 * slideT;

                float2 q = (f - float2(px, py)) * float2(1.0, 0.85);
                float d = length(q);
                float body = smoothstep(rad, rad * 0.75, d);

                float2 n = q / rad;
                float hl = smoothstep(0.45, 0.0, length(n - float2(-0.35, 0.4))) * body;
                float rim = smoothstep(0.55, 1.0, length(n)) * body;

                // wet trail between the start spot and the current drop position
                float tx = px0 + sin(f.y * 14.0 + r.x * 6.0) * 0.03 * slideT;
                float inTrail = step(py, f.y) * step(f.y, py0) * step(0.001, slideT);
                float tw = 0.035 + 0.02 * r.y;
                float tr = smoothstep(tw, 0.0, abs(f.x - tx)) * inTrail;
                tr *= 1.0 - 0.6 * saturate((f.y - py) / max(py0 - py, 0.01));
                tr *= fadeOut;

                return float4(body, hl, rim, tr) * on;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.sp.xy / IN.sp.w;
                if (_FlipY > 0.5) uv.y = 1.0 - uv.y;

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float rain = _Rain;
                float wind = _Wind * 0.35;

                float3 pc = 0;
                float a = 0;

                // --- fog: soft drifting noise ---
                if (_Fog > 0.01)
                {
                    float2 fp = float2(uv.x * aspect, uv.y) * 1.6;
                    float n = vnoise(fp + float2(_T * 0.03, 0)) * 0.6
                            + vnoise(fp * 2.3 - float2(_T * 0.05, _T * 0.01)) * 0.3
                            + vnoise(fp * 5.1 + float2(0, _T * 0.02)) * 0.1;
                    float3 fogCol = lerp(float3(0.55, 0.6, 0.66), float3(0.82, 0.85, 0.88), _Ambient) * (0.35 + 0.65 * _Ambient);
                    Over(pc, a, fogCol, _Fog * (0.18 + 0.45 * n));
                }

                // --- lens droplets (only when it really rains) ---
                float lens = smoothstep(0.1, 0.7, rain);
                if (lens > 0.001)
                {
                    float4 d1 = Drops(uv, aspect, 5.5, rain * 0.45 * lens, 7.0, 1.0);
                    float4 d2 = Drops(uv, aspect, 10.0, rain * 0.65 * lens, 23.0, 1.0);
                    float4 d3 = Drops(uv, aspect, 26.0, rain * 0.9 * lens, 51.0, 0.0);

                    float body = max(max(d1.x, d2.x), d3.x * 0.7);
                    float hl = max(max(d1.y, d2.y), d3.y * 0.7);
                    float rim = max(max(d1.z, d2.z), d3.z * 0.7);
                    float trail = max(d1.w, d2.w);

                    float3 glass = float3(0.85, 0.92, 1.0) * _Ambient;

                    Over(pc, a, glass * 0.9, trail * 0.07);                          // wet smear
                    Over(pc, a, glass, body * 0.10);                                 // drop body
                    Over(pc, a, float3(0.02, 0.04, 0.07), rim * 0.22);               // dark refraction rim
                    Over(pc, a, float3(1, 1, 1) * (0.4 + 0.6 * _Ambient), hl * 0.75);// specular glint
                }

                // --- rain streaks, 3 parallax layers ---
                if (rain > 0.01)
                {
                    float far = Streaks(uv, aspect, 95.0, 9.0, 2.6, 0.35, 0.06, wind * 0.9, 3.0, rain * 0.9) * 0.25;
                    float mid = Streaks(uv, aspect, 45.0, 5.5, 3.8, 0.40, 0.08, wind, 17.0, rain * 0.75) * 0.40;
                    float near = Streaks(uv, aspect, 22.0, 3.2, 5.5, 0.45, 0.12, wind * 1.1, 41.0, rain * 0.55) * 0.55;

                    float streak = saturate(far + mid + near);
                    float3 rainCol = float3(0.78, 0.86, 0.95) * (0.3 + 0.7 * _Ambient);
                    Over(pc, a, rainCol, streak * 0.8);
                }

                // --- lightning flash ---
                Over(pc, a, float3(0.85, 0.9, 1.0), _Flash);

                float3 rgb = pc / max(a, 1e-4);
                return fixed4(rgb, a * IN.color.a);
            }
            ENDCG
        }
    }
}
