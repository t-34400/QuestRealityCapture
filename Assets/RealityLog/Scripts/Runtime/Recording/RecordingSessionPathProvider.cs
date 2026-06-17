#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace RealityLog.Recording
{
    public sealed class RecordingSessionPathProvider
    {
        public RecordingSessionPaths Create(RecordingSessionConfig config)
        {
            var sessionName = DateTime.Now.ToString(NormalizeSessionNameFormat(config.sessionNameFormat));
            var rootDirectoryPath = Path.Combine(Application.persistentDataPath, sessionName);
            Directory.CreateDirectory(rootDirectoryPath);

            var leftCamera = CreateCameraPaths(rootDirectoryPath, config.camera.left);
            var rightCamera = CreateCameraPaths(rootDirectoryPath, config.camera.right);
            var depth = CreateDepthPaths(rootDirectoryPath, config.depth);
            var pose = CreatePosePaths(rootDirectoryPath, config.pose);

            return new RecordingSessionPaths(sessionName, rootDirectoryPath, leftCamera, rightCamera, depth, pose);
        }

        private static RecordingSessionPaths.CameraPaths CreateCameraPaths(
            string rootDirectoryPath,
            RecordingSessionConfig.CameraSideConfig config)
        {
            var imageDirectoryPath = Path.Combine(rootDirectoryPath, config.imageDirectoryName);
            Directory.CreateDirectory(imageDirectoryPath);

            return new RecordingSessionPaths.CameraPaths(
                imageDirectoryPath,
                Path.Combine(rootDirectoryPath, config.metadataFileName),
                Path.Combine(rootDirectoryPath, config.formatInfoFileName));
        }

        private static RecordingSessionPaths.PosePaths CreatePosePaths(
            string rootDirectoryPath,
            RecordingSessionConfig.PoseConfig config)
        {
            return new RecordingSessionPaths.PosePaths(
                Path.Combine(rootDirectoryPath, config.hmdFileName),
                Path.Combine(rootDirectoryPath, config.leftControllerFileName),
                Path.Combine(rootDirectoryPath, config.rightControllerFileName));
        }

        private static RecordingSessionPaths.DepthPaths CreateDepthPaths(
            string rootDirectoryPath,
            RecordingSessionConfig.DepthConfig config)
        {
            var leftDirectoryPath = Path.Combine(rootDirectoryPath, config.leftDirectoryName);
            var rightDirectoryPath = Path.Combine(rootDirectoryPath, config.rightDirectoryName);
            Directory.CreateDirectory(leftDirectoryPath);
            Directory.CreateDirectory(rightDirectoryPath);

            return new RecordingSessionPaths.DepthPaths(
                leftDirectoryPath,
                rightDirectoryPath,
                Path.Combine(rootDirectoryPath, config.leftDescriptorFileName),
                Path.Combine(rootDirectoryPath, config.rightDescriptorFileName));
        }

        private static string NormalizeSessionNameFormat(string format)
        {
            const string defaultFormat = "yyyyMMdd_HHmmss";
            if (string.IsNullOrWhiteSpace(format))
            {
                return defaultFormat;
            }

            try
            {
                _ = DateTime.Now.ToString(format);
                return format;
            }
            catch (FormatException ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Invalid sessionNameFormat '{format}': {ex.Message}. Using {defaultFormat}.");
                return defaultFormat;
            }
        }
    }
}
