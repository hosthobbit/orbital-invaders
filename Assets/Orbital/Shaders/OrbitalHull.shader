// Lit hull shader: ships, voxel invaders, debris, bunkers, moon.
// Vertex colour: rgb = paint (when _UseVertexColor), a = emission mask.
Shader "Orbital/Hull"
{
    Properties
    {
        _BaseColor ("Base colour", Color) = (0.85, 0.87, 0.9, 1)
        _Metallic ("Metallic", Range(0,1)) = 0.6
        _Smoothness ("Smoothness", Range(0,1)) = 0.7
        _UseVertexColor ("Use vertex colour", Float) = 0
        _EmissionStrength ("Emission strength (x vertex alpha)", Float) = 0
        [HDR] _EmissionTint ("Emission tint", Color) = (1,1,1,1)
        _EmissionPulse ("Emission pulse", Float) = 0
        _PanelScale ("Panel line scale (0 off)", Float) = 0
        _PanelDepth ("Panel line darkness", Range(0,1)) = 0.5
        _NoiseScale ("Albedo noise scale (0 off)", Float) = 0
        _NoiseAmount ("Albedo noise amount", Range(0,1)) = 0.4
        _Bumpiness ("Bumpiness", Float) = 0
        [HDR] _RimColor ("Rim colour", Color) = (0.3, 0.6, 1, 1)
        _Rim ("Rim amount", Range(0,4)) = 0
        [HDR] _FlashColor ("Flash colour", Color) = (3, 3, 3, 1)
        _Flash ("Flash", Range(0,1)) = 0
        _Dissolve ("Dissolve", Range(0,1)) = 0
        [HDR] _DissolveEdge ("Dissolve edge", Color) = (4, 1.5, 0.5, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Metallic;
            half _Smoothness;
            float _UseVertexColor;
            float _EmissionStrength;
            half4 _EmissionTint;
            float _EmissionPulse;
            float _PanelScale;
            half _PanelDepth;
            float _NoiseScale;
            half _NoiseAmount;
            float _Bumpiness;
            half4 _RimColor;
            half _Rim;
            half4 _FlashColor;
            half _Flash;
            half _Dissolve;
            half4 _DissolveEdge;
        CBUFFER_END

        float OHash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ONoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(OHash(i), OHash(i + float3(1,0,0)), f.x),
                             lerp(OHash(i + float3(0,1,0)), OHash(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(OHash(i + float3(0,0,1)), OHash(i + float3(1,0,1)), f.x),
                             lerp(OHash(i + float3(0,1,1)), OHash(i + float3(1,1,1)), f.x), f.y), f.z);
        }

        float OFbm(float3 p)
        {
            float v = 0, a = 0.5;
            [unroll] for (int o = 0; o < 4; o++) { v += a * ONoise(p); p = p * 2.07 + 3.1; a *= 0.5; }
            return v;
        }

        void DissolveClip(float3 p)
        {
            if (_Dissolve > 0.001) clip(OFbm(p * 6.0) + 0.02 - _Dissolve * 0.95);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float4 color : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs vpi = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = vpi.positionCS;
                o.positionWS = vpi.positionWS;
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalOS = v.normalOS;
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(i.positionOS);
                float3 n = normalize(i.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);

                half3 albedo = _BaseColor.rgb;
                if (_UseVertexColor > 0.5) albedo *= i.color.rgb;

                if (_NoiseScale > 0)
                {
                    float nz = OFbm(i.positionOS * _NoiseScale);
                    albedo *= 1.0 - _NoiseAmount + _NoiseAmount * 2.0 * nz;
                    // craters / pits
                    float pits = smoothstep(0.62, 0.7, OFbm(i.positionOS * _NoiseScale * 0.5 + 9.0));
                    albedo *= 1.0 - pits * _NoiseAmount * 0.6;
                }

                half smooth = _Smoothness;
                if (_PanelScale > 0)
                {
                    // panel seams on the two axes most perpendicular to the normal
                    float3 an = abs(normalize(i.normalOS));
                    float3 p = i.positionOS * _PanelScale;
                    float2 uv = an.x > an.y && an.x > an.z ? p.yz : (an.y > an.z ? p.xz : p.xy);
                    float2 g = abs(frac(uv + 0.5) - 0.5);
                    float seam = 1.0 - smoothstep(0.0, 0.035, min(g.x, g.y));
                    float plate = OHash(floor(float3(uv, 3.0)));
                    albedo *= (1.0 - seam * _PanelDepth) * (0.93 + 0.1 * plate);
                    smooth *= 1.0 - seam * 0.6;
                }

                if (_Bumpiness > 0)
                {
                    float e = 0.03;
                    float3 bp = i.positionOS * 7.0;
                    float b0 = OFbm(bp);
                    float3 grad = float3(OFbm(bp + float3(e,0,0)) - b0, OFbm(bp + float3(0,e,0)) - b0, OFbm(bp + float3(0,0,e)) - b0) / e;
                    grad = TransformObjectToWorldDir(grad);
                    n = normalize(n - (grad - dot(grad, n) * n) * _Bumpiness * 0.03);
                }

                half3 emission = i.color.rgb * i.color.a * _EmissionStrength * _EmissionTint.rgb;
                if (_EmissionPulse > 0) emission *= 1.0 + _EmissionPulse * 0.35 * sin(_Time.y * 4.0 + i.positionWS.x * 0.7);

                half fres = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                emission += _RimColor.rgb * fres * _Rim;
                emission += _FlashColor.rgb * _Flash;

                if (_Dissolve > 0.001)
                {
                    float d = OFbm(i.positionOS * 6.0) + 0.02 - _Dissolve * 0.95;
                    emission += _DissolveEdge.rgb * smoothstep(0.08, 0.0, d);
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = _Metallic;
                s.smoothness = smooth;
                s.occlusion = 1.0;
                s.emission = emission;
                s.alpha = 1.0;
                s.normalTS = half3(0, 0, 1);

                half4 col = UniversalFragmentPBR(inputData, s);
                col.a = 1.0;
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings ShadowVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir)));
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(i.positionOS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings DepthVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half DepthFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(i.positionOS);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings DNVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 DNFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(i.positionOS);
                float3 n = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5)), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
