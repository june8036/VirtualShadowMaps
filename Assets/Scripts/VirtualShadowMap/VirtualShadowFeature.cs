using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VirtualTexture
{
    public sealed class VirtualShadowFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private static readonly string m_VirtualShadowMapsKeyword = "_VIRTUAL_SHADOW_MAPS";

        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private static GlobalKeyword m_VirtualShadowMapsKeywordFeature;

        class VirtualShadowPass : ScriptableRenderPass
        {
            public VirtualShadowPass(RenderPassEvent passEvent)
            {
                this.renderPassEvent = passEvent;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get();
                cmd.Clear();

                var virtualShadowMaps = VirtualShadowManager.instance.First();
                if (virtualShadowMaps != null && virtualShadowMaps.enabled)
                {
                    if (VirtualShadowManager.instance.TryGetCamera(renderingData.cameraData.camera, out var virtualShadowCamera))
                    {
                        if (virtualShadowCamera.enabled)
                            cmd.EnableKeyword(m_VirtualShadowMapsKeywordFeature);
                        else
                            cmd.DisableKeyword(m_VirtualShadowMapsKeywordFeature);
                    }
                    else
                    {
                        cmd.DisableKeyword(m_VirtualShadowMapsKeywordFeature);
                    }
                }
                else
                {
                    cmd.DisableKeyword(m_VirtualShadowMapsKeywordFeature);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        VirtualShadowPass m_VirtualShadowPass;

        public override void Create()
        {
            m_VirtualShadowMapsKeywordFeature = GlobalKeyword.Create(m_VirtualShadowMapsKeyword);
            m_VirtualShadowPass = new VirtualShadowPass(RenderPassEvent.BeforeRendering);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.renderType != CameraRenderType.Overlay)
                renderer.EnqueuePass(m_VirtualShadowPass);
        }
    }
}