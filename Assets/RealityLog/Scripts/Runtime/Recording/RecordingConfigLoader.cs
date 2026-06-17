#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace RealityLog.Recording
{
    public static class RecordingConfigLoader
    {
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
            if (!string.IsNullOrWhiteSpace(externalConfigPath))
            {
                var resolvedPath = ResolveExternalPath(externalConfigPath);
                if (File.Exists(resolvedPath))
                {
                    try
                    {
                        return File.ReadAllText(resolvedPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[{Constants.LOG_TAG}] Failed to read recording config JSON: {ex.Message}");
                        return string.Empty;
                    }
                }

                Debug.LogError($"[{Constants.LOG_TAG}] Recording config JSON was not found: {resolvedPath}");
            }

            return configAsset != null ? configAsset.text : string.Empty;
        }

        private static string ResolveExternalPath(string externalConfigPath)
        {
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
