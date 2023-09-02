Shader "Hidden/Virtual Texture/Draw Lookup"
{
	CGINCLUDE
		#include "UnityCG.cginc"

		UNITY_INSTANCING_BUFFER_START(LookupBlock)
			UNITY_DEFINE_INSTANCED_PROP(float4, _TiledIndex)
			UNITY_DEFINE_INSTANCED_PROP(float4x4, _CropMatrix)
		UNITY_INSTANCING_BUFFER_END(LookupBlock)

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

			float4x4 mat = UNITY_MATRIX_M;
			mat = UNITY_ACCESS_INSTANCED_PROP(LookupBlock, _CropMatrix);

			float2 pos = saturate(mul(mat, input.positionOS).xy);
			pos.y = 1 - pos.y;

			output.positionCS = float4(pos * 2 - 1, 0, 1);
			output.color = UNITY_ACCESS_INSTANCED_PROP(LookupBlock, _TiledIndex);

			return output;
		}

		half4 LookupFragment(Varyings input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);
			return input.color;
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
			
			#pragma multi_compile_instancing

			ENDCG
		}
	}
}
