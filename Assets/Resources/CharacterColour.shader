Shader "FloatingTiles/CharacterColour"
{
    Properties { _Color ("Assigned Colour", Color) = (1,1,1,1) }
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
            struct v2f { float4 position : SV_POSITION; float3 normal : TEXCOORD0; float3 world : TEXCOORD1; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 light = normalize(float3(-.5,1,-.6));
                float3 view = normalize(_WorldSpaceCameraPos.xyz - i.world);
                float diffuse = .35 + .65 * saturate(dot(n,light));
                float silhouette = lerp(.65,1,smoothstep(0,.4,saturate(dot(n,view))));
                float highlight = .22 * pow(saturate(dot(n,normalize(light+view))),32);
                return fixed4(_Color.rgb * diffuse * silhouette + highlight, _Color.a);
            }
            ENDCG
        }
    }
}
