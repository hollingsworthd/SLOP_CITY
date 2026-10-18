Shader "SlopCity/CloudedDusk"
{
    Properties
    {
        _Exposure ("Exposure", Float) = .9
        _Tint ("Tint", Color) = (1,1,1,1)
        _SunDirection ("Sun direction", Vector) = (.82,.24,-.51,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float _Exposure;
            float4 _Tint, _SunDirection;
            struct input { float4 vertex : POSITION; };
            struct output { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            output vert(input v)
            {
                output o; o.position = UnityObjectToClipPos(v.vertex); o.direction = v.vertex.xyz; return o;
            }
            float cloudHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            float cloudNoise(float2 p)
            {
                float2 cell = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(cloudHash(cell),cloudHash(cell+float2(1,0)),f.x),
                    lerp(cloudHash(cell+float2(0,1)),cloudHash(cell+float2(1,1)),f.x),f.y);
            }
            float cloud(float2 p)
            {
                float n = 0, amplitude = .52;
                [unroll] for (int i = 0; i < 5; i++)
                { n += cloudNoise(p) * amplitude; p = mul(float2x2(1.6,1.2,-1.2,1.6),p)+17.3; amplitude *= .5; }
                return n;
            }
            half4 frag(output i) : SV_Target
            {
                float3 ray = normalize(i.direction), sun = normalize(_SunDirection.xyz);
                float height = saturate(ray.y), facing = saturate(dot(ray,sun));
                float horizon = pow(1-height,3);
                float3 sky = lerp(float3(.12,.23,.37),float3(.57,.55,.48),horizon);
                sky += float3(.75,.27,.055)*pow(facing,8)*horizon;
                // A single inexpensive cloud plane creates coherent cloud banks with
                // soft billowed edges. No particles, extra textures or render targets.
                float2 p = ray.xz / max(.12, ray.y+.16) * 1.65 + float2(_Time.y*.002,0);
                float density = cloud(p);
                float cover = smoothstep(.34,.62,density)*smoothstep(-.015,.12,ray.y);
                float edge = saturate((.60-density)*5);
                float3 clouds = lerp(float3(.16,.20,.25),float3(.48,.49,.48),edge);
                clouds += float3(.80,.32,.095)*pow(facing,6)*edge;
                sky = lerp(sky,clouds,cover*.94);
                float disc = smoothstep(.99945,.9998,facing)*(1-cover);
                sky += float3(3,1.7,.65)*disc;
                sky = lerp(float3(.21,.25,.27),sky,smoothstep(-.08,.03,ray.y));
                return half4(sky*_Exposure*_Tint.rgb,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
