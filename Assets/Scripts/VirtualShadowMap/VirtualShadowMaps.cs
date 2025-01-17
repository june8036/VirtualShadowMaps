﻿using System;
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
    public class VirtualShadowMaps : MonoBehaviour
    {
        /// <summary>
        /// 光源组件
        /// </summary>
        private Light m_Light;

        /// <summary>
        /// 光源的变换矩阵
        /// </summary>
        private Transform m_LightTransform;

        /// <summary>
        /// 阴影渲染相机对象
        /// </summary>
        private GameObject m_CameraGO;

        /// <summary>
        /// 阴影渲染相机
        /// </summary>
        private Camera m_Camera;

        /// <summary>
        /// 阴影渲染相机变换矩阵
        /// </summary>
        private Transform m_CameraTransform;

        /// <summary>
        /// 渲染单个ShadowMap需要的纹理
        /// </summary>
		private RenderTexture m_CameraTexture;

        /// <summary>
        /// Shadow Caster 材质
        /// </summary>
        public Material castMaterial;

        /// <summary>
        /// Tile纹理生成材质
        /// </summary>
        public Material drawTileMaterial;

        /// <summary>
        /// Lookup纹理生成材质
        /// </summary>
        public Material drawLookupMaterial;

        /// <summary>
        /// 计算最小最大的深度信息
        /// </summary>
        public ComputeShader minMaxDpethCompute;

        /// <summary>
        /// 覆盖区域大小.
        /// </summary>
        [Space(10)]
        public int regionSize = 1024;

        /// <summary>
        /// 覆盖区域中心.
        /// </summary>
        public Vector3 regionCenter = Vector3.zero;

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
        /// Depth Bias
        /// </summary>
        [Space(10)]
        [Range(0, 3)]
        public float bias = 0.05f;

        /// <summary>
        /// Normal Bias
        /// </summary>
        [Range(0, 3)]
        public float normalBias = 0.4f;

        /// <summary>
        /// PCSS enable
        /// </summary>
        [Space(10)]
        public bool pcssFilter = true;

        /// <summary>
        /// PCSS filter radius
        /// </summary>
        [Range(0, 1)]
        public float softnesss = 0.5f;

        /// <summary>
        /// PCSS filter near
        /// </summary>
        [Range(0, 2)]
        public float softnessNear = 0.2f;

        /// <summary>
        /// PCSS filter far
        /// </summary>
        [Range(0, 2)]
        public float softnessFar = 1.0f;

        /// <summary>
        /// 用于流式加载的数据
        /// </summary>
        [Space(10)]
        public VirtualShadowData shadowData;

        /// <summary>
        /// 阴影渲染相机变换矩阵
        /// </summary>
        public Transform cameraTransform { get => m_CameraTransform; }

        /// <summary>
        /// 页表尺寸.
        /// </summary>
        public int pageSize { get => 1 << (maxMipLevel - 1); }

        /// <summary>
        /// 单个页表对应的世界尺寸.
        /// </summary>
        public float cellSize { get => regionSize / (float)pageSize; }

        /// <summary>
        /// 页表对应的世界区域.
        /// </summary>
        public Rect regionRange
        {
            get
            {
                return new Rect(-regionSize / 2, -regionSize / 2, regionSize, regionSize);
            }
        }

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
                     deviceType == GraphicsDeviceType.XboxOne ||
                     deviceType == GraphicsDeviceType.GameCoreXboxOne ||
                     deviceType == GraphicsDeviceType.GameCoreXboxSeries);
            }
        }

        public static int maxUniformBufferSize { get => 64; }

        public void OnEnable()
        {
            m_Light = GetComponent<Light>();
            m_LightTransform = m_Light.transform;

#if UNITY_EDITOR
            InitShadowCamera();

            foreach (var cam in SceneView.GetAllSceneCameras())
            {
                if (cam.cameraType == CameraType.SceneView)
                {
                    if (cam.gameObject.TryGetComponent<VirtualShadowCamera>(out var virtualShadowCamera))
                        virtualShadowCamera.enabled = true;
                    else
                        cam.gameObject.AddComponent<VirtualShadowCamera>();
                }
            }
#endif

            VirtualShadowManager.instance.Register(this);
        }

        public void OnDisable()
        {
            DestroyShadowCamera();

#if UNITY_EDITOR
            foreach (var cam in SceneView.GetAllSceneCameras())
            {
                if (cam.cameraType == CameraType.SceneView)
                {
                    if (cam.gameObject.TryGetComponent<VirtualShadowCamera>(out var virtualShadowCamera))
                        virtualShadowCamera.enabled = false;
                }
            }
#endif

            VirtualShadowManager.instance.Unregister(this);
        }

        public void OnDestroy()
        {
            DestroyCameraTexture();
        }


#if UNITY_EDITOR
        public void Refresh()
        {
            foreach (var cam in SceneView.GetAllSceneCameras())
            {
                if (cam.cameraType == CameraType.SceneView)
                {
                    if (cam.gameObject.TryGetComponent<VirtualShadowCamera>(out var virtualShadowCamera))
                        virtualShadowCamera.ResetShadowMaps();
                }
            }
        }
#endif

        private void InitShadowCamera()
        {
            m_CameraGO = new GameObject("", typeof(Camera));
            m_CameraGO.name = "VirtualShadowCamera" + m_CameraGO.GetInstanceID().ToString();
            m_CameraGO.hideFlags = HideFlags.HideAndDontSave;
            m_CameraGO.transform.parent = this.transform;

            m_Camera = m_CameraGO.GetComponent<Camera>();
            m_Camera.enabled = false;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;
            m_Camera.orthographic = true;
            m_Camera.renderingPath = RenderingPath.Forward;
            m_Camera.targetTexture = m_CameraTexture;
            m_Camera.allowHDR = false;
            m_Camera.allowMSAA = false;
            m_Camera.allowDynamicResolution = false;
            m_Camera.aspect = 1.0f;
            m_Camera.useOcclusionCulling = false;
            m_Camera.SetReplacementShader(castMaterial.shader, "RenderType");

            m_CameraTransform = m_Camera.GetComponent<Transform>();
            m_CameraTransform.localRotation = Quaternion.identity;
            m_CameraTransform.localScale = Vector3.one;
        }

        private void DestroyShadowCamera()
        {
            if (m_CameraGO != null)
            {
                if (Application.isEditor)
                    DestroyImmediate(m_CameraGO);
                else
                    Destroy(m_CameraGO);

                m_CameraGO = null;
                m_Camera = null;
                m_CameraTransform = null;
            }

            DestroyCameraTexture();
        }

        public Camera GetCamera()
        {
            if (m_Camera == null)
                InitShadowCamera();

            return m_Camera;
        }

        public void CreateCameraTexture(RenderTextureFormat format = RenderTextureFormat.Shadowmap)
        {
            if (m_CameraTexture == null || (m_CameraTexture != null && m_CameraTexture.format != format))
            {
                if (m_CameraTexture != null)
                    m_CameraTexture.Release();

                m_CameraTexture = RenderTexture.GetTemporary(maxResolution.ToInt(), maxResolution.ToInt(), 16, format);
                m_CameraTexture.name = "StaticShadowMap";
                m_CameraTexture.useMipMap = false;
                m_CameraTexture.autoGenerateMips = false;
                m_CameraTexture.filterMode = FilterMode.Point;
                m_CameraTexture.wrapMode = TextureWrapMode.Clamp;
            }

            m_Camera.targetTexture = m_CameraTexture;
        }

        public void DestroyCameraTexture()
        {
            if (m_CameraTexture != null)
            {
                if (m_Camera != null)
                    m_Camera.targetTexture = null;

                RenderTexture.ReleaseTemporary(m_CameraTexture);
                m_CameraTexture = null;
            }
        }

        public RenderTexture GetCameraTexture()
        {
            return m_CameraTexture;
        }

        public Transform GetCameraTransform()
        {
            return m_CameraTransform;
        }

        public Light GetLight()
        {
            return m_Light;
        }

        public Transform GetLightTransform()
        {
            return m_LightTransform;
        }

        public List<Renderer> GetRenderers()
        {
            var camera = GetCamera();
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

                        var isCastShadow = renderer.gameObject.isStatic;
                        if (renderer.TryGetComponent<VirtualShadowCaster>(out var virtualShadowCaster))
                            isCastShadow = virtualShadowCaster.castShadow;

                        var layerTest = ((1 << renderer.gameObject.layer) & camera.cullingMask) > 0;
                        if (isCastShadow && layerTest)
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
                var layerTest = ((1 << renderer.gameObject.layer) & camera.cullingMask) > 0;
                if (renderer.enabled && layerTest)
                {
                    var isCastShadow = renderer.gameObject.isStatic;
                    if (renderer.TryGetComponent<VirtualShadowCaster>(out var virtualShadowCaster))
                        isCastShadow = virtualShadowCaster.castShadow;

                    if (isCastShadow)
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

        public void CalculateRegionBox()
        {
            var bounds = VirtualShadowMapsUtilities.CalculateBoundingBox(this.GetRenderers());
            this.regionCenter = bounds.center;
            this.regionSize = Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Max(bounds.size.x, bounds.size.z)));
        }

        public Bounds CalculateBoundingBox()
        {
            var regionRange = this.regionRange;
            var bounds = VirtualShadowMapsUtilities.CalculateBoundingBox(this.GetRenderers());
            var worldSize = new Vector3(regionRange.size.x, bounds.size.y, regionRange.size.y);

            return new Bounds(regionCenter, worldSize);
        }

#if UNITY_EDITOR
        public void OnRenderObject()
        {
            var camera = Camera.current;
            if (camera != null)
            {
                if (camera.cameraType == CameraType.SceneView)
                {
                    if (!camera.TryGetComponent<VirtualShadowCamera>(out var virtualShadowCamera))
                        camera.gameObject.AddComponent<VirtualShadowCamera>();
                }
            }
        }

        public void Reset()
        {
            this.m_Camera = GetCamera();

            this.maxMipLevel = 4;
            this.maxResolution = ShadowResolution._1024;
            this.bias = 0.05f;
            this.normalBias = 0.4f;
            this.shadowData = null;

            var castMaterialPath = AssetDatabase.GUIDToAssetPath("3de651371e844a740acfc6662403e39d");
            var drawTileMaterialPath = AssetDatabase.GUIDToAssetPath("e21bfd4f15c8c984db63c19afcbfbf14");
            var drawLookupMaterialPath = AssetDatabase.GUIDToAssetPath("e4fe469a756b64248b416a5c9091c426");
            var minMaxDepthPath = AssetDatabase.GUIDToAssetPath("329d26da45b19d245a3024f2ca7848cc");

            this.castMaterial = AssetDatabase.LoadAssetAtPath<Material>(castMaterialPath);
            this.drawTileMaterial = AssetDatabase.LoadAssetAtPath<Material>(drawTileMaterialPath);
            this.drawLookupMaterial = AssetDatabase.LoadAssetAtPath<Material>(drawLookupMaterialPath);
            this.minMaxDpethCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(minMaxDepthPath);

            this.CalculateRegionBox();

            EditorUtility.SetDirty(this);
        }

        public void OnValidate()
        {
            if (this.castMaterial == null)
            {
                var castMaterialPath = AssetDatabase.GUIDToAssetPath("3de651371e844a740acfc6662403e39d");
                this.castMaterial = AssetDatabase.LoadAssetAtPath<Material>(castMaterialPath);
            }

            if (this.drawTileMaterial == null)
            {
                var drawTileMaterialPath = AssetDatabase.GUIDToAssetPath("e21bfd4f15c8c984db63c19afcbfbf14");
                this.drawTileMaterial = AssetDatabase.LoadAssetAtPath<Material>(drawTileMaterialPath);
            }

            if (this.drawLookupMaterial == null)
            {
                var drawLookupMaterialPath = AssetDatabase.GUIDToAssetPath("e4fe469a756b64248b416a5c9091c426");
                this.drawLookupMaterial = AssetDatabase.LoadAssetAtPath<Material>(drawLookupMaterialPath);
            }

            if (this.minMaxDpethCompute == null)
            {
                var minMaxDepthPath = AssetDatabase.GUIDToAssetPath("329d26da45b19d245a3024f2ca7848cc");
                this.minMaxDpethCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(minMaxDepthPath);
            }
        }

        public void OnDrawGizmos()
        {
            if (Selection.activeGameObject == this.gameObject)
            {
                if (shadowData != null)
                {
                    Gizmos.matrix = Matrix4x4.identity;
                    Gizmos.color = new Color(0.0f, 0.5f, 1.0f, 0.2f);
                    Gizmos.DrawCube(shadowData.bounds.center, shadowData.bounds.size);
                    Gizmos.color = new Color(0.0f, 0.5f, 1.0f, 0.5f);
                    Gizmos.DrawWireCube(shadowData.bounds.center, shadowData.bounds.size);
                }
            }
        }
#endif
    }
}