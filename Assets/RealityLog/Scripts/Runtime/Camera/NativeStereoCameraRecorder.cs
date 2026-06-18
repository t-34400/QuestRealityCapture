#nullable enable

using System;
using System.IO;
using RealityLog.Recording;
using UnityEngine;

namespace RealityLog.Camera
{
    public class NativeStereoCameraRecorder : MonoBehaviour
    {
        [SerializeField] private CameraPermissionManager cameraPermissionManager = default!;
        [SerializeField] private MonoBehaviour cameraMetadataProvider = default!;
        [SerializeField] private bool allowJavaMetadataFallback = false;
        [SerializeField] private string dataDirectoryName = string.Empty;
        [SerializeField] private string leftImageSubdirName = "left_camera_raw";
        [SerializeField] private string rightImageSubdirName = "right_camera_raw";
        [SerializeField] private string leftCameraMetaDataFileName = "left_camera_characteristics.json";
        [SerializeField] private string rightCameraMetaDataFileName = "right_camera_characteristics.json";
        [SerializeField] private string leftFormatInfoFileName = "left_camera_image_format.json";
        [SerializeField] private string rightFormatInfoFileName = "right_camera_image_format.json";
        [SerializeField] private string pairCsvFileName = "stereo_pairs.csv";
        [SerializeField] private int targetSaveFps = 0;
        [SerializeField] private bool preferOpenByCameraId = true;
        [SerializeField, Min(0.001f)] private float maxTimeDeltaSeconds = 0.02f;

        private readonly RecordingMetadataWriter metadataWriter = new();

        private IntPtr sessionHandle = IntPtr.Zero;
        private bool initialized;
        private bool opened;
        private bool recording;
        private CameraMetadata? leftMetadata;
        private CameraMetadata? rightMetadata;
        private StereoRecordingPaths? configuredPaths;

        public NativeStereoCameraStats LastStats { get; private set; }

        public void SetDataDirectoryName(string value)
        {
            dataDirectoryName = value;
            configuredPaths = null;
        }

        public void ApplyConfiguration(RecordingSessionConfig config, RecordingSessionPaths paths)
        {
            targetSaveFps = config.camera.targetSaveFps;
            preferOpenByCameraId = config.camera.preferOpenByCameraId;
            allowJavaMetadataFallback = config.camera.allowJavaMetadataFallback;
            leftImageSubdirName = config.camera.left.imageDirectoryName;
            rightImageSubdirName = config.camera.right.imageDirectoryName;
            leftCameraMetaDataFileName = config.camera.left.metadataFileName;
            rightCameraMetaDataFileName = config.camera.right.metadataFileName;
            leftFormatInfoFileName = config.camera.left.formatInfoFileName;
            rightFormatInfoFileName = config.camera.right.formatInfoFileName;
            pairCsvFileName = config.camera.stereoPairFileName;
            maxTimeDeltaSeconds = config.camera.stereoMaxTimeDeltaSeconds;
            dataDirectoryName = paths.SessionName;
            configuredPaths = new StereoRecordingPaths(
                paths.LeftCamera.ImageDirectoryPath,
                paths.RightCamera.ImageDirectoryPath,
                paths.LeftCamera.MetadataFilePath,
                paths.RightCamera.MetadataFilePath,
                paths.LeftCamera.FormatInfoFilePath,
                paths.RightCamera.FormatInfoFilePath,
                Path.Combine(paths.RootDirectoryPath, pairCsvFileName));

            if ((!config.camera.enabled || !config.camera.left.enabled || !config.camera.right.enabled)
                && (initialized || opened || recording))
            {
                Close();
            }
        }

        public bool Initialize()
        {
            if (initialized)
            {
                return true;
            }

            if (!EnsureCameraPermission())
            {
                return false;
            }

            leftMetadata = ResolveSelectedMetadata(CameraPosition.Left);
            rightMetadata = ResolveSelectedMetadata(CameraPosition.Right);
            if (leftMetadata == null || rightMetadata == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native stereo camera metadata is unavailable.");
                return false;
            }

            var size = leftMetadata.sensor.pixelArraySize;
            if (rightMetadata.sensor.pixelArraySize.width != size.width || rightMetadata.sensor.pixelArraySize.height != size.height)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native stereo camera sizes differ. left={size.width}x{size.height}, right={rightMetadata.sensor.pixelArraySize.width}x{rightMetadata.sensor.pixelArraySize.height}");
                return false;
            }

            var paths = ResolveRecordingPaths();
            metadataWriter.WriteCameraMetadata(paths.LeftMetadataFilePath, leftMetadata);
            metadataWriter.WriteCameraMetadata(paths.RightMetadataFilePath, rightMetadata);

            if (!EnsureSession())
            {
                return false;
            }

            var maxDeltaNs = Math.Max(1L, (long)(maxTimeDeltaSeconds * 1_000_000_000.0f));
            if (!CheckResult(NativeCameraBridge.InitializeStereoSession(
                    sessionHandle,
                    size.width,
                    size.height,
                    paths.LeftImageDirectoryPath,
                    paths.RightImageDirectoryPath,
                    paths.LeftFormatInfoFilePath,
                    paths.RightFormatInfoFilePath,
                    paths.PairCsvFilePath,
                    maxDeltaNs),
                "initialize native stereo camera"))
            {
                DestroySession();
                return false;
            }

            if (!CheckResult(NativeCameraBridge.SetStereoSessionSaveFrameRate(sessionHandle, targetSaveFps), "set native stereo save frame rate"))
            {
                DestroySession();
                return false;
            }

            initialized = true;
            return true;
        }

        public bool OpenCamera()
        {
            if (opened)
            {
                return true;
            }

            if (!Initialize())
            {
                return false;
            }

            var result = preferOpenByCameraId && leftMetadata != null && rightMetadata != null
                && !string.IsNullOrEmpty(leftMetadata.cameraId) && !string.IsNullOrEmpty(rightMetadata.cameraId)
                ? NativeCameraBridge.OpenStereoSessionByIds(sessionHandle, leftMetadata.cameraId, rightMetadata.cameraId)
                : NativeCameraBridge.OpenStereoSession(sessionHandle);

            if (!CheckResult(result, "open native stereo camera"))
            {
                return false;
            }

            opened = true;
            Debug.Log($"[{Constants.LOG_TAG}] Native stereo camera opened. left={NativeCameraBridge.GetStereoSessionLastOpenedLeftCameraId(sessionHandle)}, right={NativeCameraBridge.GetStereoSessionLastOpenedRightCameraId(sessionHandle)}");
            return true;
        }

        public bool StartRecording()
        {
            if (recording)
            {
                return true;
            }

            if (!OpenCamera())
            {
                return false;
            }

            if (!CheckResult(NativeCameraBridge.StartStereoSessionRecording(sessionHandle), "start native stereo recording"))
            {
                return false;
            }

            recording = true;
            return true;
        }

        public bool StopRecording()
        {
            if (!recording)
            {
                return true;
            }

            var result = NativeCameraBridge.StopStereoSessionRecording(sessionHandle);
            recording = false;
            RefreshStats();
            return CheckResult(result, "stop native stereo recording");
        }

        public bool Close()
        {
            if (recording)
            {
                StopRecording();
            }

            if (!initialized && !opened)
            {
                return true;
            }

            var result = NativeCameraBridge.CloseStereoSession(sessionHandle);
            initialized = false;
            opened = false;
            leftMetadata = null;
            rightMetadata = null;
            RefreshStats();
            var ok = CheckResult(result, "close native stereo camera");
            DestroySession();
            return ok;
        }

        public bool RefreshStats()
        {
            if (sessionHandle == IntPtr.Zero)
            {
                return false;
            }

            var result = NativeCameraBridge.GetStereoSessionStats(sessionHandle, out var stats);
            if (result == NativeCameraResult.Ok)
            {
                LastStats = stats;
                return true;
            }

            return false;
        }

        private void OnDestroy()
        {
            Close();
            DestroySession();
        }

        private bool EnsureCameraPermission()
        {
            if (cameraPermissionManager == null)
            {
                cameraPermissionManager = FindAnyObjectByType<CameraPermissionManager>();
            }

            if (cameraPermissionManager == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] CameraPermissionManager is not assigned for native stereo recording.");
                return false;
            }

            if (cameraPermissionManager.HasRequiredCameraPermission)
            {
                return true;
            }

            cameraPermissionManager.RequestCameraPermissionIfNeeded();
            Debug.LogError($"[{Constants.LOG_TAG}] Camera permission has not been granted for native stereo recording.");
            return false;
        }

        private bool EnsureSession()
        {
            if (sessionHandle != IntPtr.Zero)
            {
                return true;
            }

            var result = NativeCameraBridge.CreateStereoSession(out sessionHandle);
            if (result == NativeCameraResult.Ok && sessionHandle != IntPtr.Zero)
            {
                return true;
            }

            Debug.LogError($"[{Constants.LOG_TAG}] Failed to create native stereo camera session: {result}.");
            sessionHandle = IntPtr.Zero;
            return false;
        }

        private void DestroySession()
        {
            if (sessionHandle == IntPtr.Zero)
            {
                return;
            }

            NativeCameraBridge.DestroyStereoSession(sessionHandle);
            sessionHandle = IntPtr.Zero;
        }

        private StereoRecordingPaths ResolveRecordingPaths()
        {
            if (configuredPaths != null)
            {
                return configuredPaths;
            }

            var root = Path.Combine(Application.persistentDataPath, dataDirectoryName);
            var leftDirectory = Path.Combine(root, leftImageSubdirName);
            var rightDirectory = Path.Combine(root, rightImageSubdirName);
            Directory.CreateDirectory(leftDirectory);
            Directory.CreateDirectory(rightDirectory);
            return new StereoRecordingPaths(
                leftDirectory,
                rightDirectory,
                Path.Combine(root, leftCameraMetaDataFileName),
                Path.Combine(root, rightCameraMetaDataFileName),
                Path.Combine(root, leftFormatInfoFileName),
                Path.Combine(root, rightFormatInfoFileName),
                Path.Combine(root, pairCsvFileName));
        }

        private ICameraMetadataProvider? ResolveMetadataProvider()
        {
            if (cameraMetadataProvider is ICameraMetadataProvider configuredProvider
                && (allowJavaMetadataFallback || !(cameraMetadataProvider is JavaCameraMetadataProvider)))
            {
                return configuredProvider;
            }

            var nativeProvider = GetComponent<NativeCameraMetadataProvider>()
                ?? gameObject.AddComponent<NativeCameraMetadataProvider>();
            cameraMetadataProvider = nativeProvider;
            return nativeProvider;
        }

        private CameraMetadata? ResolveSelectedMetadata(CameraPosition position)
        {
            var metadata = ResolveMetadataProvider()?.GetMetadata(position);
            if (metadata != null || !allowJavaMetadataFallback)
            {
                return metadata;
            }

            var javaProvider = ResolveJavaMetadataProvider();
            return javaProvider?.GetMetadata(position);
        }

        private ICameraMetadataProvider? ResolveJavaMetadataProvider()
        {
            if (cameraPermissionManager == null)
            {
                return null;
            }

            var javaProvider = cameraPermissionManager.GetComponent<JavaCameraMetadataProvider>()
                ?? cameraPermissionManager.gameObject.AddComponent<JavaCameraMetadataProvider>();
            javaProvider.Configure(cameraPermissionManager);
            return javaProvider;
        }

        private bool CheckResult(NativeCameraResult result, string operation)
        {
            if (result == NativeCameraResult.Ok)
            {
                return true;
            }

            Debug.LogError($"[{Constants.LOG_TAG}] Failed to {operation}: {result}. {NativeCameraBridge.GetStereoSessionLastError(sessionHandle)}");
            return false;
        }

        private sealed class StereoRecordingPaths
        {
            public StereoRecordingPaths(
                string leftImageDirectoryPath,
                string rightImageDirectoryPath,
                string leftMetadataFilePath,
                string rightMetadataFilePath,
                string leftFormatInfoFilePath,
                string rightFormatInfoFilePath,
                string pairCsvFilePath)
            {
                LeftImageDirectoryPath = leftImageDirectoryPath;
                RightImageDirectoryPath = rightImageDirectoryPath;
                LeftMetadataFilePath = leftMetadataFilePath;
                RightMetadataFilePath = rightMetadataFilePath;
                LeftFormatInfoFilePath = leftFormatInfoFilePath;
                RightFormatInfoFilePath = rightFormatInfoFilePath;
                PairCsvFilePath = pairCsvFilePath;
            }

            public string LeftImageDirectoryPath { get; }
            public string RightImageDirectoryPath { get; }
            public string LeftMetadataFilePath { get; }
            public string RightMetadataFilePath { get; }
            public string LeftFormatInfoFilePath { get; }
            public string RightFormatInfoFilePath { get; }
            public string PairCsvFilePath { get; }
        }
    }
}
