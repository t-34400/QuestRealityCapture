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

            var isNativeCamera2 = config.camera.backend == RecordingSessionConfig.CameraBackend.NativeCamera2;
            var isMruk = config.camera.backend == RecordingSessionConfig.CameraBackend.Mruk;
            var leftCamera = CreateCameraPaths(rootDirectoryPath, config.camera.left, isNativeCamera2);
            var rightCamera = CreateCameraPaths(rootDirectoryPath, config.camera.right, isNativeCamera2);
            var leftMrukCamera = CreateMrukCameraPaths(rootDirectoryPath, config.camera.left, isMruk);
            var rightMrukCamera = CreateMrukCameraPaths(rootDirectoryPath, config.camera.right, isMruk);
            var depth = CreateDepthPaths(rootDirectoryPath, config.depth);
            var pose = CreatePosePaths(rootDirectoryPath, config.pose);

            return new RecordingSessionPaths(
                sessionName,
                rootDirectoryPath,
                Path.Combine(rootDirectoryPath, "session_info.json"),
                leftCamera,
                rightCamera,
                leftMrukCamera,
                rightMrukCamera,
                Path.Combine(rootDirectoryPath, config.camera.mrukStereoPairFileName),
                depth,
                pose);
        }

        private static RecordingSessionPaths.CameraPaths CreateCameraPaths(
            string rootDirectoryPath,
            RecordingSessionConfig.CameraSideConfig config,
            bool createDirectory)
        {
            var imageDirectoryPath = Path.Combine(rootDirectoryPath, config.imageDirectoryName);
            if (createDirectory)
            {
                Directory.CreateDirectory(imageDirectoryPath);
            }

            return new RecordingSessionPaths.CameraPaths(
                imageDirectoryPath,
                Path.Combine(rootDirectoryPath, config.metadataFileName),
                Path.Combine(rootDirectoryPath, config.formatInfoFileName));
        }

        private static RecordingSessionPaths.MrukCameraPaths CreateMrukCameraPaths(
            string rootDirectoryPath,
            RecordingSessionConfig.CameraSideConfig config,
            bool createDirectory)
        {
            var imageDirectoryPath = Path.Combine(rootDirectoryPath, config.mrukImageDirectoryName);
            if (createDirectory)
            {
                Directory.CreateDirectory(imageDirectoryPath);
            }

            return new RecordingSessionPaths.MrukCameraPaths(
                imageDirectoryPath,
                Path.Combine(rootDirectoryPath, config.mrukIntrinsicsFileName),
                Path.Combine(rootDirectoryPath, config.mrukFrameMetadataFileName));
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
