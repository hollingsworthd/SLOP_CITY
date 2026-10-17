Shader "SlopCity/WorldText"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZTest LEqual
        ZWrite Off
        Cull Back
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(1)
            };
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.vertex);
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = input.color;
                color.a *= tex2D(_MainTex, input.uv).a;
                UNITY_APPLY_FOG(input.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
