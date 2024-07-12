using System.Collections.Generic;
using UnityEngine;

namespace VirtualTexture
{
    public static class BoundsExtension
    {
        public static Vector3 GetCorner(this Bounds aabb, int index)
        {
            Debug.Assert(index >= 0 && index <= 7);
            float X = (((index & 1) != 0) ^ ((index & 2) != 0)) ? (aabb.max.x) : (aabb.min.x);
            float Y = ((index / 2) % 2 == 0) ? (aabb.min.y) : (aabb.max.y);
            float Z = (index < 4) ? (aabb.min.z) : (aabb.max.z);
            return new Vector3(X, Y, Z);
        }

        public static Bounds CalclateFitScene(this Bounds bounds, Matrix4x4 worldToLocalMatrix)
        {
            var boundsInLightSpace = new Bounds();
            boundsInLightSpace.max = Vector3.negativeInfinity;
            boundsInLightSpace.min = Vector3.positiveInfinity;

            for (var i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.GetCorner(i);
                Vector3 localPosition = worldToLocalMatrix.MultiplyPoint(corner);

                boundsInLightSpace.Encapsulate(localPosition);
            }

            return boundsInLightSpace;
        }
    }

    public static class VirtualShadowMapsUtilities
    {
        public static Bounds CalculateBoundingBox(List<Renderer> renderers)
        {
            Bounds aabb = new Bounds();
            aabb.max = Vector3.negativeInfinity;
            aabb.min = Vector3.positiveInfinity;

            foreach (var renderer in renderers)
                aabb.Encapsulate(renderer.bounds);

            return aabb;
        }

        public static Bounds CalculateBoundingBox(List<Renderer> renderers, Camera camera)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);

            Bounds aabb = new Bounds();
            aabb.max = Vector3.negativeInfinity;
            aabb.min = Vector3.positiveInfinity;

            foreach (var it in renderers)
            {
                if (GeometryUtility.TestPlanesAABB(planes, it.bounds))
                    aabb.Encapsulate(it.bounds);
            }

            return aabb;
        }

        public static Bounds CalculateBoundingBox(List<MeshRenderer> renderers, Plane[] planes)
        {
            Bounds aabb = new Bounds();
            aabb.max = Vector3.negativeInfinity;
            aabb.min = Vector3.positiveInfinity;

            foreach (var it in renderers)
            {
                if (GeometryUtility.TestPlanesAABB(planes, it.bounds))
                    aabb.Encapsulate(it.bounds);
            }

            return aabb;
        }

        public static float CameraSpaceDistance(Vector3 pos, Vector3 normal, Vector3 origion, Vector3 direction)
        {
            Plane pane = new Plane(normal, pos);
            Ray ray = new Ray(origion, direction);

            pane.Raycast(ray, out var enter);
            var point = ray.GetPoint(enter);

            return (ray.origin - point).magnitude;
        }

        public static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
        {
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(pos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        public static float CalculateBiasScale(float orthographicSize, int tileSize)
        {
            float halfFrustumSize = orthographicSize;
            float halfTexelResolution = halfFrustumSize / tileSize;

            float biasScale = 10;
            biasScale *= halfTexelResolution;

            return biasScale;
        }

        public static bool CanSkipTile(Color[] colors, int x, int y, int count, int texWidth)
        {
            int emptyCount = 0;

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    if (colors[(y * count + i) * texWidth + x * count + j].r == 0) emptyCount++;
                }
            }

            return emptyCount * 100.0f / count / count > 99;
        }

        public static int CalculateTileNums(Texture2D source, int blockSize)
        {
            var colors = source.GetPixels();

            int tileNums = 0;

            for (int i = 0; i < source.height / blockSize; i++)
            {
                for (int j = 0; j < source.width / blockSize; j++)
                {
                    if (CanSkipTile(colors, j, i, blockSize, source.width))
                        continue;

                    tileNums++;
                }
            }

            return tileNums;
        }

        public static Matrix4x4 GetWorldToShadowMapSpaceMatrix(Matrix4x4 proj, Matrix4x4 view)
        {
            Matrix4x4 worldToShadow = proj * view;
            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m22 = 1.0f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;
            textureScaleAndBias.m23 = 0.0f;

            return textureScaleAndBias * worldToShadow;
        }

        public static Matrix4x4 GetTextureScaleAndBiasMatrix(Vector3 offset, Vector3 scale)
        {
            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = scale.x;
            textureScaleAndBias.m11 = scale.x;
            textureScaleAndBias.m22 = scale.x;
            textureScaleAndBias.m03 = offset.x;
            textureScaleAndBias.m13 = offset.y;
            textureScaleAndBias.m23 = offset.z;

            return textureScaleAndBias;
        }
    }
}