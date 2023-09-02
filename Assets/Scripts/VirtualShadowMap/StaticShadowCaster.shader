Shader "Hidden/StaticShadowMap/ShadowCaster"
{
	CGINCLUDE
		#include "UnityCG.cginc"

		struct Attributes
		{
			float4 vertex : POSITION;
			float3 normal : NORMAL;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct Varyings
		{   float4 vertex 	: SV_POSITION;
			float4 depth 	: TEXCOORD0;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

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
			output.depth = output.vertex;

			return output;
		}

		float frag (Varyings input) : SV_TARGET
		{
			UNITY_SETUP_INSTANCE_ID(input);
			return input.depth.z;
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
