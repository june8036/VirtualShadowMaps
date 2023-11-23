Shader "Hidden/Virtual Texture/Draw Lookup"
{
	CGINCLUDE
		#include "UnityCG.cginc"

		float4 _TiledIndex;
		float4x4 _CropMatrix;

		struct Attributes
		{
			float4 positionOS   : POSITION;
			float2 uv           : TEXCOORD0;

			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct Varyings
		{
			float4 color		: TEXCOORD0;
			float4 positionCS	: SV_POSITION;

			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		Varyings LookupVertex(Attributes input)
		{
			Varyings output;

			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_TRANSFER_INSTANCE_ID(input, output);

			float2 pos = saturate(mul(unity_ObjectToWorld, input.positionOS).xy);
		#if UNITY_UV_STARTS_AT_TOP
			pos.y = 1 - pos.y;
		#endif

			output.positionCS = float4(pos * 2 - 1, 0, 1);

			return output;
		}

		float4 LookupFragment(Varyings input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);
			return _TiledIndex;
		}
	ENDCG
	SubShader
	{
		Pass
		{
			ZTest Always ZWrite Off
			Cull Off

			CGPROGRAM

			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0

			#pragma vertex LookupVertex
			#pragma fragment LookupFragment
			
			ENDCG
		}
	}
}
