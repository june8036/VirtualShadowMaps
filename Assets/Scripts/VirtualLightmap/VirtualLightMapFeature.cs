using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VirtualTexture
{
    public sealed class VirtualLightMapFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private static readonly string m_VirtualLightMapsKeyword = "_VIRTUAL_LIGHT_MAPS";

        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private static GlobalKeyword m_VirtualLightMapsKeywordFeature;

        class VirtualLightPass : ScriptableRenderPass
        {
            public VirtualLightPass(RenderPassEvent passEvent)
            {
                this.renderPassEvent = passEvent;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get();
                cmd.Clear();

                var VirtualLightMaps = VirtualLightMapsManager.instance.First();
                if (VirtualLightMaps != null && VirtualLightMaps.enabled)
                {
                    if (VirtualLightMapsManager.instance.TryGetCamera(renderingData.cameraData.camera, out var VirtualLightCamera))
                    {
                        if (VirtualLightCamera.enabled)
                        {
                            cmd.EnableKeyword(m_VirtualLightMapsKeywordFeature);
                        }
                        else
                        {
                            cmd.DisableKeyword(m_VirtualLightMapsKeywordFeature);
                        }
                    }
                    else
                    {
                        cmd.DisableKeyword(m_VirtualLightMapsKeywordFeature);
                    }
                }
                else
                {
                    cmd.DisableKeyword(m_VirtualLightMapsKeywordFeature);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        VirtualLightPass m_VirtualLightPass;

        public override void Create()
        {
            m_VirtualLightMapsKeywordFeature = GlobalKeyword.Create(m_VirtualLightMapsKeyword);
            m_VirtualLightPass = new VirtualLightPass(RenderPassEvent.BeforeRendering);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.renderType != CameraRenderType.Overlay)
                renderer.EnqueuePass(m_VirtualLightPass);
        }
    }
}