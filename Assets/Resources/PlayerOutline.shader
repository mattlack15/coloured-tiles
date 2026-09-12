Shader "FloatingTiles/PlayerOutline"
{
    Properties
    {
        _OutlineColor ("Outline Colour", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Float) = .045
    }
    SubShader
    {
        Tags { "Queue"="Geometry+1" "RenderType"="Opaque" }
        Pass
        {
            Cull Front
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _OutlineWidth;
            fixed4 _OutlineColor;
            struct v2f { float4 position : SV_POSITION; };
            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world += UnityObjectToWorldNormal(v.normal) * _OutlineWidth;
                o.position = UnityWorldToClipPos(world);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return _OutlineColor; }
            ENDCG
        }
    }
}
