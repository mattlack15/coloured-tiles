Shader "FloatingTiles/ArenaStone"
{
    Properties { _Color ("Stone Colour", Color) = (.52,.54,.57,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct v2f { float4 position : SV_POSITION; float3 normal : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float shade = .4 + .6 * saturate(dot(normalize(i.normal), normalize(float3(-.4,.85,-.3))));
                return fixed4(_Color.rgb * shade, _Color.a);
            }
            ENDCG
        }
    }
}
