Shader "Hidden/Virtual Texture/Feedback"
{
	CGINCLUDE
		#include "UnityCG.cginc"
   
		struct Attributes
		{
			float4 vertex	: POSITION;
			float2 uv		: TEXCOORD1;
		};

		struct Varyings
		{
			float3 worldPos : TEXCOORD0;
			float4 vertex	: SV_POSITION;
		};

		UNITY_DECLARE_TEX2D(_MainTex);

		Varyings vert (Attributes v)
		{
			Varyings o;
#if UNITY_UV_STARTS_AT_TOP
			v.uv.y = 1 - v.uv.y;
#endif
			o.vertex = float4(v.uv.xy * 2 - 1, 0, 1);
			o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
			return o;
		}
		
		float4 frag(Varyings i) : SV_Target
		{
			return float4(i.worldPos, 1);
		}
	ENDCG
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			ZTest Always ZWrite On
			Cull Off
		 
			CGPROGRAM

			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0

			#pragma vertex vert
			#pragma fragment frag

			ENDCG
		}
	}
}