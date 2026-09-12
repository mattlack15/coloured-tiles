Shader "FloatingTiles/Lava"
{
 Properties { _Color ("Molten Colour", Color) = (1,.19,.015,1) }
 SubShader {
 Tags { "RenderType"="Opaque" }
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _Color;
 struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; };
 v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz; return o; }
 float2 hash(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453); }
 fixed4 frag(v2f i):SV_Target {
 float2 p=i.world.xz*.62;
 p+=float2(sin(p.y*.6+_Time.y*.23),cos(p.x*.5-_Time.y*.19))*.32;
 float2 cell=floor(p), f=frac(p); float nearest=10, second=10;
 for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++) {
 float2 offset=float2(x,y), seed=hash(cell+offset);
 float2 d=offset+.5+.32*sin(_Time.y*.16+6.2831*seed)-f;
 float dist=dot(d,d); if(dist<nearest){second=nearest;nearest=dist;}else second=min(second,dist);
 }
 float edge=sqrt(second)-sqrt(nearest);
 float glow=1-smoothstep(.025,.16,edge);
 float core=1-smoothstep(.0,.035,edge);
 float pulse=.85+.15*sin(_Time.y*1.3+i.world.x*.35+i.world.z*.4);
 float3 crust=lerp(float3(.025,.008,.006),float3(.12,.025,.008),saturate(nearest));
 float3 lava=lerp(_Color.rgb,float3(1,.8,.22),core)*pulse;
 return float4(lerp(crust,lava,glow),1);
 }
 ENDCG
 }
 }
}
