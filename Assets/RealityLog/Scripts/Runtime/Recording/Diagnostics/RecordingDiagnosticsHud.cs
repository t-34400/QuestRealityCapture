#nullable enable

using RealityLog.Depth;
using UnityEngine;

namespace RealityLog.Recording
{
    public sealed class RecordingDiagnosticsHud : MonoBehaviour
    {
        [SerializeField] private TextMesh? textMesh = null;
        [SerializeField] private Vector3 cameraLocalPosition = new(0.0f, -0.24f, 1.5f);
        [SerializeField, Min(0.001f)] private float characterSize = 0.012f;

        private TrackingQualityMonitor? monitor;
        private TrajectoryVisualizer? trajectory;
        private LiveDepthCoverageVisualizer? coverage;
        private bool isVisible;

        public void Bind(TrackingQualityMonitor trackingMonitor, TrajectoryVisualizer? trajectoryVisualizer, LiveDepthCoverageVisualizer? coverageVisualizer)
        {
            monitor = trackingMonitor;
            trajectory = trajectoryVisualizer;
            coverage = coverageVisualizer;
        }

        public void StartHud()
        {
            EnsureTextMesh();
            if (textMesh != null)
            {
                textMesh.gameObject.SetActive(true);
            }

            isVisible = true;
        }

        public void StopHud()
        {
            isVisible = false;
            if (textMesh != null)
            {
                textMesh.text = string.Empty;
                textMesh.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (!isVisible || textMesh == null || monitor == null)
            {
                return;
            }

            var lastEvent = monitor.LastEvent;
            var eventText = lastEvent.HasValue
                ? $"last {lastEvent.Value.reason} seg={lastEvent.Value.segmentId}"
                : "none";
            var trajectoryText = trajectory != null && trajectory.IsVisualizing
                ? trajectory.PointCount.ToString()
                : "off";
            var coverageText = coverage != null && coverage.IsVisualizing
                ? $"segment {coverage.CurrentSegmentId}"
                : $"segment {monitor.SegmentId}";

            textMesh.text =
                "REC\n" +
                $"Tracking: {monitor.CurrentState}\n" +
                $"Coverage: {coverageText}\n" +
                $"Events: {eventText}\n" +
                $"Trajectory points: {trajectoryText}";
        }

        private void EnsureTextMesh()
        {
            if (textMesh != null)
            {
                return;
            }

            var mainCamera = global::UnityEngine.Camera.main;
            var cameraTransform = mainCamera != null ? mainCamera.transform : transform;
            var hudObject = new GameObject("RecordingDiagnosticsHudText");
            hudObject.transform.SetParent(cameraTransform, false);
            hudObject.transform.localPosition = cameraLocalPosition;
            hudObject.transform.localRotation = Quaternion.identity;
            hudObject.transform.localScale = Vector3.one;

            textMesh = hudObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.UpperLeft;
            textMesh.alignment = TextAlignment.Left;
            textMesh.characterSize = characterSize;
            textMesh.fontSize = 48;
            textMesh.color = Color.white;
            textMesh.text = string.Empty;
        }

        private void OnDestroy()
        {
            StopHud();
        }
    }
}
