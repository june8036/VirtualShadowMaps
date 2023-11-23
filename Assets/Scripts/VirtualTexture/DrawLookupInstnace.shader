Shader "Virtual Texture/Draw Lookup"
{
	HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		UNITY_INSTANCING_BUFFER_START(LookupBlock)
		UNITY_DEFINE_INSTANCED_PROP(float4, _PageInfo)
		UNITY_DEFINE_INSTANCED_PROP(float4x4, _ImageMVP)
		UNITY_INSTANCING_BUFFER_END(LookupBlock)

		struct Attributes
		{
			float4 positionOS   : POSITION;
			float2 uv           : TEXCOORD0;

			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct Varyings
		{
			float4 color           : TEXCOORD0;
			float4 positionCS	   : SV_POSITION;
		};

		Varyings LookupVertex(Attributes input)
		{
			UNITY_SETUP_INSTANCE_ID(input);

			float4x4 mat = UNITY_MATRIX_M;
			mat = UNITY_ACCESS_INSTANCED_PROP(LookupBlock, _ImageMVP);

			float2 pos = saturate(mul(mat, input.positionOS).xy);
			pos.y = 1 - pos.y;

			Varyings output;
			output.positionCS = float4(pos * 2 - 1, 0, 1);
			output.color = UNITY_ACCESS_INSTANCED_PROP(LookupBlock, _PageInfo);

			return output;
		}

		half4 LookupFragment(Varyings input) : SV_Target
		{
			return input.color;
		}
	ENDHLSL
	SubShader
	{
		Pass
		{
			ZTest Always ZWrite Off 
			Cull Off

			HLSLPROGRAM

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

			#pragma vertex LookupVertex
			#pragma fragment LookupFragment
			
			#pragma multi_compile_instancing

			ENDHLSL
		}
	}
}
