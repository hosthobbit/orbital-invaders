// Unlit glow used for particles, beams, move markers, runes and shockwaves.
Shader "Orbital/Glow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Colour", Color) = (1,1,1,1)
        _Fresnel ("Edge fade (0 off)", Float) = 0
        _Scroll ("UV scroll", Vector) = (0,0,0,0)
        _SoftEdge ("Soft V edge (beams)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _Fresnel;
                float4 _Scroll;
                float _SoftEdge;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
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
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + _Scroll.xy * _Time.y;
                o.color = v.color;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(vpi.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 col = tex * _Color * i.color;
                if (_Fresnel != 0)
                {
                    float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    half f = saturate(abs(dot(normalize(i.normalWS), viewDir)));
                    // positive: bright facing the camera (beams); negative: bright at the rim (bubbles)
                    col *= _Fresnel > 0 ? pow(f, _Fresnel) : pow(1.0 - f, -_Fresnel);
                }
                if (_SoftEdge > 0)
                {
                    float v = frac(i.uv.y - _Scroll.y * _Time.y);
                    col *= saturate(sin(v * 3.14159) * _SoftEdge);
                }
                // Fog fades additive glow toward black so distant fx sink into the dark.
                // only when fog is on: with fog off URP's ComputeFogIntensity returns 0 and blacks everything out
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                col.rgb *= ComputeFogIntensity(i.fogFactor);
                #endif
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
