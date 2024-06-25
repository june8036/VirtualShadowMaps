using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VirtualTexture
{
    public sealed class VirtualMaterialMapFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private static readonly string m_VirtualMaterialMapsKeyword = "_VIRTUAL_MATERIAL_MAPS";

        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private static GlobalKeyword m_VirtualMaterialMapsKeywordFeature;

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

                var virtualMaterialMaps = VirtualMaterialMapsManager.instance.First();
                if (virtualMaterialMaps != null && virtualMaterialMaps.enabled)
                {
                    if (VirtualMaterialMapsManager.instance.TryGetCamera(renderingData.cameraData.camera, out var VirtualLightCamera))
                    {
                        if (VirtualLightCamera.enabled)
                        {
                            cmd.EnableKeyword(m_VirtualMaterialMapsKeywordFeature);
                        }
                        else
                        {
                            cmd.DisableKeyword(m_VirtualMaterialMapsKeywordFeature);
                        }
                    }
                    else
                    {
                        cmd.DisableKeyword(m_VirtualMaterialMapsKeywordFeature);
                    }
                }
                else
                {
                    cmd.DisableKeyword(m_VirtualMaterialMapsKeywordFeature);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        VirtualLightPass m_VirtualMaterialPass;

        public override void Create()
        {
            m_VirtualMaterialMapsKeywordFeature = GlobalKeyword.Create(m_VirtualMaterialMapsKeyword);
            m_VirtualMaterialPass = new VirtualLightPass(RenderPassEvent.BeforeRendering);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.renderType != CameraRenderType.Overlay)
                renderer.EnqueuePass(m_VirtualMaterialPass);
        }
    }
}