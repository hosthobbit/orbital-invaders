// Additive atmospheric halo shell around a planet, brighter on the sunlit limb.
Shader "Orbital/Atmosphere"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.3, 0.6, 1.5, 1)
        _Power ("Falloff power", Float) = 4
        _Night ("Night side brightness", Float) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Power;
                float _Night;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            V Vert(A v)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float rim = 1.0 - saturate(dot(n, v));
                // fade to zero exactly at the silhouette so the halo edge is soft
                float halo = pow(rim, _Power) * smoothstep(1.0, 0.9, rim);
                float lightSide = saturate(dot(n, GetMainLight().direction) * 0.7 + 0.45);
                return half4(_Color.rgb * halo * max(lightSide, _Night), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
