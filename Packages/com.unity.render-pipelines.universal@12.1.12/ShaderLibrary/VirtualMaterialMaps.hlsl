#ifndef UNITY_VIRTUAL_MATERIAL_MAPS_INCLUDED
#define UNITY_VIRTUAL_MATERIAL_MAPS_INCLUDED

#if defined(SHADER_API_D3D11) || defined(SHADER_API_PS4) || defined(SHADER_API_PS5) || defined(SHADER_API_XBOXONE)
#define USE_STRUCTURED_BUFFER_FOR_VIRTUAL_MATERIAL_MAPS 1
#endif

// x = page size;
// y = 1.0f / page size
// z = page mip level
float4 _VirtualMaterialPageParams;

// x = region.x
// y = region.y
// z = 1.0f / region.width
// w = 1.0f / region.height
float4 _VirtualMaterialRegionParams;

// x = tile.tileSize
// y = tile.tilingCount
// z = tile.textureSize
// w = tile.paddingSize
float4 _VirtualMaterialTileParams;

// x = page size
// y = page size * tile size
// z = page mip level - 1
// w = mipmap bias
float4 _VirtualMaterialFeedbackParams;

float4 _VirtualMaterialTileTexture_TexelSize;

#if USE_STRUCTURED_BUFFER_FOR_VIRTUAL_MATERIAL_MAPS
StructuredBuffer<float4> _VirtualMaterialMatrixs_SSBO;
#else
#define MAX_VISIBLE_SHADOW_UBO 64
float4 _VirtualMaterialMatrixs[MAX_VISIBLE_SHADOW_UBO];
#endif

TEXTURE2D(_VirtualMaterialLookupTexture);
SAMPLER(sampler_VirtualMaterialLookupTexture);

TEXTURE2D(_VirtualMaterialTileTexture);
SAMPLER(sampler_VirtualMaterialTileTexture);

int VirtualMaterialMaps_GetPageIndex(float2 page)
{
	return page.y * _VirtualMaterialTileParams.y + page.x;
}

float2 VirtualMaterialMaps_GetLookupCoord(float2 staticLightmapUV)
{
	return (staticLightmapUV.xy - _VirtualMaterialRegionParams.xy) * _VirtualMaterialRegionParams.zw;
}

float4 VirtualMaterialMaps_SampleLookupPage(float2 uv)
{
	float2 uvInt = uv - frac(uv * _VirtualMaterialPageParams.x) * _VirtualMaterialPageParams.y;
	float4 page = SAMPLE_TEXTURE2D_LOD(_VirtualMaterialLookupTexture, sampler_VirtualMaterialLookupTexture, uvInt, 0);
	return page;
}

float2 VirtualMaterialMaps_ComputeTileCoordFromPage(float2 page, float2 offset)
{
	return (page.xy * _VirtualMaterialTileParams.x + offset * _VirtualMaterialTileParams.x) / _VirtualMaterialTileParams.z;
}

float2 VirtualMaterialMaps_GetVirtualTexcoord(float2 staticLightmapUV)
{
	float2 uv = VirtualMaterialMaps_GetLookupCoord(staticLightmapUV);
	float4 page = VirtualMaterialMaps_SampleLookupPage(uv);

#if USE_STRUCTURED_BUFFER_FOR_VIRTUAL_SHADOW_MAPS
	float4 crop = _VirtualMaterialMatrixs_SSBO[VirtualMaterialMaps_GetPageIndex(page.xy)];
#else
	float4 crop = _VirtualMaterialMatrixs[VirtualMaterialMaps_GetPageIndex(page.xy)];
#endif

	staticLightmapUV -= crop.xy;
	staticLightmapUV /= crop.zw;

#if UNITY_UV_STARTS_AT_TOP
	staticLightmapUV.y = 1 - staticLightmapUV.y;
#endif

	return VirtualMaterialMaps_ComputeTileCoordFromPage(page.xy, staticLightmapUV);
}

float3 SampleVirtualMaterialMap(float2 staticLightmapUV)
{
#if _VIRTUAL_MATERIAL_DEBUG
	float2 uv = VirtualMaterialMaps_GetLookupCoord(staticLightmapUV);
	float4 page = VirtualMaterialMaps_SampleLookupPage(uv);
	return page.xyz / _VirtualMaterialPageParams.z;
#else
	float2 coord = VirtualMaterialMaps_GetVirtualTexcoord(staticLightmapUV);
	return SAMPLE_TEXTURE2D_LOD(_VirtualMaterialTileTexture, sampler_VirtualMaterialTileTexture, coord.xy, 0);
#endif
}

#endif