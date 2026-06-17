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
            config.pose.fileName = NormalizeText(config.pose.fileName, "poses.csv", "pose.fileName");
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
