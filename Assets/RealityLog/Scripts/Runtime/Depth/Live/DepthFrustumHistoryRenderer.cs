#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace RealityLog.Depth
{
    internal sealed class DepthFrustumHistoryRenderer
    {
        private readonly Queue<GameObject> samples = new();
        private Transform? root;
        private Material? material;
        private int maxSamples;
        private float lineWidthMeters;
        private float alpha;

        public void Configure(Transform parent, int maxSamples, float lineWidthMeters, float alpha)
        {
            this.maxSamples = Mathf.Max(0, maxSamples);
            this.lineWidthMeters = Mathf.Max(0.0005f, lineWidthMeters);
            this.alpha = Mathf.Clamp01(alpha);
            EnsureRoot(parent);
            EnsureMaterial();
            TrimSamples();
        }

        public void AddSample(DepthFrameDesc descriptor, Matrix4x4 depthCameraToWorld, float distanceMeters, int segmentId)
        {
            if (root == null || maxSamples <= 0)
            {
                return;
            }

            var distance = Mathf.Max(0.05f, distanceMeters);
            var sampleObject = new GameObject($"Depth Sample Frustum {samples.Count:000}");
            sampleObject.transform.SetParent(root, false);

            var origin = depthCameraToWorld.MultiplyPoint3x4(Vector3.zero);
            var topLeft = DepthCameraPointToWorld(depthCameraToWorld, -descriptor.fovLeftAngleTangent, descriptor.fovTopAngleTangent, distance);
            var topRight = DepthCameraPointToWorld(depthCameraToWorld, descriptor.fovRightAngleTangent, descriptor.fovTopAngleTangent, distance);
            var bottomLeft = DepthCameraPointToWorld(depthCameraToWorld, -descriptor.fovLeftAngleTangent, -descriptor.fovDownAngleTangent, distance);
            var bottomRight = DepthCameraPointToWorld(depthCameraToWorld, descriptor.fovRightAngleTangent, -descriptor.fovDownAngleTangent, distance);

            var color = FrustumColor(segmentId);
            AddLine(sampleObject.transform, origin, topLeft, color);
            AddLine(sampleObject.transform, origin, topRight, color);
            AddLine(sampleObject.transform, origin, bottomLeft, color);
            AddLine(sampleObject.transform, origin, bottomRight, color);
            AddLine(sampleObject.transform, topLeft, topRight, color);
            AddLine(sampleObject.transform, topRight, bottomRight, color);
            AddLine(sampleObject.transform, bottomRight, bottomLeft, color);
            AddLine(sampleObject.transform, bottomLeft, topLeft, color);

            samples.Enqueue(sampleObject);
            TrimSamples();
        }

        public void Clear()
        {
            while (samples.Count > 0)
            {
                Object.Destroy(samples.Dequeue());
            }
        }

        public void Dispose()
        {
            Clear();
            if (root != null)
            {
                Object.Destroy(root.gameObject);
                root = null;
            }

            if (material != null)
            {
                Object.Destroy(material);
                material = null;
            }
        }

        private void EnsureRoot(Transform parent)
        {
            if (root != null)
            {
                return;
            }

            var rootObject = new GameObject("Live Depth Sample Frustums");
            rootObject.transform.SetParent(parent, false);
            root = rootObject.transform;
        }

        private void EnsureMaterial()
        {
            if (material != null)
            {
                return;
            }

            material = DepthVisualizationMaterialFactory.CreateLineMaterial();
        }

        private void AddLine(Transform parent, Vector3 start, Vector3 end, Color color)
        {
            var lineObject = new GameObject("Frustum Edge");
            lineObject.transform.SetParent(parent, false);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
            lineRenderer.startWidth = lineWidthMeters;
            lineRenderer.endWidth = lineWidthMeters;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.numCapVertices = 2;
            if (material != null)
            {
                lineRenderer.sharedMaterial = material;
            }
        }

        private void TrimSamples()
        {
            while (samples.Count > maxSamples)
            {
                Object.Destroy(samples.Dequeue());
            }
        }

        private Color FrustumColor(int segmentId)
        {
            var hue = Mathf.Repeat(segmentId * 0.17f + 0.54f, 1.0f);
            var color = Color.HSVToRGB(hue, 0.55f, 1.0f);
            color.a = alpha;
            return color;
        }

        private static Vector3 DepthCameraPointToWorld(Matrix4x4 depthCameraToWorld, float xTan, float yTan, float depthMeters)
        {
            return depthCameraToWorld.MultiplyPoint3x4(new Vector3(xTan * depthMeters, yTan * depthMeters, -depthMeters));
        }
    }
}
