using UnityEngine;

namespace VirtualTexture
{
    [DisallowMultipleComponent]
	[RequireComponent(typeof(Renderer))]
	public class VirtualShadowCaster : MonoBehaviour
	{
		[SerializeField]
		public bool castShadow = true;
    }
}