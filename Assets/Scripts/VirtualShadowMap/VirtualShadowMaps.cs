using System;
using System.Collections.Generic;
using UnityEngine;

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
        /// Shadow Caster 着色器
        /// </summary>
        public Shader castShader;

        /// <summary>
        /// Tile纹理生成着色器
        /// </summary>
        public Shader drawTileShader;

        /// <summary>
        /// Tile纹理生成着色器
        /// </summary>
        public Shader drawDepthTileShader;

        /// <summary>
        /// Lookup纹理生成着色器
        /// </summary>
        public Shader drawLookupShader;

        /// <summary>
        /// 覆盖区域大小.
        /// </summary>
        [Space(10)]
        public int regionSize = 1024;

        /// <summary>
        /// 页表尺寸.
        /// </summary>
        [SerializeField]
        public int pageSize = 16;

        /// <summary>
        /// 最大mipmap等级
        /// </summary>
        [SerializeField]
        [Range(0, 8)]
        public int maxMipLevel = 4;

        /// <summary>
        /// 单个Tile的尺寸.
        /// </summary>
        [SerializeField]
        public int maxResolution = 1024;

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
        /// 用于流式加载的数据
        /// </summary>
        [Space(10)]
        public VirtualShadowData shadowData;

        /// <summary>
        /// 阴影渲染相机变换矩阵
        /// </summary>
        public Transform cameraTransform { get => m_CameraTransform; }

        /// <summary>
        /// 单个页表对应的世界尺寸.
        /// </summary>
        public float cellSize
        {
            get
            {
                return regionSize / (float)pageSize;
            }
        }

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

        public void OnEnable()
        {
            m_Light = GetComponent<Light>();
            m_LightTransform = m_Light.transform;

            InitShadowCamera();

#if UNITY_EDITOR
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

        public void Reset()
        {
            var bounds = VirtualShadowMapsUtilities.CalculateBoundingBox(this.GetRenderers());

            this.m_Camera = GetCamera();
            
            this.regionSize = Mathf.ClosestPowerOfTwo(Mathf.FloorToInt(Mathf.Max(bounds.size.x, bounds.size.z)));
            this.pageSize = this.regionSize / 128;
            this.maxMipLevel = 4;
            this.maxResolution = 1024;
            this.bias = 0.05f;
            this.normalBias = 0.4f;

            this.shadowData = null;

            this.castShader = Shader.Find("Hidden/StaticShadowMap/ShadowCaster");
            this.drawTileShader = Shader.Find("Hidden/Virtual Texture/Draw Tile");
            this.drawDepthTileShader = Shader.Find("Hidden/Virtual Texture/Draw Depth Tile");
            this.drawLookupShader = Shader.Find("Hidden/Virtual Texture/Draw Lookup");

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

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
            m_Camera.SetReplacementShader(castShader, "RenderType");

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

                m_CameraTexture = new RenderTexture(maxResolution, maxResolution, 16, format);
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
            if (m_CameraTexture)
            {
                if (m_Camera != null)
                    m_Camera.targetTexture = null;

                m_CameraTexture.Release();
                DestroyImmediate(m_CameraTexture);

                m_CameraTexture = null;
            }
        }

        public RenderTexture GetCameraTexture()
        {
            return m_CameraTexture;
        }

        public Light GetLight()
        {
            return m_Light;
        }

        public Transform GetLightTransform()
        {
            return m_LightTransform;
        }

        public List<MeshRenderer> GetRenderers()
        {
            var camera = GetCamera();
            var renderers = new List<MeshRenderer>();

            foreach (var renderer in GameObject.FindObjectsOfType<MeshRenderer>())
            {
                var layerTest = ((1 << renderer.gameObject.layer) & camera.cullingMask) > 0;
                if (renderer.gameObject.isStatic && layerTest)
                {
                    if (renderer.enabled)
                    {
                        if (renderer.TryGetComponent<MeshFilter>(out var meshFilter))
                        {
                            if (meshFilter.sharedMesh != null && renderer.sharedMaterial != null)
                                renderers.Add(renderer);
                        }
                    }
                }
            }

            return renderers;
        }

        public Bounds CalculateBoundingBox()
        {
            var regionRange = this.regionRange;
            var bounds = VirtualShadowMapsUtilities.CalculateBoundingBox(this.GetRenderers());
            var worldSize = new Vector3(regionRange.size.x, bounds.size.y, regionRange.size.y);

            return new Bounds(bounds.center, worldSize);
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

        public void OnValidate()
        {
            if (this.castShader == null)
                this.castShader = Shader.Find("Hidden/StaticShadowMap/ShadowCaster");
            if (this.drawTileShader == null)
                this.drawTileShader = Shader.Find("Hidden/Virtual Texture/Draw Tile");
            if (this.drawDepthTileShader == null)
                this.drawDepthTileShader = Shader.Find("Hidden/Virtual Texture/Draw Depth Tile");
            if (this.drawLookupShader == null)
                this.drawLookupShader = Shader.Find("Hidden/Virtual Texture/Draw Lookup");
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