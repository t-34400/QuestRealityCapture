#nullable enable

using RealityLog.Recording;
using UnityEngine;

namespace RealityLog.Recording
{
    public readonly struct RecordingDiagnosticsSettings
    {
        public RecordingDiagnosticsSettings(
            bool enabled,
            bool showHud,
            bool showTrajectory,
            bool showTrackingEvents,
            float positionJumpMeters,
            float rotationJumpDegrees)
        {
            this.enabled = enabled;
            this.showHud = showHud;
            this.showTrajectory = showTrajectory;
            this.showTrackingEvents = showTrackingEvents;
            this.positionJumpMeters = Mathf.Max(0.001f, positionJumpMeters);
            this.rotationJumpDegrees = Mathf.Max(0.1f, rotationJumpDegrees);
        }

        public readonly bool enabled;
        public readonly bool showHud;
        public readonly bool showTrajectory;
        public readonly bool showTrackingEvents;
        public readonly float positionJumpMeters;
        public readonly float rotationJumpDegrees;

        public static RecordingDiagnosticsSettings FromConfig(RecordingSessionConfig.LiveFeedbackConfig config)
        {
            var diagnostics = config.diagnostics;
            return new RecordingDiagnosticsSettings(
                config.enabled && diagnostics.enabled,
                diagnostics.showHud,
                diagnostics.showTrajectory,
                diagnostics.showTrackingEvents,
                diagnostics.positionJumpMeters,
                diagnostics.rotationJumpDegrees);
        }
    }
}
