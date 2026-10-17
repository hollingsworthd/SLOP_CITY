Shader "SlopCity/WetCitySurface"
{
    Properties
    {
        _Color ("Authored tint", Color) = (1,1,1,1)
        _MainTex ("Authored scanned surface", 2D) = "white" {}
        _BumpMap ("Authored normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 1
        _MetallicGlossMap ("Authored metal / roughness", 2D) = "white" {}
        _Metallic ("Metal", Range(0,1)) = 0
        _Glossiness ("Authored smoothness", Range(0,1)) = .43
        _GlossMapScale ("Roughness map scale", Range(0,1)) = 1
        _OcclusionMap ("Authored occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion strength", Range(0,1)) = 1
        _EmissionMap ("Authored emission", 2D) = "white" {}
        _EmissionColor ("Authored radiance", Color) = (0,0,0,1)
        _WetFilmCoverage ("Rain pool coverage", Range(0,1)) = .78
        _WetFilmSmoothness ("Rain pool smoothness", Range(0,1)) = .94
        _WetReflectionGain ("Rain pool radiance response", Float) = 1
        _RippleAmplitude ("Metre-scale moving ripple", Float) = .004
        [HideInInspector] _HasMetallicMap ("Metal map present", Float) = 0
        [HideInInspector] _HasNormalMap ("Normal map present", Float) = 0
        [HideInInspector] _HasOcclusionMap ("Occlusion map present", Float) = 0
        [HideInInspector] _IsWater ("Physical water receiver", Float) = 0
        [HideInInspector] _Mode ("Original blend mode", Float) = 0
        [HideInInspector] _SrcBlend ("Original source blend", Float) = 1
        [HideInInspector] _DstBlend ("Original destination blend", Float) = 0
        [HideInInspector] _ZWrite ("Original depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows keepalpha
        #pragma target 3.0
        #include "UnityPBSLighting.cginc"
        sampler2D _MainTex, _BumpMap, _MetallicGlossMap, _OcclusionMap, _EmissionMap;
        fixed4 _Color;
        half4 _EmissionColor;
        half _BumpScale, _Metallic, _Glossiness, _GlossMapScale, _OcclusionStrength;
        half _WetFilmCoverage, _WetFilmSmoothness, _WetReflectionGain, _RippleAmplitude;
        half _HasMetallicMap, _HasNormalMap, _HasOcclusionMap, _IsWater;
        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
            float3 worldRefl;
            INTERNAL_DATA
        };
        half poolHash(float2 cell)
        {
            // Spatially fixed basin heights, never frame-varying screen grain.
            float3 mixed = frac(float3(cell.x, cell.y, cell.x) * .1031);
            mixed += dot(mixed, mixed.yzx + 33.33);
            return frac((mixed.x + mixed.y) * mixed.z);
        }
        half poolHeight(float2 position)
        {
            float2 cell = floor(position), blend = frac(position);
            blend = blend * blend * (3 - 2 * blend);
            return lerp(lerp(poolHash(cell), poolHash(cell + float2(1, 0)), blend.x),
                lerp(poolHash(cell + float2(0, 1)), poolHash(cell + 1), blend.x), blend.y);
        }
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 authored = tex2D(_MainTex, input.uv_MainTex) * _Color;
            output.Alpha = authored.a;
            // Standard's scanned surface maps share the base UV transform. Separate
            // surface-input transforms misregistered the normal/roughness/AO threefold
            // on asphalt, whose authored mainTextureScale is one repeat per 3 metres.
            half4 metal = tex2D(_MetallicGlossMap, input.uv_MainTex);
            output.Metallic = lerp(_Metallic, metal.r, _HasMetallicMap);
            half smoothness = lerp(_Glossiness, metal.a * _GlossMapScale, _HasMetallicMap);
            float2 metre = input.worldPos.xz;
            // Broad uneven rain pools follow world metres and the scanned roughness.
            // Dry aggregate stays rough; avoid turning the whole street into varnish.
            // Two smooth metre-space scales form irregular pools without a repeated
            // stripe lattice across wide streets. No extra textures or render targets.
            half basin = poolHeight(metre * .28) * .70
                + poolHeight(metre * .73 + float2(17.4, -8.6)) * .30;
            half film = lerp(saturate(_WetFilmCoverage * 1.65)
                * smoothstep(.30, .76, basin + smoothness * .24), 1, _IsWater);
            output.Albedo = authored.rgb * lerp(lerp(1, .88, film), 1, _IsWater);
            half drySmoothness = smoothness;
            output.Smoothness = lerp(drySmoothness, _WetFilmSmoothness, film);
            output.Occlusion = lerp(1, tex2D(_OcclusionMap, input.uv_MainTex).g,
                _HasOcclusionMap * _OcclusionStrength);
            half3 aggregate = normalize(lerp(half3(0,0,1),
                UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _BumpScale), _HasNormalMap));
            // A pool's surface sits above the coarse scan, so its moving highlights
            // remain visible even in the forward Web path without screen reflections.
            output.Normal = normalize(lerp(aggregate, half3(0,0,1), film * .92));
            half2 ripple = half2(sin(metre.x * 5.8 + metre.y * 3.7 + _Time.y * 1.8),
                sin(metre.y * 6.4 - metre.x * 2.9 - _Time.y * 1.4));
            output.Normal = normalize(output.Normal + half3(ripple * _RippleAmplitude * film, 0));
            half3 reflectionDirection = WorldReflectionVector(input, output.Normal);
            half4 encoded = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflectionDirection, (1 - output.Smoothness) * 6);
            half3 environment = DecodeHDR(encoded, unity_SpecCube0_HDR);
            half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.worldPos);
            half fresnel = .04 + .96 * pow(1 - saturate(dot(WorldNormalVector(input, output.Normal), viewDirection)), 5);
            // Standard already supplies the physical probe/specular response. Keep
            // the small grazing supplement bounded instead of adding a neon wash.
            output.Emission = tex2D(_EmissionMap, input.uv_MainTex).rgb * _EmissionColor.rgb
                + environment * fresnel * .04 * min(1, max(0, _WetReflectionGain - 1)) * film;
        }
        ENDCG
    }
    Fallback "Standard"
}
