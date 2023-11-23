Shader "Virtual Texture/Feedback"
{
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D_X(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _MainTex_TexelSize;

        struct Attributes
        {
            float4 positionOS   : POSITION;
            float2 uv           : TEXCOORD0;
            uint   id           : SV_VERTEXID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS    : SV_POSITION;
            float2 uv            : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float4 GetMaxFeedback(float2 uv, int count)
        {
            float4 col = float4(1, 1, 1, 1);

            for (int y = 0; y < count; y++)
            {
                for (int x = 0; x < count; x++)
                {
                    float4 col1 = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv + float2(_MainTex_TexelSize.x * x, _MainTex_TexelSize.y * y), 0);
                    col = lerp(col, col1, step(col1.b, col.b));
                }
            }

            return col;
        }

        Varyings FeedbackVertex(uint id : SV_VERTEXID)
        {
            Varyings o;
            o.uv = GetFullScreenTriangleTexCoord(id);
            o.positionCS = GetFullScreenTriangleVertexPosition(id);

            return o;
        }

        float4 FeedbackFragment(Varyings input) : SV_Target
        {
            return GetMaxFeedback(input.uv, 8);
        }

    ENDHLSL
    SubShader
    {
        Pass
        {
            Name "Feedback"
            Tags{"LightMode" = "Feedback"}

            ZWrite On

            HLSLPROGRAM

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            #pragma vertex FeedbackVertex
            #pragma fragment FeedbackFragment

            ENDHLSL
        }
    }
}