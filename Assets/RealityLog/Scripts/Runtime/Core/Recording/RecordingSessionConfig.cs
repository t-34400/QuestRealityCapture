#nullable enable

using System;

namespace RealityLog.Recording
{
    [Serializable]
    public sealed class RecordingSessionConfig
    {
        public string sessionNameFormat = "yyyyMMdd_HHmmss";
        public CameraConfig camera = new();
        public DepthConfig depth = new();
        public PoseConfig pose = new();
        public LiveFeedbackConfig liveFeedback = new();

        [Serializable]
        public sealed class CameraConfig
        {
            public bool enabled = true;
            public int targetSaveFps = 10;
            public bool preferOpenByCameraId = true;
            public bool allowJavaMetadataFallback = false;
            public CameraSideConfig left = CameraSideConfig.LeftDefaults();
            public CameraSideConfig right = CameraSideConfig.RightDefaults();
        }

        [Serializable]
        public sealed class CameraSideConfig
        {
            public bool enabled = true;
            public string imageDirectoryName = string.Empty;
            public string metadataFileName = string.Empty;
            public string formatInfoFileName = string.Empty;

            public static CameraSideConfig LeftDefaults()
            {
                return new CameraSideConfig
                {
                    imageDirectoryName = "left_camera_raw",
                    metadataFileName = "left_camera_characteristics.json",
                    formatInfoFileName = "left_camera_image_format.json"
                };
            }

            public static CameraSideConfig RightDefaults()
            {
                return new CameraSideConfig
                {
                    imageDirectoryName = "right_camera_raw",
                    metadataFileName = "right_camera_characteristics.json",
                    formatInfoFileName = "right_camera_image_format.json"
                };
            }
        }

        [Serializable]
        public sealed class DepthConfig
        {
            public bool enabled = true;
            public int targetSaveFps = 10;
            public string leftDirectoryName = "left_depth";
            public string rightDirectoryName = "right_depth";
            public string leftDescriptorFileName = "left_depth_descriptors.csv";
            public string rightDescriptorFileName = "right_depth_descriptors.csv";
        }

        [Serializable]
        public sealed class PoseConfig
        {
            public bool enabled = true;
            public int targetSaveFps = 30;
            public string hmdFileName = "hmd_poses.csv";
            public string leftControllerFileName = "left_controller_poses.csv";
            public string rightControllerFileName = "right_controller_poses.csv";

            // Kept so older override JSON files fail soft instead of losing pose output entirely.
            public string fileName = string.Empty;
        }

        [Serializable]
        public sealed class LiveFeedbackConfig
        {
            public bool enabled = false;
            public CoverageConfig coverage = new();
            public DiagnosticsConfig diagnostics = new();
        }

        [Serializable]
        public sealed class CoverageConfig
        {
            public bool enabled = true;
            public int targetUpdateFps = 3;
            public int samplingStep = 24;
            public float voxelSizeMeters = 0.15f;
            public int maxVoxels = 30000;
            public float minDepthMeters = 0.3f;
            public float maxDepthMeters = 5.0f;
            public string eye = "left";
            public bool showSampleFrustums = false;
            public float frustumSampleIntervalSeconds = 1.0f;
            public int maxFrustumSamples = 24;
            public bool logPoseDiagnostics = false;
            public float poseDiagnosticIntervalSeconds = 1.0f;
            public bool flipVerticalProjection = true;
        }

        [Serializable]
        public sealed class DiagnosticsConfig
        {
            public bool enabled = true;
            public bool showHud = false;
            public bool showTrajectory = true;
            public bool showTrackingEvents = true;
            public float positionJumpMeters = 0.3f;
            public float rotationJumpDegrees = 30.0f;
        }
    }
}
