using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualTexture
{
    public sealed class VirtualShadowManager
    {
        static readonly Lazy<VirtualShadowManager> m_Instance = new Lazy<VirtualShadowManager>(() => new VirtualShadowManager());

        public static VirtualShadowManager instance => m_Instance.Value;

        private List<VirtualShadowMaps> m_VirtualShadowMaps;

        VirtualShadowManager()
        {
            m_VirtualShadowMaps = new List<VirtualShadowMaps>();
        }

        public VirtualShadowMaps First()
        {
            return m_VirtualShadowMaps.Count > 0 ? m_VirtualShadowMaps.First() : null;
        }

        public void Register(VirtualShadowMaps shadowMaps)
        {
            m_VirtualShadowMaps.Add(shadowMaps);
        }

        public void Unregister(VirtualShadowMaps shadowMaps)
        {
            m_VirtualShadowMaps.Remove(shadowMaps);
        }
    }
}