using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VirtualTexture
{
    public sealed class VirtualShadowManager
    {
        static readonly Lazy<VirtualShadowManager> m_Instance = new Lazy<VirtualShadowManager>(() => new VirtualShadowManager());

        public static VirtualShadowManager instance => m_Instance.Value;

        private List<VirtualShadowMaps> m_VirtualShadowMaps;

        private Dictionary<Camera, VirtualShadowCamera> m_VirtualShadowCameras;

        VirtualShadowManager()
        {
            m_VirtualShadowMaps = new List<VirtualShadowMaps>();
            m_VirtualShadowCameras = new Dictionary<Camera, VirtualShadowCamera>();
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

        public void RegisterCamera(VirtualShadowCamera camera)
        {
            m_VirtualShadowCameras.Add(camera.GetCamera(), camera);
        }

        public void UnregisterCamera(VirtualShadowCamera camera)
        {
            m_VirtualShadowCameras.Remove(camera.GetCamera());
        }

        public bool TryGetCamera(Camera camera, out VirtualShadowCamera value)
        {
            return m_VirtualShadowCameras.TryGetValue(camera, out value);
        }
    }
}