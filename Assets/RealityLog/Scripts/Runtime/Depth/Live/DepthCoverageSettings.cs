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
            DepthCoverageEye eye)
        {
            this.enabled = enabled;
            this.targetUpdateFps = Mathf.Max(1, targetUpdateFps);
            this.samplingStep = Mathf.Max(1, samplingStep);
            this.voxelSizeMeters = Mathf.Max(0.01f, voxelSizeMeters);
            this.maxVoxels = Mathf.Max(1, maxVoxels);
            this.minDepthMeters = Mathf.Max(0.0f, minDepthMeters);
            this.maxDepthMeters = Mathf.Max(this.minDepthMeters + 0.01f, maxDepthMeters);
            this.eye = eye;
        }

        public readonly bool enabled;
        public readonly int targetUpdateFps;
        public readonly int samplingStep;
        public readonly float voxelSizeMeters;
        public readonly int maxVoxels;
        public readonly float minDepthMeters;
        public readonly float maxDepthMeters;
        public readonly DepthCoverageEye eye;

        public float UpdateIntervalSeconds => 1.0f / targetUpdateFps;

        public static DepthCoverageEye ParseEye(string? value)
        {
            return value != null && value.ToLowerInvariant() == "right"
                ? DepthCoverageEye.Right
                : DepthCoverageEye.Left;
        }
    }
}
