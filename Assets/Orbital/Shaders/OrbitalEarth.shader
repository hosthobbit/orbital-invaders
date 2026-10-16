// Procedural Earth: continents, oceans with sun glint, clouds, night-side city lights, atmosphere rim.
Shader "Orbital/Earth"
{
    Properties
    {
        _Ocean ("Ocean", Color) = (0.02, 0.09, 0.22, 1)
        _OceanShallow ("Ocean shallow", Color) = (0.03, 0.22, 0.35, 1)
        _Land ("Land", Color) = (0.12, 0.22, 0.08, 1)
        _Desert ("Desert", Color) = (0.45, 0.36, 0.22, 1)
        _Ice ("Ice", Color) = (0.9, 0.93, 0.96, 1)
        [HDR] _CityLights ("City lights", Color) = (3.0, 1.6, 0.6, 1)
        [HDR] _Atmosphere ("Atmosphere", Color) = (0.35, 0.65, 1.6, 1)
        _Seed ("Seed", Vector) = (3.1, 7.7, 1.3, 0)
        _CloudSpeed ("Cloud speed", Float) = 0.004
        _Exposure ("Exposure", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-10" }

        Pass
        {
            Name "Earth"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Ocean, _OceanShallow, _Land, _Desert, _Ice, _CityLights, _Atmosphere;
                float4 _Seed;
                float _CloudSpeed, _Exposure;
            CBUFFER_END

            float H(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float N(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(H(i), H(i + float3(1,0,0)), f.x), lerp(H(i + float3(0,1,0)), H(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(H(i + float3(0,0,1)), H(i + float3(1,0,1)), f.x), lerp(H(i + float3(0,1,1)), H(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float F(float3 p, int oct)
            {
                float v = 0, a = 0.5;
                for (int o = 0; o < oct; o++) { v += a * N(p); p = p * 2.03 + 1.7; a *= 0.5; }
                return v;
            }

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 dirOS : TEXCOORD2; };

            V Vert(A v)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.dirOS = normalize(v.positionOS.xyz);
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                float3 d = normalize(i.dirOS);
                float3 n = normalize(i.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light sun = GetMainLight();
                float3 L = sun.direction;

                // continents
                float c = F(d * 2.1 + _Seed.xyz, 6);
                c += 0.15 * F(d * 9.0 + _Seed.zxy, 4) - 0.075;
                float land = smoothstep(0.515, 0.53, c);
                float shallow = smoothstep(0.45, 0.515, c) * (1.0 - land);
                float lat = abs(d.y);
                float dry = smoothstep(0.45, 0.65, F(d * 4.0 + 11.0, 4)) * (1.0 - smoothstep(0.2, 0.55, lat));
                half3 landCol = lerp(_Land.rgb, _Desert.rgb, dry);
                landCol *= 0.75 + 0.5 * F(d * 30.0, 3);
                half3 col = lerp(lerp(_Ocean.rgb, _OceanShallow.rgb, shallow), landCol, land);
                float ice = smoothstep(0.8, 0.86, lat + 0.06 * F(d * 12.0, 3));
                col = lerp(col, _Ice.rgb, ice);

                // clouds drifting
                float3 cd = d;
                float ang = _Time.y * _CloudSpeed;
                cd.xz = float2(cd.x * cos(ang) - cd.z * sin(ang), cd.x * sin(ang) + cd.z * cos(ang));
                float cl = F(cd * 3.2 + F(cd * 6.0, 3) * 1.2, 6);
                float clouds = smoothstep(0.56, 0.78, cl) * 0.85;

                // lighting
                float ndl = dot(n, L);
                float day = smoothstep(-0.12, 0.25, ndl);
                float diff = saturate(ndl * 0.9 + 0.1);
                float3 h = normalize(L + viewDir);
                float spec = pow(saturate(dot(n, h)), 90.0) * (1.0 - land) * (1.0 - clouds) * 2.2 * day;
                half3 lit = col * diff * sun.color * 0.95 + spec * sun.color;
                lit = lerp(lit, half3(1, 1, 1) * (diff * 1.05 + 0.02) * sun.color, clouds * 0.9);

                // night side city lights
                float cities = smoothstep(0.62, 0.75, F(d * 40.0 + 5.0, 3)) * smoothstep(0.45, 0.6, F(d * 7.0 + 2.0, 3));
                lit += _CityLights.rgb * cities * land * (1.0 - ice) * (1.0 - day) * (1.0 - clouds * 0.7);

                // atmosphere rim + terminator glow
                float rim = pow(1.0 - saturate(dot(n, viewDir)), 2.6);
                float lightFacing = saturate(ndl * 0.8 + 0.35);
                lit += _Atmosphere.rgb * rim * lightFacing * 0.8;
                lit += half3(1.0, 0.35, 0.12) * pow(1.0 - abs(ndl), 12.0) * 0.12 * rim;

                return half4(lit * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
