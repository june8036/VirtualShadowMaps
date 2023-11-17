#ifndef UNITY_VIRTUAL_TEXTURE_INCLUDED
#define UNITY_VIRTUAL_TEXTURE_INCLUDED

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
float4 _VirtualShadowBiasParams;

StructuredBuffer<float4x4> _VirtualShadowMatrixs;

UNITY_DECLARE_TEX2D(_VirtualShadowLookupTexture);
UNITY_DECLARE_SHADOWMAP(_VirtualShadowTileTexture);

float4 _VirtualShadowTileTexture_TexelSize;

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
	return (worldPos.xz - _VirtualShadowRegionParams.xy) * _VirtualShadowRegionParams.zw;
}

float4 SampleLookupPage(float2 uv)
{
	float2 uvInt = uv - frac(uv * _VirtualShadowPageParams.x) * _VirtualShadowPageParams.y;
	float4 page = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_VirtualShadowLookupTexture, _VirtualShadowLookupTexture, uvInt, 0);
	return page;
}

float2 ComputeTileCoordFromPage(float2 page, float2 pageOffset)
{
	return (page.rg * (_VirtualShadowTileParams.x + _VirtualShadowTileParams.w * 2) + pageOffset * _VirtualShadowTileParams.x + _VirtualShadowTileParams.w) / _VirtualShadowTileParams.z;
}

float3 GetVirtualShadowTexcoord(float3 worldPos, float3 normalWS)
{
	float2 uv = ComputeLookupTexcoord(worldPos);
	float4 page = SampleLookupPage(uv);

	float scale = (1.0 - clamp(dot(normalWS, _WorldSpaceLightPos0.xyz), 0, 0.9f)) * page.w;
	worldPos = worldPos + _WorldSpaceLightPos0.xyz * scale.xxx * _VirtualShadowBiasParams.x;
	worldPos = worldPos + normalWS * scale.xxx * _VirtualShadowBiasParams.y;

	float4 ndcpos = mul(_VirtualShadowMatrixs[GetPageIndex(page)], float4(worldPos, 1));
#if UNITY_UV_STARTS_AT_TOP
	ndcpos.y = 1 - ndcpos.y;
#endif

	return float3(ComputeTileCoordFromPage(page, ndcpos.xy / ndcpos.w), ndcpos.z);
}

inline float3 combineVirtualShadowcoordComponents (float2 baseUV, float2 deltaUV, float depth, float2 receiverPlaneDepthBias)
{
	float3 uv = float3( baseUV + deltaUV, depth );
	uv.z += dot (deltaUV, receiverPlaneDepthBias); // apply the depth bias
	return uv;
}

half SampleVirtualShadowMap_PCF3x3(float4 coord, float2 receiverPlaneDepthBias)
{
	const float2 offset = float2(0.5,0.5);
	float2 uv = (coord.xy * _VirtualShadowTileTexture_TexelSize.zw) + offset;
	float2 base_uv = (floor(uv) - offset) * _VirtualShadowTileTexture_TexelSize.xy;
	float2 st = frac(uv);

	float2 uw = float2( 3-2*st.x, 1+2*st.x );
	float2 u = float2( (2-st.x) / uw.x - 1, (st.x)/uw.y + 1 );
	u *= _VirtualShadowTileTexture_TexelSize.x;

	float2 vw = float2( 3-2*st.y, 1+2*st.y );
	float2 v = float2( (2-st.y) / vw.x - 1, (st.y)/vw.y + 1);
	v *= _VirtualShadowTileTexture_TexelSize.y;

    half shadow;
	half sum = 0;

    sum += uw[0] * vw[0] * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u[0], v[0]), coord.z, receiverPlaneDepthBias));
    sum += uw[1] * vw[0] * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u[1], v[0]), coord.z, receiverPlaneDepthBias));
    sum += uw[0] * vw[1] * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u[0], v[1]), coord.z, receiverPlaneDepthBias));
    sum += uw[1] * vw[1] * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u[1], v[1]), coord.z, receiverPlaneDepthBias));

    shadow = sum / 16.0f;

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
	return 1 - shadow;
#else
	return shadow;
#endif
}

half SampleVirtualShadowMap_PCF5x5(float4 coord, float2 receiverPlaneDepthBias)
{ 
#if defined(SHADOWS_NATIVE)
	const float2 offset = float2(0.5,0.5);
	float2 uv = (coord.xy * _VirtualShadowTileTexture_TexelSize.zw) + offset;
	float2 base_uv = (floor(uv) - offset) * _VirtualShadowTileTexture_TexelSize.xy;
	float2 st = frac(uv);

	float3 uw = float3( 4-3*st.x, 7, 1+3*st.x );
	float3 u = float3( (3-2*st.x) / uw.x - 2, (3+st.x)/uw.y, st.x/uw.z + 2 );
	u *= _VirtualShadowTileTexture_TexelSize.x;

	float3 vw = float3( 4-3*st.y, 7, 1+3*st.y );
	float3 v = float3( (3-2*st.y) / vw.x - 2, (3+st.y)/vw.y, st.y/vw.z + 2 );
	v *= _VirtualShadowTileTexture_TexelSize.y;

	half shadow = 0.0f;
	half sum = 0.0f;

	half3 accum = uw * vw.x;
	sum += accum.x * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.x,v.x), coord.z, receiverPlaneDepthBias));
    sum += accum.y * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.y,v.x), coord.z, receiverPlaneDepthBias));
    sum += accum.z * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.z,v.x), coord.z, receiverPlaneDepthBias));

	accum = uw * vw.y;
    sum += accum.x *  UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.x,v.y), coord.z, receiverPlaneDepthBias));
    sum += accum.y *  UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.y,v.y), coord.z, receiverPlaneDepthBias));
    sum += accum.z *  UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.z,v.y), coord.z, receiverPlaneDepthBias));

	accum = uw * vw.z;
    sum += accum.x * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.x,v.z), coord.z, receiverPlaneDepthBias));
    sum += accum.y * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.y,v.z), coord.z, receiverPlaneDepthBias));
    sum += accum.z * UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(u.z,v.z), coord.z, receiverPlaneDepthBias));

    shadow = sum / 144.0f;

#else // #if defined(SHADOWS_NATIVE)

	// when we don't have hardware PCF sampling, then the above 5x5 optimized PCF really does not work.
	// Fallback to a simple 3x3 sampling with averaged results.
 	half shadow = 0;
	float2 base_uv = coord.xy;
	float2 ts = _VirtualShadowTileTexture_TexelSize.xy;
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(-ts.x,-ts.y), coord.z, receiverPlaneDepthBias));
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(    0,-ts.y), coord.z, receiverPlaneDepthBias));
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2( ts.x,-ts.y), coord.z, receiverPlaneDepthBias));
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(-ts.x,    0), coord.z, receiverPlaneDepthBias));
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(    0,    0), coord.z, receiverPlaneDepthBias));
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2( ts.x,    0), coord.z, receiverPlaneDepthBias));
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(-ts.x, ts.y), coord.z, receiverPlaneDepthBias));
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2(    0, ts.y), coord.z, receiverPlaneDepthBias));
	shadow += UNITY_SAMPLE_SHADOW(_VirtualShadowTileTexture, combineVirtualShadowcoordComponents(base_uv, float2( ts.x, ts.y), coord.z, receiverPlaneDepthBias));
	shadow /= 9.0;

#endif // else of #if defined(SHADOWS_NATIVE)

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
	return 1 - shadow;
#else
	return shadow;
#endif
}

#endif