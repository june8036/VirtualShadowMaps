using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VirtualTexture
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public class VirtualMaterialMaps : MonoBehaviour
    {
        /// <summary>
        /// Feedback材质
        /// </summary>
        public Material feedbackMaterial;

        /// <summary>
        /// Tile纹理生成材质
        /// </summary>
        public Material drawTileMaterial;

        /// <summary>
        /// Lookup纹理生成材质
        /// </summary>
        public Material drawLookupMaterial;

        /// <summary>
        /// 最大mipmap等级
        /// </summary>
        [Space(10)]
        [Range(1, 8)]
        public int maxMipLevel = 4;

        /// <summary>
        /// 单个Tile的尺寸.
        /// </summary>
        public ShadowResolution maxResolution = ShadowResolution._1024;

        /// <summary>
        /// 页表尺寸.
        /// </summary>
        public int pageSize { get => 1 << (maxMipLevel - 1); }

        /// <summary>
        /// 用于流式加载的数据
        /// </summary>
        [Space(10)]
        public VirtualMaterialMapData lightData;

        public static bool useStructuredBuffer
        {
            get
            {
                GraphicsDeviceType deviceType = SystemInfo.graphicsDeviceType;
                return !Application.isMobilePlatform &&
                    (deviceType == GraphicsDeviceType.Direct3D11 ||
                     deviceType == GraphicsDeviceType.Direct3D12 ||
                     deviceType == GraphicsDeviceType.PlayStation4 ||
                     deviceType == GraphicsDeviceType.PlayStation5 ||
                     deviceType == GraphicsDeviceType.XboxOne);
            }
        }

        public static int maxUniformBufferSize { get => 64; }

        public void OnEnable()
        {
#if UNITY_EDITOR
            foreach (var cam in SceneView.GetAllSceneCameras())
            {
                if (cam.cameraType == CameraType.SceneView)
                {
                    if (cam.gameObject.TryGetComponent<VirtualMaterialMapCamera>(out var virtualLightMapCamera))
                        virtualLightMapCamera.enabled = true;
                    else
                        cam.gameObject.AddComponent<VirtualMaterialMapCamera>();
                }
            }
#endif

            VirtualMaterialMapsManager.instance.Register(this);
        }

        public void OnDisable()
        {
#if UNITY_EDITOR
            foreach (var cam in SceneView.GetAllSceneCameras())
            {
                if (cam.cameraType == CameraType.SceneView)
                {
                    if (cam.gameObject.TryGetComponent<VirtualMaterialMapCamera>(out var virtualLightMapCamera))
                        virtualLightMapCamera.enabled = false;
                }
            }
#endif

            VirtualMaterialMapsManager.instance.Unregister(this);
        }

#if UNITY_EDITOR
        public void Refresh()
        {
            foreach (var cam in SceneView.GetAllSceneCameras())
            {
                if (cam.cameraType == CameraType.SceneView)
                {
                    if (cam.gameObject.TryGetComponent<VirtualMaterialMapCamera>(out var virtualLightMapCamera))
                        virtualLightMapCamera.Rebuild();
                }
            }
        }
#endif

        public List<Renderer> GetRenderers()
        {
            var renderers = new List<Renderer>();
            var allRenderers = new List<Renderer>();

            foreach (var lodGroup in GameObject.FindObjectsOfType<LODGroup>())
            {
                foreach (var lod in lodGroup.GetLODs())
                {
                    foreach (var renderer in lod.renderers)
                    {
                        if (renderer != null)
                            allRenderers.Add(renderer);
                    }
                }
            }

            foreach (var lodGroup in GameObject.FindObjectsOfType<LODGroup>())
            {
                var lods = lodGroup.GetLODs();
                if (lods.Length > 0)
                {
                    foreach (var renderer in lods[0].renderers)
                    {
                        if (renderer == null)
                            continue;

                        if (renderer.gameObject.isStatic)
                        {
                            if (renderer.TryGetComponent<MeshFilter>(out var meshFilter))
                            {
                                if (meshFilter.sharedMesh != null && renderer.sharedMaterial != null)
                                    renderers.Add(renderer);
                            }
                        }
                    }
                }
            }

            foreach (var renderer in GameObject.FindObjectsOfType<MeshRenderer>())
            {
                if (renderer.enabled)
                {
                    if (renderer.gameObject.isStatic)
                    {
                        if (renderer.TryGetComponent<MeshFilter>(out var meshFilter))
                        {
                            if (meshFilter.sharedMesh != null && renderer.sharedMaterial != null)
                            {
                                if (!allRenderers.Contains(renderer))
                                    renderers.Add(renderer);
                            }
                        }
                    }
                }
            }

            return renderers;
        }
    }
}