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
            config.camera.targetSaveFps = NormalizeFps(config.camera.targetSaveFps, "camera.targetSaveFps");
            config.depth.targetSaveFps = NormalizeFps(config.depth.targetSaveFps, "depth.targetSaveFps");
            config.pose.targetSaveFps = NormalizeFps(config.pose.targetSaveFps, "pose.targetSaveFps");
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
