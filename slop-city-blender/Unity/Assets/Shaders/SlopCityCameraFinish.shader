Shader "Hidden/SlopCity/CameraFinish"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _CameraDepthNormalsTexture, _ContactTex, _BloomTex, _ReflectionTex, _CameraGBufferTexture1;
        float4 _MainTex_TexelSize, _ViewScale, _BlurDirection, _WetGroundBounds;
        float4x4 _SlopViewToWorld;
        float3 _EyeWorld, _HazeColor;
        float _Exposure, _BloomStrength, _BloomThreshold, _ReflectionStrength, _HeightHaze, _DeferredAvailable;
        half3 gradeCity(float3 color)
        {
            // Linear HDR probe/neon values can exceed 256. Squaring them in half
            // precision overflows before the shoulder compresses them for display.
            color = max(0, color * _Exposure);
            float3 filmic = saturate((color * (2.51 * color + .03)) / (color * (2.43 * color + .59) + .14));
            float peak = max(color.r, max(color.g, color.b));
            float shoulder = saturate((peak * (2.51 * peak + .03)) / (peak * (2.43 * peak + .59) + .14));
            float highlight = max(0, peak - 1);
            shoulder = lerp(shoulder, .804 + .196 * highlight / (8 + highlight), step(1, peak));
            // Compress HDR energy by one shared scale. Independent channel clipping
            // turns eight-times-brighter cyan/pink neon white and destroys its identity.
            float3 chromatic = color * shoulder / max(peak, .0001);
            float3 result = lerp(filmic, chromatic, saturate((peak - .8) * 1.25));
            float luminance = dot(result, float3(.2126, .7152, .0722));
            return lerp(luminance.xxx, result, .98);
        }
        float2 depthUV(float2 uv)
        {
            #if UNITY_UV_STARTS_AT_TOP
            if (_MainTex_TexelSize.y < 0) uv.y = 1 - uv.y;
            #endif
            return uv;
        }
        void readSurface(float2 uv, out float depth, out float3 normal)
        {
            DecodeDepthNormal(tex2Dlod(_CameraDepthNormalsTexture, float4(uv, 0, 0)), depth, normal);
            depth *= _ViewScale.z;
        }
        float3 positionAt(float2 uv, float depth)
        { return float3((uv * 2 - 1) * _ViewScale.xy * depth, -depth); }
        float wetReceiver(float2 uv, float depth, float3 normal)
        {
            float3 world = mul(_SlopViewToWorld, float4(positionAt(uv, depth), 1)).xyz;
            float3 worldNormal = mul((float3x3)_SlopViewToWorld, normal);
            // The road is at -0.10m. Sidewalks, habitat floorboards and venue
            // floors are above 0m and must not receive the wet-asphalt layer.
            return step(.65, worldNormal.y) * step(-.15, world.y) * step(world.y, -.055)
                * step(_WetGroundBounds.x, world.x) * step(world.x, _WetGroundBounds.y)
                * step(_WetGroundBounds.z, world.z) * step(world.z, _WetGroundBounds.w);
        }
        half4 contact(v2f_img input) : SV_Target
        {
            float2 uv = depthUV(input.uv);
            float depth; float3 normal; readSurface(uv, depth, normal);
            if (depth > _ViewScale.z * .98 || depth < .03) return half4(1, 1, 1, 1);
            float3 origin = positionAt(uv, depth);
            const float radius = 1.1;
            float2 sampleRadius = min(float2(.12, .12), radius / max(.1, depth) / (_ViewScale.xy * 2));
            float obscurance = 0;
            float rotate = frac(sin(dot(floor(uv / abs(_MainTex_TexelSize.xy)), float2(12.9898,78.233))) * 43758.5453) * 6.283185;
            [unroll] for (int sampleIndex = 0; sampleIndex < 16; sampleIndex++)
            {
                float angle = sampleIndex * 2.399963 + rotate;
                float ring = .12 + .88 * (sampleIndex + .5) / 16;
                float2 sampleUV = saturate(uv + float2(cos(angle), sin(angle)) * sampleRadius * ring);
                float otherDepth; float3 otherNormal; readSurface(sampleUV, otherDepth, otherNormal);
                float3 delta = positionAt(sampleUV, otherDepth) - origin;
                float distance = length(delta);
                float facing = max(0, dot(normal, delta / max(distance, .0001)) - .065);
                obscurance += facing * saturate(1 - distance / radius) * step(.025, distance);
            }
            float ao = max(.27, 1 - obscurance * _ViewScale.w * .31);
            return half4(ao, ao, ao, 1);
        }
        half4 bilateral(v2f_img input) : SV_Target
        {
            float centerDepth; float3 normal; readSurface(depthUV(input.uv), centerDepth, normal);
            float4 color = 0; float total = 0;
            [unroll] for (int offset = -2; offset <= 2; offset++)
            {
                float2 sampleUV = saturate(input.uv + _BlurDirection.xy * offset * 1.2);
                float otherDepth; float3 otherNormal; readSurface(depthUV(sampleUV), otherDepth, otherNormal);
                float weight = exp2(-abs(otherDepth-centerDepth) * 4 - offset*offset*.35)
                    * pow(saturate(dot(normal, otherNormal)), 4);
                color += tex2D(_MainTex, sampleUV) * weight; total += weight;
            }
            return color / max(total, .00001);
        }
        half4 reflectGround(v2f_img input) : SV_Target
        {
            float2 uv = depthUV(input.uv);
            float depth; float3 normal; readSurface(uv, depth, normal);
            if (depth < .05 || depth > 75 || _ReflectionStrength < .001) return 0;
            float3 origin = positionAt(uv, depth);
            if (wetReceiver(uv, depth, normal) < .5) return 0;
            // Rain film follows the street plane. Tracing every granular asphalt
            // normal as a perfect mirror produces disconnected sparkle clusters.
            normal = normalize(mul(float3(0, 1, 0), (float3x3)_SlopViewToWorld));
            float3 direction = normalize(reflect(normalize(origin), normal));
            float3 ray = origin + normal * .06;
            float travel = .18, previousTravel = 0, previousDifference = -1;
            float4 result = 0;
            [loop] for (int stepIndex = 0; stepIndex < 36; stepIndex++)
            {
                float3 rayPosition = ray + direction * travel;
                if (rayPosition.z > -.12) break;
                float2 location = (rayPosition.xy / -rayPosition.z / _ViewScale.xy) * .5 + .5;
                if (any(location < .012) || any(location > .988)) break;
                float sceneDepth; float3 sceneNormal; readSurface(location, sceneDepth, sceneNormal);
                float difference = -rayPosition.z - sceneDepth;
                if (difference > 0 && previousDifference <= 0 && travel > .5)
                {
                    float nearTravel = previousTravel, farTravel = travel;
                    [unroll] for (int refine = 0; refine < 8; refine++)
                    {
                        float midpoint = (nearTravel + farTravel) * .5;
                        float3 candidate = ray + direction * midpoint;
                        float2 candidateUV = (candidate.xy / -candidate.z / _ViewScale.xy) * .5 + .5;
                        float hitDepth; float3 hitNormal; readSurface(candidateUV, hitDepth, hitNormal);
                        if (-candidate.z > hitDepth) farTravel = midpoint; else nearTravel = midpoint;
                    }
                    float3 refined = ray + direction * farTravel;
                    location = (refined.xy / -refined.z / _ViewScale.xy) * .5 + .5;
                    float refinedDepth; float3 refinedNormal; readSurface(location, refinedDepth, refinedNormal);
                    // Coarse steps grow with distance. Test surface thickness only
                    // after refinement, or entire sections of reflected signs vanish.
                    float hitSeparation = -refined.z - refinedDepth;
                    if (hitSeparation < 0 || hitSeparation > .22)
                    {
                        previousDifference = difference; previousTravel = travel;
                        travel = travel * 1.155 + .22;
                        continue;
                    }
                    float edge = saturate(min(min(location.x,location.y),min(1-location.x,1-location.y)) * 12);
                    float fresnel = .16 + .84 * pow(1-saturate(dot(-normalize(origin),normal)), 4);
                    float smoothness = lerp(.4, tex2Dlod(_CameraGBufferTexture1,float4(uv,0,0)).a, _DeferredAvailable);
                    float confidence = edge * fresnel * saturate((80-depth)/30) * saturate(1-travel/90);
                    float coverage = confidence * smoothstep(.55,.88,smoothness);
                    result = float4(tex2Dlod(_MainTex, float4(depthUV(location),0,0)).rgb * coverage, coverage);
                    break;
                }
                previousDifference = difference;
                previousTravel = travel;
                travel = travel * 1.155 + .22;
            }
            return result;
        }
        half4 highlights(v2f_img input) : SV_Target
        {
            float2 delta = abs(_MainTex_TexelSize.xy) * .75;
            half3 color = (tex2D(_MainTex, input.uv + delta).rgb
                + tex2D(_MainTex, input.uv - delta).rgb
                + tex2D(_MainTex, input.uv + float2(delta.x, -delta.y)).rgb
                + tex2D(_MainTex, input.uv + float2(-delta.x, delta.y)).rgb) * .25;
            half brightness = max(color.r, max(color.g, color.b));
            half excess = max(0, brightness - _BloomThreshold);
            // Selected neon retains strong radiance. Bloom receives a soft,
            // hue-preserving energy bound so neighboring glyphs/windows stay distinct.
            half haloEnergy = 2.5 * excess / (2.5 + excess);
            return half4(color * haloEnergy / max(brightness, .0001), 1);
        }
        half4 blur(v2f_img input) : SV_Target
        {
            half3 color = tex2D(_MainTex, input.uv).rgb * .227027;
            color += tex2D(_MainTex, input.uv + _BlurDirection.xy * 1.384615).rgb * .316216;
            color += tex2D(_MainTex, input.uv - _BlurDirection.xy * 1.384615).rgb * .316216;
            color += tex2D(_MainTex, input.uv + _BlurDirection.xy * 3.230769).rgb * .070270;
            color += tex2D(_MainTex, input.uv - _BlurDirection.xy * 3.230769).rgb * .070270;
            return half4(color, 1);
        }
        half4 finish(v2f_img input) : SV_Target
        {
            half3 color = tex2D(_MainTex, input.uv).rgb;
            float ao = tex2D(_ContactTex, input.uv).r;
            // Emissive media and lamps retain their luminance instead of acquiring dirty halos.
            color *= lerp(ao, 1, saturate(max(color.r, max(color.g, color.b)) - .65));
            half4 reflection = tex2D(_ReflectionTex, input.uv);
            float depth; float3 normal; float2 uv = depthUV(input.uv); readSurface(uv, depth, normal);
            color += reflection.rgb * _ReflectionStrength * wetReceiver(uv, depth, normal);
            color += tex2D(_BloomTex, input.uv).rgb * _BloomStrength;
            if (depth < _ViewScale.z * .98)
            {
                float3 world = mul(_SlopViewToWorld, float4(positionAt(uv, depth), 1)).xyz;
                float averageHeight = max(0, (world.y + _EyeWorld.y) * .5);
                float density = _HeightHaze * exp(-averageHeight * .048);
                float haze = saturate(1 - exp(-max(0, depth-10) * density));
                color = lerp(color, _HazeColor, haze);
            }
            color = gradeCity(color);
            float2 centered = input.uv * 2 - 1;
            color *= 1 - .075 * saturate(dot(centered, centered) * .55);
            return half4(color, 1);
        }
        half4 reflectionBlur(v2f_img input) : SV_Target
        {
            float depth; float3 normal; readSurface(depthUV(input.uv), depth, normal);
            if (wetReceiver(depthUV(input.uv), depth, normal) < .5) return 0;
            half4 color = 0; float total = 0;
            [unroll] for (int offset = -2; offset <= 2; offset++)
            {
                float2 sampleUV = saturate(input.uv + _BlurDirection.xy * offset * .5);
                float otherDepth; float3 otherNormal; readSurface(depthUV(sampleUV), otherDepth, otherNormal);
                float weight = exp2(-abs(otherDepth-depth)*2-offset*offset*.5)
                    * wetReceiver(depthUV(sampleUV), otherDepth, otherNormal);
                color += tex2D(_MainTex, sampleUV) * weight; total += weight;
            }
            return color / max(total, .00001);
        }
        half luminanceOf(half3 color) { return dot(color, half3(.299,.587,.114)); }
        half4 antialias(v2f_img input) : SV_Target
        {
            float2 texel = abs(_MainTex_TexelSize.xy), uv = input.uv;
            half3 center = tex2D(_MainTex, uv).rgb;
            half nw = luminanceOf(tex2D(_MainTex,uv+float2(-1,-1)*texel).rgb);
            half ne = luminanceOf(tex2D(_MainTex,uv+float2(1,-1)*texel).rgb);
            half sw = luminanceOf(tex2D(_MainTex,uv+float2(-1,1)*texel).rgb);
            half se = luminanceOf(tex2D(_MainTex,uv+float2(1,1)*texel).rgb);
            half middle = luminanceOf(center);
            half lower = min(middle,min(min(nw,ne),min(sw,se))), upper = max(middle,max(max(nw,ne),max(sw,se)));
            if (upper-lower < max(.018,upper*.09)) return half4(center,1);
            float2 direction = float2(-((nw+ne)-(sw+se)),(nw+sw)-(ne+se));
            float reduce = max((nw+ne+sw+se)*.03125,.0078125);
            direction = clamp(direction / (min(abs(direction.x),abs(direction.y))+reduce),-6,6)*texel;
            half3 a = .5*(tex2D(_MainTex,uv+direction*(1.0/3-.5)).rgb+tex2D(_MainTex,uv+direction*(2.0/3-.5)).rgb);
            half3 b = a*.5+.25*(tex2D(_MainTex,uv-direction*.5).rgb+tex2D(_MainTex,uv+direction*.5).rgb);
            half lb = luminanceOf(b);
            return half4(lb < lower || lb > upper ? a : b,1);
        }
        // Forward profile has no depth buffers; the bloom input has a strict size cap.
        half4 browserFinish(v2f_img input) : SV_Target
        {
            half3 color = tex2D(_MainTex, input.uv).rgb;
            color += tex2D(_BloomTex, input.uv).rgb * _BloomStrength;
            color = gradeCity(color);
            float2 centered = input.uv * 2 - 1;
            color *= 1 - .075 * saturate(dot(centered, centered) * .55);
            return half4(color, 1);
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment contact
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment highlights
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment blur
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment finish
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment reflectGround
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment reflectionBlur
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment bilateral
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment antialias
            #pragma target 3.0
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment browserFinish
            #pragma target 3.0
        ENDCG }
    }
    Fallback Off
}
