#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace RealityLog.Recording
{
    public static class RecordingConfigLoader
    {
        public const string DefaultExternalConfigPath = "recording_config.json";

        public static RecordingSessionConfig Load(TextAsset? configAsset, string externalConfigPath)
        {
            var config = new RecordingSessionConfig();
            var json = ResolveJson(configAsset, externalConfigPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return config;
            }

            try
            {
                JsonUtility.FromJsonOverwrite(json, config);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to parse recording config JSON: {ex.Message}");
            }

            Normalize(config);
            return config;
        }

        private static string ResolveJson(TextAsset? configAsset, string externalConfigPath)
        {
            var resolvedExternalPath = ResolveExternalPath(externalConfigPath);
            if (!string.IsNullOrEmpty(resolvedExternalPath))
            {
                if (File.Exists(resolvedExternalPath))
                {
                    try
                    {
                        Debug.Log($"[{Constants.LOG_TAG}] Loading recording config JSON from external path: {resolvedExternalPath}");
                        return File.ReadAllText(resolvedExternalPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[{Constants.LOG_TAG}] Failed to read recording config JSON from {resolvedExternalPath}: {ex.Message}");
                        return string.Empty;
                    }
                }

                Debug.Log($"[{Constants.LOG_TAG}] External recording config JSON not found. Using fallback config: {resolvedExternalPath}");
            }

            if (configAsset != null)
            {
                Debug.Log($"[{Constants.LOG_TAG}] Loading recording config JSON from TextAsset: {configAsset.name}");
                return configAsset.text;
            }

            return string.Empty;
        }

        public static string ResolveExternalPath(string externalConfigPath)
        {
            if (string.IsNullOrWhiteSpace(externalConfigPath))
            {
                return string.Empty;
            }

            return Path.IsPathRooted(externalConfigPath)
                ? externalConfigPath
                : Path.Combine(Application.persistentDataPath, externalConfigPath);
        }

        private static void Normalize(RecordingSessionConfig config)
        {
            config.sessionNameFormat = NormalizeText(
                config.sessionNameFormat,
                new RecordingSessionConfig().sessionNameFormat,
                "sessionNameFormat");

            config.camera ??= new RecordingSessionConfig.CameraConfig();
            config.camera.targetSaveFps = NormalizeFps(config.camera.targetSaveFps, "camera.targetSaveFps");
            config.camera.left = NormalizeCameraSide(
                config.camera.left,
                RecordingSessionConfig.CameraSideConfig.LeftDefaults(),
                "camera.left");
            config.camera.right = NormalizeCameraSide(
                config.camera.right,
                RecordingSessionConfig.CameraSideConfig.RightDefaults(),
                "camera.right");

            config.depth ??= new RecordingSessionConfig.DepthConfig();
            config.depth.targetSaveFps = NormalizeFps(config.depth.targetSaveFps, "depth.targetSaveFps");
            config.depth.leftDirectoryName = NormalizeText(
                config.depth.leftDirectoryName,
                "left_depth",
                "depth.leftDirectoryName");
            config.depth.rightDirectoryName = NormalizeText(
                config.depth.rightDirectoryName,
                "right_depth",
                "depth.rightDirectoryName");
            config.depth.leftDescriptorFileName = NormalizeText(
                config.depth.leftDescriptorFileName,
                "left_depth_descriptors.csv",
                "depth.leftDescriptorFileName");
            config.depth.rightDescriptorFileName = NormalizeText(
                config.depth.rightDescriptorFileName,
                "right_depth_descriptors.csv",
                "depth.rightDescriptorFileName");

            config.pose ??= new RecordingSessionConfig.PoseConfig();
            config.pose.targetSaveFps = NormalizeFps(config.pose.targetSaveFps, "pose.targetSaveFps");
            config.pose.hmdFileName = NormalizeText(config.pose.hmdFileName, "hmd_poses.csv", "pose.hmdFileName");
            config.pose.leftControllerFileName = NormalizeText(config.pose.leftControllerFileName, "left_controller_poses.csv", "pose.leftControllerFileName");
            config.pose.rightControllerFileName = NormalizeText(config.pose.rightControllerFileName, "right_controller_poses.csv", "pose.rightControllerFileName");
            config.pose.fileName = config.pose.fileName ?? string.Empty;

            NormalizeLiveFeedback(config);
        }


        private static void NormalizeLiveFeedback(RecordingSessionConfig config)
        {
            config.liveFeedback ??= new RecordingSessionConfig.LiveFeedbackConfig();
            config.liveFeedback.coverage ??= new RecordingSessionConfig.CoverageConfig();
            config.liveFeedback.diagnostics ??= new RecordingSessionConfig.DiagnosticsConfig();

            var coverage = config.liveFeedback.coverage;
            coverage.targetUpdateFps = NormalizeFps(coverage.targetUpdateFps, "liveFeedback.coverage.targetUpdateFps");
            coverage.samplingStep = NormalizePositiveInt(coverage.samplingStep, 24, "liveFeedback.coverage.samplingStep");
            coverage.voxelSizeMeters = NormalizePositiveFloat(coverage.voxelSizeMeters, 0.15f, "liveFeedback.coverage.voxelSizeMeters");
            coverage.maxVoxels = NormalizePositiveInt(coverage.maxVoxels, 30000, "liveFeedback.coverage.maxVoxels");
            coverage.minDepthMeters = NormalizePositiveFloat(coverage.minDepthMeters, 0.3f, "liveFeedback.coverage.minDepthMeters");
            coverage.maxDepthMeters = NormalizeDepthMax(coverage.maxDepthMeters, coverage.minDepthMeters, "liveFeedback.coverage.maxDepthMeters");
            coverage.eye = NormalizeEye(coverage.eye, "liveFeedback.coverage.eye");

            var diagnostics = config.liveFeedback.diagnostics;
            diagnostics.positionJumpMeters = NormalizePositiveFloat(diagnostics.positionJumpMeters, 0.3f, "liveFeedback.diagnostics.positionJumpMeters");
            diagnostics.rotationJumpDegrees = NormalizePositiveFloat(diagnostics.rotationJumpDegrees, 30.0f, "liveFeedback.diagnostics.rotationJumpDegrees");
        }

        private static int NormalizePositiveInt(int value, int defaultValue, string name)
        {
            if (value > 0)
            {
                return value;
            }

            Debug.LogError($"[{Constants.LOG_TAG}] Invalid non-positive recording config value {name}={value}. Using {defaultValue}.");
            return defaultValue;
        }

        private static float NormalizePositiveFloat(float value, float defaultValue, string name)
        {
            if (value > 0f && !float.IsNaN(value) && !float.IsInfinity(value))
            {
                return value;
            }

            Debug.LogError($"[{Constants.LOG_TAG}] Invalid non-positive recording config value {name}={value}. Using {defaultValue}.");
            return defaultValue;
        }

        private static float NormalizeDepthMax(float value, float minDepthMeters, string name)
        {
            if (value > minDepthMeters && !float.IsNaN(value) && !float.IsInfinity(value))
            {
                return value;
            }

            var fallback = Math.Max(minDepthMeters + 0.1f, 5.0f);
            Debug.LogError($"[{Constants.LOG_TAG}] Invalid recording config value {name}={value}. Using {fallback}.");
            return fallback;
        }

        private static string NormalizeEye(string? value, string name)
        {
            if (string.Equals(value, "left", StringComparison.OrdinalIgnoreCase))
            {
                return "left";
            }

            if (string.Equals(value, "right", StringComparison.OrdinalIgnoreCase))
            {
                return "right";
            }

            Debug.LogWarning($"[{Constants.LOG_TAG}] Invalid recording config value {name}={value}. Using left.");
            return "left";
        }

        private static RecordingSessionConfig.CameraSideConfig NormalizeCameraSide(
            RecordingSessionConfig.CameraSideConfig? side,
            RecordingSessionConfig.CameraSideConfig defaults,
            string name)
        {
            side ??= defaults;
            side.imageDirectoryName = NormalizeText(
                side.imageDirectoryName,
                defaults.imageDirectoryName,
                $"{name}.imageDirectoryName");
            side.metadataFileName = NormalizeText(
                side.metadataFileName,
                defaults.metadataFileName,
                $"{name}.metadataFileName");
            side.formatInfoFileName = NormalizeText(
                side.formatInfoFileName,
                defaults.formatInfoFileName,
                $"{name}.formatInfoFileName");
            return side;
        }

        private static string NormalizeText(string? value, string defaultValue, string name)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Debug.LogWarning($"[{Constants.LOG_TAG}] Empty recording config value {name}. Using default: {defaultValue}");
            return defaultValue;
        }

        private static int NormalizeFps(int fps, string name)
        {
            if (fps >= 0)
            {
                return fps;
            }

            Debug.LogError($"[{Constants.LOG_TAG}] Invalid negative recording config value {name}={fps}. Using 0.");
            return 0;
        }
    }
}
