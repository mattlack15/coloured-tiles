Shader "FloatingTiles/StarryNight"
{
 Properties { _Color ("Night Colour", Color) = (.006,.01,.025,1) }
 SubShader {
 Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
 Cull Off ZWrite Off
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _Color;
 struct v2f {float4 pos:SV_POSITION;float3 dir:TEXCOORD0;};
 v2f vert(appdata_base v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.dir=v.vertex.xyz;return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float stars(float2 uv,float density){
 float2 p=uv*density,c=floor(p),f=frac(p);
 float seed=hash(c);float2 center=.2+.6*float2(hash(c+15.7),hash(c+72.4));
 float dist=length(f-center);float aa=max(fwidth(dist),.002);
 float radius=lerp(.022,.075,seed);
 float spot=1-smoothstep(radius-aa,radius+aa,dist);
 return spot*step(.975,seed)*(.7+.3*sin(_Time.y*.6+seed*80));
 }
 fixed4 frag(v2f i):SV_Target{
 float3 d=normalize(i.dir);
 float2 uv=float2(atan2(d.z,d.x)/6.283185+.5,asin(clamp(d.y,-1,1))/3.141593+.5);
 float3 sky=_Color.rgb+float3(.008,.009,.018)*pow(1-abs(d.y),3);
 float s=stars(uv*float2(2,1),140)+stars(uv*float2(2,1)+.137,75)*.75;
 return float4(sky+s*float3(.78,.86,1),1);
 }
 ENDCG
 }
 }
}
