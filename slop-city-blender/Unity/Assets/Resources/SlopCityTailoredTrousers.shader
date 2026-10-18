Shader "SlopCity/TailoredTrousers"
{
    Properties
    {
        _Color ("Cloth color", Color) = (1,1,1,1)
        _MainTex ("Cloth texture", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = .16
        [HideInInspector] _HipPose ("Hip sine/cosine", Vector) = (0,1,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        fixed4 _Color;
        half _Metallic, _Glossiness;
        UNITY_INSTANCING_BUFFER_START(Cloth)
            UNITY_DEFINE_INSTANCED_PROP(float4, _HipPose)
        UNITY_INSTANCING_BUFFER_END(Cloth)
        struct Input { float2 uv_MainTex; };
        float3 rotateHip(float3 v,float2 pose)
        {
            return float3(v.x,pose.y*v.y-pose.x*v.z,pose.x*v.y+pose.y*v.z);
        }
        void vert(inout appdata_full v)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            float4 pose=UNITY_ACCESS_INSTANCED_PROP(Cloth,_HipPose);
            float2 weights=v.texcoord1.xy;
            float rootWeight=1-weights.x-weights.y;
            float3 leftPivot=float3(-.096,.91,0),rightPivot=float3(.096,.91,0);
            v.vertex.xyz=v.vertex.xyz*rootWeight
                +(rotateHip(v.vertex.xyz-leftPivot,pose.xy)+leftPivot)*weights.x
                +(rotateHip(v.vertex.xyz-rightPivot,pose.zw)+rightPivot)*weights.y;
            v.normal=normalize(v.normal*rootWeight+rotateHip(v.normal,pose.xy)*weights.x+rotateHip(v.normal,pose.zw)*weights.y);
            v.tangent.xyz=normalize(v.tangent.xyz*rootWeight+rotateHip(v.tangent.xyz,pose.xy)*weights.x+rotateHip(v.tangent.xyz,pose.zw)*weights.y);
        }
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            fixed4 c=tex2D(_MainTex,IN.uv_MainTex)*_Color;
            o.Albedo=c.rgb;o.Alpha=c.a;o.Metallic=_Metallic;o.Smoothness=_Glossiness;
        }
        ENDCG
    }
    Fallback Off
}
