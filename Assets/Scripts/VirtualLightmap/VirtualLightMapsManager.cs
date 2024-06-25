using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VirtualTexture
{
    public sealed class VirtualLightMapsManager
    {
        static readonly Lazy<VirtualLightMapsManager> m_Instance = new Lazy<VirtualLightMapsManager>(() => new VirtualLightMapsManager());

        public static VirtualLightMapsManager instance => m_Instance.Value;

        private List<VirtualLightMaps> m_VirtualLightMaps;

        private Dictionary<Camera, VirtualLightMapCamera> m_VirtualLightMapsCameras;

        VirtualLightMapsManager()
        {
            m_VirtualLightMaps = new List<VirtualLightMaps>();
            m_VirtualLightMapsCameras = new Dictionary<Camera, VirtualLightMapCamera>();
        }

        public VirtualLightMaps First()
        {
            return m_VirtualLightMaps.Count > 0 ? m_VirtualLightMaps.First() : null;
        }

        public void Register(VirtualLightMaps shadowMaps)
        {
            m_VirtualLightMaps.Add(shadowMaps);
        }

        public void Unregister(VirtualLightMaps shadowMaps)
        {
            m_VirtualLightMaps.Remove(shadowMaps);
        }

        public void RegisterCamera(VirtualLightMapCamera camera)
        {
            m_VirtualLightMapsCameras.Add(camera.GetCamera(), camera);
        }

        public void UnregisterCamera(VirtualLightMapCamera camera)
        {
            m_VirtualLightMapsCameras.Remove(camera.GetCamera());
        }

        public bool TryGetCamera(Camera camera, out VirtualLightMapCamera value)
        {
            return m_VirtualLightMapsCameras.TryGetValue(camera, out value);
        }
    }
}