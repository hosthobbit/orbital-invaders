// Deep-space sky sphere: layered stars, galaxy band and coloured nebulae.
Shader "Orbital/Sky"
{
    Properties
    {
        [HDR] _NebulaA ("Nebula A", Color) = (0.55, 0.08, 0.45, 1)
        [HDR] _NebulaB ("Nebula B", Color) = (0.05, 0.25, 0.55, 1)
        [HDR] _Band ("Galaxy band", Color) = (0.55, 0.5, 0.45, 1)
        _StarBrightness ("Star brightness", Float) = 3
        _BandNormal ("Galaxy band normal", Vector) = (0.3, 0.9, 0.25, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _NebulaA, _NebulaB, _Band;
                float _StarBrightness;
                float4 _BandNormal;
            CBUFFER_END

            float H(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float3 H3(float3 p) { return float3(H(p), H(p + 17.3), H(p + 41.9)); }
            float N(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(H(i), H(i + float3(1,0,0)), f.x), lerp(H(i + float3(0,1,0)), H(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(H(i + float3(0,0,1)), H(i + float3(1,0,1)), f.x), lerp(H(i + float3(0,1,1)), H(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float F(float3 p) { float v = 0, a = 0.5; for (int o = 0; o < 6; o++) { v += a * N(p); p = p * 2.02 + 1.3; a *= 0.5; } return v; }

            float Stars(float3 d, float scale, float density)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float3 r = H3(cell);
                if (r.x > density) return 0;
                float3 sp = cell + 0.2 + 0.6 * r;
                float dist = length(p - sp);
                float size = 0.06 + 0.12 * r.y;
                float twinkle = 0.75 + 0.25 * sin(_Time.y * (1.0 + 3.0 * r.z) + r.y * 40.0);
                return smoothstep(size, 0.0, dist) * (0.4 + 1.6 * r.z * r.z) * twinkle;
            }

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V Vert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float band = exp(-abs(dot(d, normalize(_BandNormal.xyz))) * 5.0);
                float bandDust = F(d * 6.0 + 3.0);
                half3 col = _Band.rgb * band * (0.35 + 0.9 * bandDust * bandDust) * 0.6;
                // dark dust lanes in the band
                col *= 1.0 - smoothstep(0.55, 0.75, F(d * 11.0 + 7.0)) * band * 0.8;

                float neb1 = F(d * 2.3 + 11.0);
                float neb2 = F(d * 3.1 + 29.0);
                col += _NebulaA.rgb * pow(saturate(neb1 * 1.6 - 0.55), 2.5) * 1.6;
                col += _NebulaB.rgb * pow(saturate(neb2 * 1.6 - 0.55), 2.5) * 1.6;

                float s = Stars(d, 180.0, 0.12) + Stars(d, 420.0, 0.1) * 0.7 + Stars(d, 90.0, 0.06) * 1.4;
                s *= 1.0 + band * 2.0;
                col += s * _StarBrightness * lerp(half3(0.8, 0.88, 1.0), half3(1.0, 0.85, 0.7), H(floor(d * 180.0)));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
