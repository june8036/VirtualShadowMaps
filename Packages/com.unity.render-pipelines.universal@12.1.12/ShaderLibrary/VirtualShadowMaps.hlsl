#ifndef UNITY_VIRTUAL_SHADOW_MAPS_INCLUDED
#define UNITY_VIRTUAL_SHADOW_MAPS_INCLUDED

#if defined(SHADER_API_D3D11) || defined(SHADER_API_PS4) || defined(SHADER_API_PS5) || defined(SHADER_API_XBOXONE)
#define USE_STRUCTURED_BUFFER_FOR_VIRTUAL_SHADOW_MAPS 1
#endif

float _VirtualShadowMapEnable;

// x = page size;
// y = 1.0f / page size
// z = page mip level
float4 _VirtualShadowPageParams;

// x = region.x
// y = region.y
// z = 1.0f / region.width
// w = 1.0f / region.height
float4 _VirtualShadowRegionParams;

// x = tile.tileSize
// y = tile.tilingCount
// z = tile.textureSize
// w = tile.paddingSize
float4 _VirtualShadowTileParams;

// x = page size
// y = page size * tile size
// z = page mip level - 1
// w = mipmap bias
float4 _VirtualShadowFeedbackParams;

// x = depth bias
// y = normal bias
// z = light size
float4 _VirtualShadowBiasParams;

// x = search radius
// y = light size
float4 _VirtualShadowPcssParams;

// WorldToLocalMatrix
float4x4 _VirtualShadowLightMatrix;

// WorldToPorjiectionMatrix
float4x4 _VirtualShadowLightProjectionMatrix;

#if USE_STRUCTURED_BUFFER_FOR_VIRTUAL_SHADOW_MAPS
StructuredBuffer<float4x4> _VirtualShadowMatrixs_SSBO;
#else
#define MAX_VISIBLE_SHADOW_UBO 64
float4x4 _VirtualShadowMatrixs[MAX_VISIBLE_SHADOW_UBO];
#endif

TEXTURE2D(_VirtualShadowLookupTexture);
SAMPLER(sampler_VirtualShadowLookupTexture);

TEXTURE2D_SHADOW(_VirtualShadowTileTexture);
SAMPLER_CMP(sampler_VirtualShadowTileTexture);

float4 _VirtualShadowTileTexture_TexelSize;

SamplerState sampler_point_clamp;

#define VIRTUAL_SHADOW_MAPS_POISSON_COUNT 25

static const float2 Poisson[25] =
{
	float2(-0.978698, -0.0884121),
	float2(-0.841121, 0.521165),
	float2(-0.71746, -0.50322),
	float2(-0.702933, 0.903134),
	float2(-0.663198, 0.15482),
	float2(-0.495102, -0.232887),
	float2(-0.364238, -0.961791),
	float2(-0.345866, -0.564379),
	float2(-0.325663, 0.64037),
	float2(-0.182714, 0.321329),
	float2(-0.142613, -0.0227363),
	float2(-0.0564287, -0.36729),
	float2(-0.0185858, 0.918882),
	float2(0.0381787, -0.728996),
	float2(0.16599, 0.093112),
	float2(0.253639, 0.719535),
	float2(0.369549, -0.655019),
	float2(0.423627, 0.429975),
	float2(0.530747, -0.364971),
	float2(0.566027, -0.940489),
	float2(0.639332, 0.0284127),
	float2(0.652089, 0.669668),
	float2(0.773797, 0.345012),
	float2(0.968871, 0.840449),
	float2(0.991882, -0.657338)
};

int GetPageIndex(float4 page)
{
	return page.y * _VirtualShadowTileParams.y + page.x;
}

float4 ComputePageMipLevel(float2 uv)
{
	float2 page = floor(uv * _VirtualShadowFeedbackParams.x);

	float2 uvInt = uv * _VirtualShadowFeedbackParams.y;
	float2 dx = ddx(uvInt);
	float2 dy = ddy(uvInt);
	int mip = clamp(int(0.5 * log2(max(dot(dx, dx), dot(dy, dy))) + _VirtualShadowFeedbackParams.w + 0.5), 0, _VirtualShadowFeedbackParams.z);

	return float4(float3(page, mip), 1);
}

float2 ComputeLookupTexcoord(float3 worldPos)
{
	float3 localPos = mul(_VirtualShadowLightMatrix, worldPos);
	return (localPos.xy - _VirtualShadowRegionParams.xy) * _VirtualShadowRegionParams.zw;
}

float4 SampleLookupPage(float2 uv)
{
	float2 uvInt = uv - frac(uv * _VirtualShadowPageParams.x) * _VirtualShadowPageParams.y;
	float4 page = SAMPLE_TEXTURE2D_LOD(_VirtualShadowLookupTexture, sampler_VirtualShadowLookupTexture, uvInt, 0);
	return page;
}

float2 ComputeTileCoordFromPage(float2 page, float2 pageOffset)
{
	return (page.rg * (_VirtualShadowTileParams.x + _VirtualShadowTileParams.w * 2) + pageOffset * _VirtualShadowTileParams.x + _VirtualShadowTileParams.w) / _VirtualShadowTileParams.z;
}

float3 TransformWorldToShadowCoord(float3 worldPos, float4 page)
{
#if USE_STRUCTURED_BUFFER_FOR_VIRTUAL_SHADOW_MAPS
	float4 ndcpos = mul(_VirtualShadowMatrixs_SSBO[GetPageIndex(page)], float4(worldPos, 1));
#else
	float4 ndcpos = mul(_VirtualShadowMatrixs[GetPageIndex(page)], float4(worldPos, 1));
#endif

#if UNITY_UV_STARTS_AT_TOP
	ndcpos.y = 1 - ndcpos.y;
#endif

	return float3(ComputeTileCoordFromPage(page, ndcpos.xy / ndcpos.w), ndcpos.z);
}

float3 ApplyShadowBias(float3 positionWS, float3 normalWS, float4 page)
{
	Light light = GetMainLight();
	float scale = (1.0f - clamp(dot(normalWS, light.direction.xyz), 0.0f, 0.9f)) * max(1, page.w);
	positionWS = positionWS + light.direction.xyz * scale.xxx * _VirtualShadowBiasParams.x;
	positionWS = positionWS + normalWS * scale.xxx * _VirtualShadowBiasParams.y;

	return positionWS;
}

float3 GetVirtualShadowTexcoord(float3 positionWS, float3 normalWS)
{
	float2 uv = ComputeLookupTexcoord(positionWS);
	float4 page = SampleLookupPage(uv);

	return TransformWorldToShadowCoord(ApplyShadowBias(positionWS, normalWS, page), page);
}

inline float3 combineVirtualShadowcoordComponents(float2 baseUV, float2 deltaUV, float depth, float2 receiverPlaneDepthBias)
{
	float3 uv = float3(baseUV + deltaUV, depth);
	uv.z += dot(deltaUV, receiverPlaneDepthBias); // apply the depth bias
	return uv;
}

half SampleVirtualShadowMap_PCF3x3(float4 coord)
{
	const float2 offset = float2(0.5, 0.5);
	float2 uv = (coord.xy * _VirtualShadowTileTexture_TexelSize.zw) + offset;
	float2 base_uv = (floor(uv) - offset) * _VirtualShadowTileTexture_TexelSize.xy;
	float2 st = frac(uv);

	float2 uw = float2(3 - 2 * st.x, 1 + 2 * st.x);
	float2 u = float2((2 - st.x) / uw.x - 1, (st.x) / uw.y + 1);
	u *= _VirtualShadowTileTexture_TexelSize.x;

	float2 vw = float2(3 - 2 * st.y, 1 + 2 * st.y);
	float2 v = float2((2 - st.y) / vw.x - 1, (st.y) / vw.y + 1);
	v *= _VirtualShadowTileTexture_TexelSize.y;

	half shadow;
	half sum = 0;

	sum += uw[0] * vw[0] * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, float3(base_uv + float2(u[0], v[0]), coord.z));
	sum += uw[1] * vw[0] * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, float3(base_uv + float2(u[1], v[0]), coord.z));
	sum += uw[0] * vw[1] * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, float3(base_uv + float2(u[0], v[1]), coord.z));
	sum += uw[1] * vw[1] * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, float3(base_uv + float2(u[1], v[1]), coord.z));

	shadow = sum / 16.0f;

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
	return 1 - shadow;
#else
	return shadow;
#endif
}

float SampleVirtualShadowMap_PCF3x3(float4 coord, float2 receiverPlaneDepthBias)
{
	const float2 offset = float2(0.5, 0.5);
	float2 uv = (coord.xy * _VirtualShadowTileTexture_TexelSize.zw) + offset;
	float2 base_uv = (floor(uv) - offset) * _VirtualShadowTileTexture_TexelSize.xy;
	float2 st = frac(uv);

	float2 uw = float2(3 - 2 * st.x, 1 + 2 * st.x);
	float2 u = float2((2 - st.x) / uw.x - 1, (st.x) / uw.y + 1);
	u *= _VirtualShadowTileTexture_TexelSize.x;

	float2 vw = float2(3 - 2 * st.y, 1 + 2 * st.y);
	float2 v = float2((2 - st.y) / vw.x - 1, (st.y) / vw.y + 1);
	v *= _VirtualShadowTileTexture_TexelSize.y;

	float shadow;
	float sum = 0;

	sum += uw[0] * vw[0] * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u[0], v[0]), coord.z, receiverPlaneDepthBias));
	sum += uw[1] * vw[0] * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u[1], v[0]), coord.z, receiverPlaneDepthBias));
	sum += uw[0] * vw[1] * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u[0], v[1]), coord.z, receiverPlaneDepthBias));
	sum += uw[1] * vw[1] * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u[1], v[1]), coord.z, receiverPlaneDepthBias));

	shadow = sum / 16.0f;

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
	return 1 - shadow;
#else
	return shadow;
#endif
}

float SampleVirtualShadowMap_PCF5x5(float4 coord, float2 receiverPlaneDepthBias)
{
#if defined(SHADOWS_NATIVE)
	const float2 offset = float2(0.5, 0.5);
	float2 uv = (coord.xy * _VirtualShadowTileTexture_TexelSize.zw) + offset;
	float2 base_uv = (floor(uv) - offset) * _VirtualShadowTileTexture_TexelSize.xy;
	float2 st = frac(uv);

	float3 uw = float3(4 - 3 * st.x, 7, 1 + 3 * st.x);
	float3 u = float3((3 - 2 * st.x) / uw.x - 2, (3 + st.x) / uw.y, st.x / uw.z + 2);
	u *= _VirtualShadowTileTexture_TexelSize.x;

	float3 vw = float3(4 - 3 * st.y, 7, 1 + 3 * st.y);
	float3 v = float3((3 - 2 * st.y) / vw.x - 2, (3 + st.y) / vw.y, st.y / vw.z + 2);
	v *= _VirtualShadowTileTexture_TexelSize.y;

	float shadow = 0.0f;
	float sum = 0.0f;

	float3 accum = uw * vw.x;
	sum += accum.x * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.x, v.x), coord.z, receiverPlaneDepthBias));
	sum += accum.y * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.y, v.x), coord.z, receiverPlaneDepthBias));
	sum += accum.z * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.z, v.x), coord.z, receiverPlaneDepthBias));

	accum = uw * vw.y;
	sum += accum.x * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.x, v.y), coord.z, receiverPlaneDepthBias));
	sum += accum.y * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.y, v.y), coord.z, receiverPlaneDepthBias));
	sum += accum.z * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.z, v.y), coord.z, receiverPlaneDepthBias));

	accum = uw * vw.z;
	sum += accum.x * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.x, v.z), coord.z, receiverPlaneDepthBias));
	sum += accum.y * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.y, v.z), coord.z, receiverPlaneDepthBias));
	sum += accum.z * SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.z, v.z), coord.z, receiverPlaneDepthBias));

	shadow = sum / 144.0f;

#else // #if defined(SHADOWS_NATIVE)

	// when we don't have hardware PCF sampling, then the above 5x5 optimized PCF really does not work.
	// Fallback to a simple 3x3 sampling with averaged results.
	float shadow = 0;
	float2 base_uv = coord.xy;
	float2 ts = _VirtualShadowTileTexture_TexelSize.xy;
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(-ts.x, -ts.y), coord.z, receiverPlaneDepthBias));
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(0, -ts.y), coord.z, receiverPlaneDepthBias));
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(ts.x, -ts.y), coord.z, receiverPlaneDepthBias));
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(-ts.x, 0), coord.z, receiverPlaneDepthBias));
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(0, 0), coord.z, receiverPlaneDepthBias));
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(ts.x, 0), coord.z, receiverPlaneDepthBias));
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(-ts.x, ts.y), coord.z, receiverPlaneDepthBias));
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(0, ts.y), coord.z, receiverPlaneDepthBias));
	shadow += SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(ts.x, ts.y), coord.z, receiverPlaneDepthBias));
	shadow /= 9.0;

#endif // else of #if defined(SHADOWS_NATIVE)

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
	return 1 - shadow;
#else
	return shadow;
#endif
}

float InterleavedGradientNoise(float2 pos)
{
	static const float4x4 ditherPattern = float4x4(0.0, 0.5, 0.125, 0.625, 0.75, 0.22, 0.875, 0.375, 0.1875, 0.6875, 0.0625, 0.5625, 0.9375, 0.4375, 0.8125, 0.3125);
	float ditherValue = ditherPattern[pos.x * _ScreenParams.x % 4][pos.y * _ScreenParams.y % 4];
	return ditherValue * PI * 2;
}

float2 VirtialShadowMapsVogelDiskSampleDir(int sampleIndex, int samplesCount, float phi)
{
	//float phi = 3.14159265359f;//UNITY_PI;
	float GoldenAngle = 2.4f;

	//float r = sqrt(sampleIndex + 0.5f) / sqrt(samplesCount);
	float r = sqrt(sampleIndex + 0.5f) / sqrt(samplesCount);
	float theta = sampleIndex * GoldenAngle + phi;

	float sine, cosine;
	sincos(theta, sine, cosine);

	return float2(r * cosine, r * sine);
}

float2 VirtualShadowMapsBlockerSearch(float3 shadowCoord, float serachRadius, float2 rotation)
{
	float blockerSum = 0.0;
	float numBlockers = 0;

	UNITY_UNROLL
	for (int i = 0; i < VIRTUAL_SHADOW_MAPS_POISSON_COUNT; i++)
	{
		float2 pos = Poisson[i];
		float2 samples = float2(pos.x * rotation.x - pos.y * rotation.y, pos.y * rotation.x + pos.x * rotation.y);

		float2 offset = samples * serachRadius;
		float3 biasedCoords = float3(shadowCoord.xy + offset, shadowCoord.z);

		float shadowMapDepth = SAMPLE_TEXTURE2D_LOD(_VirtualShadowTileTexture, sampler_point_clamp, biasedCoords.xy, 0).r;

#if defined(UNITY_REVERSED_Z)
		float sum = shadowMapDepth >= biasedCoords.z;
		blockerSum += shadowMapDepth * sum;
		numBlockers += sum;
#else
		float sum = shadowMapDepth <= biasedCoords.z;
		blockerSum += shadowMapDepth * sum;
		numBlockers += sum;
#endif
	}

	float avgBlockerDepth = blockerSum / numBlockers;

#if defined(UNITY_REVERSED_Z)
	avgBlockerDepth = 1.0 - avgBlockerDepth;
#endif

	return float2(avgBlockerDepth, numBlockers);
}

float SampleVirtualShadowMap_PCSS(float3 positionWS, float3 normalWS, float angle)
{
	float2 uv = ComputeLookupTexcoord(positionWS);
	float4 page = SampleLookupPage(uv);

	float3 worldPos = ApplyShadowBias(positionWS, normalWS, page);
	float3 shadowCoord = TransformWorldToShadowCoord(worldPos, page);

	float2 rotation = float2(cos(angle), sin(angle));
	float2 blockerResults = VirtualShadowMapsBlockerSearch(shadowCoord, _VirtualShadowPcssParams.x / page.w, rotation);

	UNITY_BRANCH
	if (blockerResults.y < 0.1f)
		return 1.0;
	else if (blockerResults.y >= VIRTUAL_SHADOW_MAPS_POISSON_COUNT)
		return 0.0f;

	float total = 0;

#if defined(UNITY_REVERSED_Z)
	float penumbraRatio = ((1.0 - shadowCoord.z) - blockerResults.x) / (1 - blockerResults.x);
#else
	float penumbraRatio = (shadowCoord.z - blockerResults.x) / blockerResults.x;
#endif

#if USE_STRUCTURED_BUFFER_FOR_VIRTUAL_SHADOW_MAPS
	float4 ndcpos = mul(_VirtualShadowMatrixs_SSBO[0], float4(worldPos, 1));
#else
	float4 ndcpos = mul(_VirtualShadowMatrixs[0], float4(worldPos, 1));
#endif

	float filterSize = lerp(_VirtualShadowPcssParams.y, _VirtualShadowPcssParams.z, penumbraRatio);
	filterSize *= (_VirtualShadowPcssParams.x / page.w);

	UNITY_UNROLL
	for (int i = 0; i < VIRTUAL_SHADOW_MAPS_POISSON_COUNT; i++)
	{
		float2 pos = Poisson[i];
		float2 samples = float2(pos.x * rotation.x - pos.y * rotation.y, pos.y * rotation.x + pos.x * rotation.y);

		float2 offset = samples * filterSize;
		float3 biasedCoords = float3(shadowCoord.xy + offset, shadowCoord.z);

		float shadow = SAMPLE_TEXTURE2D_SHADOW(_VirtualShadowTileTexture, sampler_VirtualShadowTileTexture, biasedCoords);
		total += shadow;
	}

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
	return 1.0f - total / VIRTUAL_SHADOW_MAPS_POISSON_COUNT;
#else
	return total / VIRTUAL_SHADOW_MAPS_POISSON_COUNT;
#endif	
}

#endif