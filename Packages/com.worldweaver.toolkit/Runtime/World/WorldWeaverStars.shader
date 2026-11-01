Shader "WorldWeaver/Stars"
{
    Properties { _Color ("Color", Color) = (0.8,0.88,1,1) }
    SubShader
    {
        Tags { "Queue"="Background+1" "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            fixed4 _Color;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o; }
            fixed4 frag(v2f i):SV_Target
            { float radius=length(i.uv*2-1);clip(1-radius);float light=pow(saturate(1-radius),1.5);return fixed4(_Color.rgb*i.color.rgb*light,1); }
            ENDCG
        }
    }
}
