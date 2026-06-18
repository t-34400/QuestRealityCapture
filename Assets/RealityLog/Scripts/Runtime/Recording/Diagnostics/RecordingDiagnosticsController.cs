#nullable enable

using System;
using UnityEngine;

namespace RealityLog.Recording
{
    public sealed class RecordingDiagnosticsController : MonoBehaviour
    {
        [SerializeField] private TrackingQualityMonitor? trackingMonitor = null;
        [SerializeField] private TrajectoryVisualizer? trajectoryVisualizer = null;
        [SerializeField] private TrackingEventMarkerRenderer? eventMarkerRenderer = null;
        [SerializeField] private RecordingDiagnosticsHud? diagnosticsHud = null;

        private RecordingDiagnosticsSettings settings = new(false, true, true, true, 0.3f, 30.0f);
        private bool isRunning;

        public bool IsRunning => isRunning;

        public void ApplyConfiguration(RecordingSessionConfig.LiveFeedbackConfig config)
        {
            settings = RecordingDiagnosticsSettings.FromConfig(config);
            trackingMonitor?.ApplyConfiguration(settings);
        }

        public bool TryStartDiagnostics()
        {
            StopDiagnostics();

            if (!settings.enabled)
            {
                return true;
            }

            try
            {
                EnsureComponents();
                trackingMonitor!.ApplyConfiguration(settings);

                if (settings.showTrajectory)
                {
                    trajectoryVisualizer!.StartVisualization();
                    trackingMonitor.PoseSampled += trajectoryVisualizer.OnPoseSampled;
                }

                if (settings.showTrackingEvents)
                {
                    eventMarkerRenderer!.StartRendering();
                    trackingMonitor.TrackingEventRaised += eventMarkerRenderer.OnTrackingEventRaised;
                }

                if (settings.showHud)
                {
                    diagnosticsHud!.Bind(trackingMonitor, settings.showTrajectory ? trajectoryVisualizer : null);
                    diagnosticsHud.StartHud();
                }

                if (!trackingMonitor.TryStartMonitoring())
                {
                    StopDiagnostics();
                    return false;
                }

                isRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Failed to start recording diagnostics: {ex.Message}");
                StopDiagnostics();
                return false;
            }
        }

        public void StopDiagnostics()
        {
            if (trackingMonitor != null)
            {
                if (trajectoryVisualizer != null)
                {
                    trackingMonitor.PoseSampled -= trajectoryVisualizer.OnPoseSampled;
                }

                if (eventMarkerRenderer != null)
                {
                    trackingMonitor.TrackingEventRaised -= eventMarkerRenderer.OnTrackingEventRaised;
                }

                trackingMonitor.StopMonitoring();
            }

            diagnosticsHud?.StopHud();
            eventMarkerRenderer?.StopRendering();
            trajectoryVisualizer?.StopVisualization();
            isRunning = false;
        }

        private void EnsureComponents()
        {
            if (trackingMonitor == null)
            {
                trackingMonitor = GetComponent<TrackingQualityMonitor>() ?? gameObject.AddComponent<TrackingQualityMonitor>();
            }

            if (settings.showTrajectory && trajectoryVisualizer == null)
            {
                trajectoryVisualizer = GetComponent<TrajectoryVisualizer>() ?? gameObject.AddComponent<TrajectoryVisualizer>();
            }

            if (settings.showTrackingEvents && eventMarkerRenderer == null)
            {
                eventMarkerRenderer = GetComponent<TrackingEventMarkerRenderer>() ?? gameObject.AddComponent<TrackingEventMarkerRenderer>();
            }

            if (settings.showHud && diagnosticsHud == null)
            {
                diagnosticsHud = GetComponent<RecordingDiagnosticsHud>() ?? gameObject.AddComponent<RecordingDiagnosticsHud>();
            }
        }

        private void OnDestroy()
        {
            StopDiagnostics();
        }
    }
}
