Shader "Hidden/StaticShadowMap/ShadowCaster"
{
	Properties
	{
		_MainTex("Albedo", 2D) = "white" {}
		_Cutoff("Cutoff", Range(0, 1)) = 0.01
	}
	CGINCLUDE
		#include "UnityCG.cginc"

		struct Attributes
		{
			float4 vertex : POSITION;
			float3 normal : NORMAL;
			float2 uv	  : TEXCOORD0;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct Varyings
		{   float4 vertex 	: SV_POSITION;
			float4 depth 	: TEXCOORD0;
			float2 uv	 	: TEXCOORD1;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		UNITY_DECLARE_TEX2D(_MainTex);

		uniform float _Cutoff;
		uniform float _ShadowMapBias;
		uniform float _ShadowMapNormalBias;

		float3 ApplyShadowBias(float3 positionWS, float3 normalWS, float3 lightDirection)
		{
		    float scale = 1.0 - clamp(dot(normalWS, lightDirection), 0, 0.9f);

		    // normal bias is negative since we want to apply an inset normal offset
		    positionWS = positionWS - lightDirection * _ShadowMapBias * scale.xxx;
		    positionWS = positionWS - normalWS * _ShadowMapNormalBias * scale.xxx;

		    return positionWS;
		}

		Varyings vert (Attributes input)
		{
			Varyings output;
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_TRANSFER_INSTANCE_ID(input, output);

			float4 worldPos = mul(unity_ObjectToWorld, input.vertex);
			float3 worldNormal = UnityObjectToWorldNormal(input.normal);

			output.vertex = mul(UNITY_MATRIX_VP, worldPos);
			output.uv = input.uv;
			output.depth = output.vertex;

			return output;
		}

		float frag (Varyings input) : SV_TARGET
		{
			UNITY_SETUP_INSTANCE_ID(input);

			float4 MainTex = UNITY_SAMPLE_TEX2D_SAMPLER(_MainTex, _MainTex, input.uv);
			clip(MainTex.a - _Cutoff);

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
			return 1 - (input.depth.z * 0.5f + 0.5f);
#else
			return input.depth.z;
#endif
		}
	ENDCG
	SubShader
	{
		LOD 100

		Tags { "RenderType" = "Opaque" }

		Pass
		{
			ZTest Lequal ZWrite On
			Cull Back

			CGPROGRAM
			#pragma prefer_hlslcc gles
			#pragma exclude_renderers d3d11_9x
			#pragma target 2.0

			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile_instancing
			ENDCG
		}
	}
}
