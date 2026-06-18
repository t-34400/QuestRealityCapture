#nullable enable

using UnityEngine;

namespace RealityLog.Depth
{
    public enum DepthCoverageEye
    {
        Left = 0,
        Right = 1
    }

    public readonly struct DepthCoverageSettings
    {
        public DepthCoverageSettings(
            bool enabled,
            int targetUpdateFps,
            int samplingStep,
            float voxelSizeMeters,
            int maxVoxels,
            float minDepthMeters,
            float maxDepthMeters,
            DepthCoverageEye eye,
            bool showSampleFrustums,
            float frustumSampleIntervalSeconds,
            int maxFrustumSamples,
            bool logPoseDiagnostics,
            float poseDiagnosticIntervalSeconds,
            bool flipVerticalProjection)
        {
            this.enabled = enabled;
            this.targetUpdateFps = Mathf.Max(1, targetUpdateFps);
            this.samplingStep = Mathf.Max(1, samplingStep);
            this.voxelSizeMeters = Mathf.Max(0.01f, voxelSizeMeters);
            this.maxVoxels = Mathf.Max(1, maxVoxels);
            this.minDepthMeters = Mathf.Max(0.0f, minDepthMeters);
            this.maxDepthMeters = Mathf.Max(this.minDepthMeters + 0.01f, maxDepthMeters);
            this.eye = eye;
            this.showSampleFrustums = showSampleFrustums;
            this.frustumSampleIntervalSeconds = Mathf.Max(0.1f, frustumSampleIntervalSeconds);
            this.maxFrustumSamples = Mathf.Max(0, maxFrustumSamples);
            this.logPoseDiagnostics = logPoseDiagnostics;
            this.poseDiagnosticIntervalSeconds = Mathf.Max(0.1f, poseDiagnosticIntervalSeconds);
            this.flipVerticalProjection = flipVerticalProjection;
        }

        public readonly bool enabled;
        public readonly int targetUpdateFps;
        public readonly int samplingStep;
        public readonly float voxelSizeMeters;
        public readonly int maxVoxels;
        public readonly float minDepthMeters;
        public readonly float maxDepthMeters;
        public readonly DepthCoverageEye eye;
        public readonly bool showSampleFrustums;
        public readonly float frustumSampleIntervalSeconds;
        public readonly int maxFrustumSamples;
        public readonly bool logPoseDiagnostics;
        public readonly float poseDiagnosticIntervalSeconds;
        public readonly bool flipVerticalProjection;

        public float UpdateIntervalSeconds => 1.0f / targetUpdateFps;

        public static DepthCoverageEye ParseEye(string? value)
        {
            return value != null && value.ToLowerInvariant() == "right"
                ? DepthCoverageEye.Right
                : DepthCoverageEye.Left;
        }
    }
}
