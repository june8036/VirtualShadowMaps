Shader "Hidden/Virtual Texture/Draw Tile"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	CGINCLUDE
		#include "UnityCG.cginc"
   
		struct Attributes
		{
			float4 vertex	: POSITION;
			float2 uv		: TEXCOORD0;
		};

		struct Varyings
		{
			float2 uv		: TEXCOORD0;
			float4 vertex	: SV_POSITION;
		};

		UNITY_DECLARE_TEX2D(_MainTex);

		Varyings vert (Attributes v)
		{
			Varyings o;
			o.vertex = UnityObjectToClipPos(v.vertex);
			o.uv = v.uv;
			return o;
		}
		
		float4 frag(Varyings i) : SV_TARGET
		{
			return UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MainTex, _MainTex, i.uv, 0).r;
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
