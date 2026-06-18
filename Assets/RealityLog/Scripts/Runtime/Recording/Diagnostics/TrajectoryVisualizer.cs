#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace RealityLog.Recording
{
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TrajectoryVisualizer : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float targetUpdateFps = 2.0f;
        [SerializeField, Min(2)] private int maxPoints = 4096;
        [SerializeField, Min(0.001f)] private float lineWidthMeters = 0.015f;
        [SerializeField] private Material? trajectoryMaterial = null;

        private readonly List<Vector3> points = new();
        private LineRenderer? lineRenderer;
        private Material? runtimeTrajectoryMaterial;
        private bool isVisualizing;
        private double lastSampleTimestamp = double.NegativeInfinity;

        public int PointCount => points.Count;
        public bool IsVisualizing => isVisualizing;

        public void StartVisualization()
        {
            points.Clear();
            lastSampleTimestamp = double.NegativeInfinity;
            EnsureLineRenderer();
            if (lineRenderer != null)
            {
                lineRenderer.positionCount = 0;
                lineRenderer.enabled = true;
            }

            isVisualizing = true;
        }

        public void StopVisualization()
        {
            isVisualizing = false;
            points.Clear();
            lastSampleTimestamp = double.NegativeInfinity;
            if (lineRenderer != null)
            {
                lineRenderer.positionCount = 0;
                lineRenderer.enabled = false;
            }
        }

        public void OnPoseSampled(TrackingPoseSample sample)
        {
            if (!isVisualizing || !ShouldAddSample(sample.timestamp))
            {
                return;
            }

            lastSampleTimestamp = sample.timestamp;
            if (points.Count == maxPoints)
            {
                points.RemoveAt(0);
            }

            points.Add(sample.position);
            UpdateLineRenderer();
        }

        private bool ShouldAddSample(double timestamp)
        {
            return double.IsNegativeInfinity(lastSampleTimestamp)
                || timestamp - lastSampleTimestamp >= 1.0 / targetUpdateFps;
        }

        private void EnsureLineRenderer()
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            if (lineRenderer == null)
            {
                return;
            }

            lineRenderer.useWorldSpace = true;
            lineRenderer.widthMultiplier = lineWidthMeters;
            lineRenderer.numCapVertices = 2;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            if (trajectoryMaterial != null)
            {
                lineRenderer.material = trajectoryMaterial;
            }
            else if (lineRenderer.sharedMaterial == null)
            {
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    runtimeTrajectoryMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    if (runtimeTrajectoryMaterial.HasProperty("_Color"))
                    {
                        runtimeTrajectoryMaterial.SetColor("_Color", Color.cyan);
                    }

                    lineRenderer.material = runtimeTrajectoryMaterial;
                }
            }
        }

        private void UpdateLineRenderer()
        {
            EnsureLineRenderer();
            if (lineRenderer == null)
            {
                return;
            }

            lineRenderer.positionCount = points.Count;
            lineRenderer.SetPositions(points.ToArray());
        }

        private void OnDestroy()
        {
            StopVisualization();
            if (runtimeTrajectoryMaterial != null)
            {
                Destroy(runtimeTrajectoryMaterial);
                runtimeTrajectoryMaterial = null;
            }
        }
    }
}
