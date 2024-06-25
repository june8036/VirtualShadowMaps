#ifndef UNITY_VIRTUAL_LIGHT_MAPS_INCLUDED
#define UNITY_VIRTUAL_LIGHT_MAPS_INCLUDED

#if defined(SHADER_API_D3D11) || defined(SHADER_API_PS4) || defined(SHADER_API_PS5) || defined(SHADER_API_XBOXONE)
#define USE_STRUCTURED_BUFFER_FOR_VIRTUAL_LIGHT_MAPS 1
#endif

// x = page size;
// y = 1.0f / page size
// z = page mip level
float4 _VirtualLightPageParams;

// x = region.x
// y = region.y
// z = 1.0f / region.width
// w = 1.0f / region.height
float4 _VirtualLightRegionParams;

// x = tile.tileSize
// y = tile.tilingCount
// z = tile.textureSize
// w = tile.paddingSize
float4 _VirtualLightTileParams;

// x = page size
// y = page size * tile size
// z = page mip level - 1
// w = mipmap bias
float4 _VirtualLightFeedbackParams;

float4 _VirtualLightTileTexture_TexelSize;

#if USE_STRUCTURED_BUFFER_FOR_VIRTUAL_LIGHT_MAPS
StructuredBuffer<float4> _VirtualLightMatrixs_SSBO;
#else
#define MAX_VISIBLE_SHADOW_UBO 64
float4 _VirtualLightMatrixs[MAX_VISIBLE_SHADOW_UBO];
#endif

TEXTURE2D(_VirtualLightLookupTexture);
SAMPLER(sampler_VirtualLightLookupTexture);

TEXTURE2D(_VirtualLightTileTexture);
SAMPLER(sampler_VirtualLightTileTexture);

int VirtualLightMaps_GetPageIndex(float2 page)
{
	return page.y * _VirtualLightTileParams.y + page.x;
}

float2 VirtualLightMaps_GetLookupCoord(float2 staticLightmapUV)
{
	return (staticLightmapUV.xy - _VirtualLightRegionParams.xy) * _VirtualLightRegionParams.zw;
}

float2 VirtualLightMaps_SampleLookupPage(float2 uv)
{
	float2 uvInt = uv - frac(uv * _VirtualLightPageParams.x) * _VirtualLightPageParams.y;
	float2 page = SAMPLE_TEXTURE2D_LOD(_VirtualLightLookupTexture, sampler_VirtualLightLookupTexture, uvInt, 0).xy;
	return page;
}

float2 VirtualLightMaps_ComputeTileCoordFromPage(float2 page, float2 offset)
{
	return (page.xy * _VirtualLightTileParams.x + offset * _VirtualLightTileParams.x) / _VirtualLightTileParams.z;
}

float2 VirtualLightMaps_GetVirtualTexcoord(float2 staticLightmapUV)
{
	float2 uv = VirtualLightMaps_GetLookupCoord(staticLightmapUV);
	float2 page = VirtualLightMaps_SampleLookupPage(uv);

#if USE_STRUCTURED_BUFFER_FOR_VIRTUAL_SHADOW_MAPS
	float4 crop = _VirtualLightMatrixs_SSBO[VirtualLightMaps_GetPageIndex(page)];
#else
	float4 crop = _VirtualLightMatrixs[VirtualLightMaps_GetPageIndex(page)];
#endif

	staticLightmapUV -= crop.xy;
	staticLightmapUV /= crop.zw;

#if UNITY_UV_STARTS_AT_TOP
	staticLightmapUV.y = 1 - staticLightmapUV.y;
#endif

	return VirtualLightMaps_ComputeTileCoordFromPage(page, staticLightmapUV);
}

float3 SampleVirtualLightMap(float2 staticLightmapUV)
{
	float2 coord = VirtualLightMaps_GetVirtualTexcoord(staticLightmapUV);
	return SAMPLE_TEXTURE2D_LOD(_VirtualLightTileTexture, sampler_VirtualLightTileTexture, coord.xy, 0);
}

#endif