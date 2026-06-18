#nullable enable

using System;
using System.IO;
using RealityLog.Recording;
using UnityEngine;

namespace RealityLog.Depth
{
    public class DepthMapExporter : MonoBehaviour
    {
        private static readonly string[] descriptorHeader = new[]
            {
                "timestamp_ms", "ovr_timestamp",
                "create_pose_location_x", "create_pose_location_y", "create_pose_location_z",
                "create_pose_rotation_x", "create_pose_rotation_y", "create_pose_rotation_z", "create_pose_rotation_w",
                "fov_left_angle_tangent", "fov_right_angle_tangent", "fov_top_angle_tangent", "fov_down_angle_tangent",
                "near_z", "far_z",
                "width", "height"
            };

        [HideInInspector]
        [SerializeField] private ComputeShader copyDepthMapShader = default!;
        [SerializeField] private string directoryName = "";
        [SerializeField] private string leftDepthMapDirectoryName = "left_depth";
        [SerializeField] private string rightDepthMapDirectoryName = "right_depth";
        [SerializeField] private string leftDepthDescFileName = "left_depth_descriptors.csv";
        [SerializeField] private string rightDepthDescFileName = "right_depth_descriptors.csv";
        [SerializeField] private int targetSaveFps = 0;

        private string? leftDepthDirectoryPath;
        private string? rightDepthDirectoryPath;
        private string? leftDepthDescriptorFilePath;
        private string? rightDepthDescriptorFilePath;

        [SerializeField] private DepthFrameProvider? depthFrameProvider = null;

        private DepthRenderTextureExporter? renderTextureExporter;
        private CsvWriter? leftDepthCsvWriter;
        private CsvWriter? rightDepthCsvWriter;

        private double baseOvrTimeSec;
        private long baseUnixTimeMs;

        private bool isExporting = false;

        public string DirectoryName
        {
            get => directoryName;
            set => directoryName = value;
        }

        public void ApplyConfiguration(RecordingSessionConfig.DepthConfig config, RecordingSessionPaths.DepthPaths paths)
        {
            targetSaveFps = config.targetSaveFps;
            leftDepthMapDirectoryName = config.leftDirectoryName;
            rightDepthMapDirectoryName = config.rightDirectoryName;
            leftDepthDescFileName = config.leftDescriptorFileName;
            rightDepthDescFileName = config.rightDescriptorFileName;
            leftDepthDirectoryPath = paths.LeftDirectoryPath;
            rightDepthDirectoryPath = paths.RightDirectoryPath;
            leftDepthDescriptorFilePath = paths.LeftDescriptorFilePath;
            rightDepthDescriptorFilePath = paths.RightDescriptorFilePath;
        }

        public void StartExport()
        {
            TryStartExport();
        }

        public bool TryStartExport()
        {
            StopExport();

            try
            {
                var paths = ResolveLegacyPathsIfNeeded();
                latestSavedTimestampMs = null;

                Directory.CreateDirectory(paths.leftDirectoryPath);
                Directory.CreateDirectory(paths.rightDirectoryPath);

                leftDepthCsvWriter = new(paths.leftDescriptorFilePath, descriptorHeader);
                rightDepthCsvWriter = new(paths.rightDescriptorFilePath, descriptorHeader);

                EnsureDepthFrameProvider();
                isExporting = true;
                depthFrameProvider?.BeginDepthUsage();

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to start depth export: {ex.Message}");
                StopExport();
                return false;
            }
        }

        public void StopExport()
        {
            isExporting = false;

            leftDepthCsvWriter?.Dispose();
            leftDepthCsvWriter = null;
            rightDepthCsvWriter?.Dispose();
            rightDepthCsvWriter = null;

            depthFrameProvider?.EndDepthUsage();
        }

        private void Awake()
        {
            baseOvrTimeSec = OVRPlugin.GetTimeInSeconds();
            baseUnixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            EnsureDepthFrameProvider();
            renderTextureExporter = new(copyDepthMapShader);

            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDestroy()
        {
            StopExport();

            renderTextureExporter?.Dispose();
            renderTextureExporter = null;

            Application.onBeforeRender -= OnBeforeRender;
        }

        private void OnBeforeRender()
        {
            if (!isExporting ||
                renderTextureExporter == null || depthFrameProvider == null
                || leftDepthCsvWriter == null || rightDepthCsvWriter == null)
            {
                return;
            }

            if (depthFrameProvider.TryGetLatestFrame(out var renderTexture, out var frameDescriptors))
            {
                const int FRAME_DESC_COUNT = 2;

                if (renderTexture == null || !renderTexture.IsCreated())
                {
                    Debug.LogError("RenderTexture is not created or null.");
                    return;
                }

                if (frameDescriptors.Length != FRAME_DESC_COUNT)
                    {
                        Debug.LogError("Expected exactly two depth frame descriptors (left and right).");
                        return;
                    }

                var width = renderTexture.width;
                var height = renderTexture.height;

                var unixTime = ConvertTimestampNsToUnixTimeMs(frameDescriptors[0].timestampNs);
                if (!ShouldSaveFrame(unixTime))
                {
                    return;
                }

                var paths = ResolveLegacyPathsIfNeeded();
                var leftDepthFilePath = Path.Join(paths.leftDirectoryPath, $"{unixTime}.raw");
                var rightDepthFilePath = Path.Join(paths.rightDirectoryPath, $"{unixTime}.raw");

                renderTextureExporter.Export(renderTexture, leftDepthFilePath, rightDepthFilePath);
                latestSavedTimestampMs = unixTime;

                for (var i = 0; i < FRAME_DESC_COUNT; ++i)
                {
                    var frameDesc = frameDescriptors[i];

                    var timestampMs = ConvertTimestampNsToUnixTimeMs(frameDesc.timestampNs);
                    var ovrTimestamp = frameDesc.timestampNs / 1.0e9;

                    var row = new double[]
                    {
                        timestampMs,
                        ovrTimestamp,
                        frameDesc.createPoseLocation.x, frameDesc.createPoseLocation.y, frameDesc.createPoseLocation.z,
                        frameDesc.createPoseRotation.x, frameDesc.createPoseRotation.y, frameDesc.createPoseRotation.z, frameDesc.createPoseRotation.w,
                        frameDesc.fovLeftAngleTangent, frameDesc.fovRightAngleTangent,
                        frameDesc.fovTopAngleTangent, frameDesc.fovDownAngleTangent,
                        frameDesc.nearZ, frameDesc.farZ,
                        width, height
                    };

                    if (i == 0)
                    {
                        leftDepthCsvWriter?.EnqueueRow(row);
                    }
                    else
                    {
                        rightDepthCsvWriter?.EnqueueRow(row);
                    }
                }
            }
        }

        private long? latestSavedTimestampMs;

        private bool ShouldSaveFrame(long timestampMs)
        {
            if (targetSaveFps <= 0 || latestSavedTimestampMs == null)
            {
                return true;
            }

            var minIntervalMs = 1000.0 / targetSaveFps;
            return timestampMs - latestSavedTimestampMs.Value >= minIntervalMs;
        }

        private (
            string leftDirectoryPath,
            string rightDirectoryPath,
            string leftDescriptorFilePath,
            string rightDescriptorFilePath) ResolveLegacyPathsIfNeeded()
        {
            if (!string.IsNullOrEmpty(leftDepthDirectoryPath)
                && !string.IsNullOrEmpty(rightDepthDirectoryPath)
                && !string.IsNullOrEmpty(leftDepthDescriptorFilePath)
                && !string.IsNullOrEmpty(rightDepthDescriptorFilePath))
            {
                return (
                    leftDepthDirectoryPath!,
                    rightDepthDirectoryPath!,
                    leftDepthDescriptorFilePath!,
                    rightDepthDescriptorFilePath!);
            }

            var rootDirectoryPath = Path.Combine(Application.persistentDataPath, DirectoryName);
            return (
                Path.Combine(rootDirectoryPath, leftDepthMapDirectoryName),
                Path.Combine(rootDirectoryPath, rightDepthMapDirectoryName),
                Path.Combine(rootDirectoryPath, leftDepthDescFileName),
                Path.Combine(rootDirectoryPath, rightDepthDescFileName));
        }

        private long ConvertTimestampNsToUnixTimeMs(long timestampNs)
        {
            var deltaMs = (long) (timestampNs / 1.0e6 - baseOvrTimeSec * 1000.0);
            return baseUnixTimeMs + deltaMs;
        }

        private void EnsureDepthFrameProvider()
        {
            if (depthFrameProvider != null)
            {
                return;
            }

            depthFrameProvider = GetComponent<DepthFrameProvider>();
            if (depthFrameProvider == null)
            {
                depthFrameProvider = gameObject.AddComponent<DepthFrameProvider>();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            const string COPY_DEPTH_MAP_SHADER_PATH = "Assets/RealityLog/ComputeShaders/CopyDepthMap.compute";

            if (copyDepthMapShader == null)
            {
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(COPY_DEPTH_MAP_SHADER_PATH);
                if (shader == null)
                {
                    Debug.LogError($"Failed to load ComputeShader at path: {COPY_DEPTH_MAP_SHADER_PATH}");
                }
                else
                {
                    copyDepthMapShader = shader;
                    Debug.Log($"Successfully loaded ComputeShader: {COPY_DEPTH_MAP_SHADER_PATH}");
                }
            }
        }
# endif
    }
}