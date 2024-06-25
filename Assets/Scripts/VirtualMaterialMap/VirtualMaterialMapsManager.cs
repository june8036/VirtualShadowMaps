using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VirtualTexture
{
    public sealed class VirtualMaterialMapsManager
    {
        static readonly Lazy<VirtualMaterialMapsManager> m_Instance = new Lazy<VirtualMaterialMapsManager>(() => new VirtualMaterialMapsManager());

        public static VirtualMaterialMapsManager instance => m_Instance.Value;

        private List<VirtualMaterialMaps> m_VirtualLightMaps;

        private Dictionary<Camera, VirtualMaterialMapCamera> m_VirtualLightMapsCameras;

        VirtualMaterialMapsManager()
        {
            m_VirtualLightMaps = new List<VirtualMaterialMaps>();
            m_VirtualLightMapsCameras = new Dictionary<Camera, VirtualMaterialMapCamera>();
        }

        public VirtualMaterialMaps First()
        {
            return m_VirtualLightMaps.Count > 0 ? m_VirtualLightMaps.First() : null;
        }

        public void Register(VirtualMaterialMaps shadowMaps)
        {
            m_VirtualLightMaps.Add(shadowMaps);
        }

        public void Unregister(VirtualMaterialMaps shadowMaps)
        {
            m_VirtualLightMaps.Remove(shadowMaps);
        }

        public void RegisterCamera(VirtualMaterialMapCamera camera)
        {
            m_VirtualLightMapsCameras.Add(camera.GetCamera(), camera);
        }

        public void UnregisterCamera(VirtualMaterialMapCamera camera)
        {
            m_VirtualLightMapsCameras.Remove(camera.GetCamera());
        }

        public bool TryGetCamera(Camera camera, out VirtualMaterialMapCamera value)
        {
            return m_VirtualLightMapsCameras.TryGetValue(camera, out value);
        }
    }
}