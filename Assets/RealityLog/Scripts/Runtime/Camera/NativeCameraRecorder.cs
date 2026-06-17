#nullable enable

using System;
using RealityLog.Recording;
using UnityEngine;

namespace RealityLog.Camera
{
    public class NativeCameraRecorder : MonoBehaviour, ICameraRecorder
    {
        [SerializeField] private CameraPermissionManager cameraPermissionManager = default!;
        [SerializeField] private MonoBehaviour cameraMetadataProvider = default!;
        [SerializeField] private bool allowJavaMetadataFallback = false;
        [SerializeField] private CameraPosition cameraPosition = CameraPosition.Left;
        [SerializeField] private string dataDirectoryName = string.Empty;
        [SerializeField] private string imageSubdirName = "left_camera";
        [SerializeField] private string cameraMetaDataFileName = "left_camera_characteristics.json";
        [SerializeField] private string formatInfoFileName = "left_camera_image_format.json";
        [SerializeField] private int targetSaveFps = 0;
        [SerializeField] private bool preferOpenByCameraId = true;

        private readonly RecordingPathProvider pathProvider = new();
        private readonly RecordingMetadataWriter metadataWriter = new();

        private IntPtr sessionHandle = IntPtr.Zero;
        private bool initialized;
        private bool opened;
        private bool recording;
        private CameraMetadata? selectedMetadata;
        private RecordingSessionPaths.CameraPaths? configuredPaths;

        public bool IsEnabledByConfiguration { get; private set; } = true;

        public string DataDirectoryName
        {
            get => dataDirectoryName;
            set => dataDirectoryName = value;
        }

        public NativeCameraStats LastStats { get; private set; }

        public void SetDataDirectoryName(string value)
        {
            dataDirectoryName = value;
            configuredPaths = null;
        }

        public void ApplyConfiguration(RecordingSessionConfig config, RecordingSessionPaths paths)
        {
            var side = cameraPosition == CameraPosition.Right ? config.camera.right : config.camera.left;
            IsEnabledByConfiguration = config.camera.enabled && side.enabled;
            targetSaveFps = config.camera.targetSaveFps;
            preferOpenByCameraId = config.camera.preferOpenByCameraId;
            allowJavaMetadataFallback = config.camera.allowJavaMetadataFallback;
            imageSubdirName = side.imageDirectoryName;
            cameraMetaDataFileName = side.metadataFileName;
            formatInfoFileName = side.formatInfoFileName;
            dataDirectoryName = paths.SessionName;
            configuredPaths = paths.GetCameraPaths(cameraPosition);

            if (!IsEnabledByConfiguration && (initialized || opened || recording))
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

            var metadata = ResolveSelectedMetadata();
            if (metadata == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native camera metadata is unavailable for {cameraPosition}.");
                return false;
            }

            var paths = ResolveRecordingPaths();

            metadataWriter.WriteCameraMetadata(paths.MetadataFilePath, metadata);

            var size = metadata.sensor.pixelArraySize;
            if (!EnsureSession())
            {
                return false;
            }

            if (!CheckResult(NativeCameraBridge.InitializeSession(sessionHandle, size.width, size.height, paths.ImageDirectoryPath, paths.FormatInfoFilePath), "initialize native camera"))
            {
                DestroySession();
                return false;
            }

            if (!CheckResult(NativeCameraBridge.SetSessionSaveFrameRate(sessionHandle, targetSaveFps), "set native save frame rate"))
            {
                DestroySession();
                return false;
            }

            selectedMetadata = metadata;
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

            var metadata = selectedMetadata ?? ResolveSelectedMetadata();
            if (metadata == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native camera metadata is unavailable for {cameraPosition}.");
                return false;
            }

            var result = preferOpenByCameraId && !string.IsNullOrEmpty(metadata.cameraId)
                ? NativeCameraBridge.OpenSessionById(sessionHandle, metadata.cameraId)
                : NativeCameraBridge.OpenSession(sessionHandle, ToNativePosition(cameraPosition));

            if (!CheckResult(result, "open native camera"))
            {
                return false;
            }

            opened = true;
            Debug.Log($"[{Constants.LOG_TAG}] Native camera opened. ID={NativeCameraBridge.GetSessionLastOpenedCameraId(sessionHandle)}");
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

            if (!CheckResult(NativeCameraBridge.StartSessionRecording(sessionHandle), "start native recording"))
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

            var result = NativeCameraBridge.StopSessionRecording(sessionHandle);
            recording = false;
            RefreshStats();
            return CheckResult(result, "stop native recording");
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

            var result = NativeCameraBridge.CloseSession(sessionHandle);
            initialized = false;
            opened = false;
            selectedMetadata = null;
            RefreshStats();
            var ok = CheckResult(result, "close native camera");
            DestroySession();
            return ok;
        }

        public bool RefreshStats()
        {
            if (sessionHandle == IntPtr.Zero)
            {
                return false;
            }

            var result = NativeCameraBridge.GetSessionStats(sessionHandle, out var stats);
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

        private bool EnsureSession()
        {
            if (sessionHandle != IntPtr.Zero)
            {
                return true;
            }

            var result = NativeCameraBridge.CreateSession(out sessionHandle);
            if (result == NativeCameraResult.Ok && sessionHandle != IntPtr.Zero)
            {
                return true;
            }

            Debug.LogError($"[{Constants.LOG_TAG}] Failed to create native camera session: {result}.");
            sessionHandle = IntPtr.Zero;
            return false;
        }

        private void DestroySession()
        {
            if (sessionHandle == IntPtr.Zero)
            {
                return;
            }

            NativeCameraBridge.DestroySession(sessionHandle);
            sessionHandle = IntPtr.Zero;
        }

        private RecordingSessionPaths.CameraPaths ResolveRecordingPaths()
        {
            if (configuredPaths != null)
            {
                return configuredPaths;
            }

            var legacyPaths = pathProvider.Create(
                dataDirectoryName,
                imageSubdirName,
                cameraMetaDataFileName,
                formatInfoFileName);

            return new RecordingSessionPaths.CameraPaths(
                legacyPaths.ImageDirectoryPath,
                legacyPaths.CameraMetadataFilePath,
                legacyPaths.FormatInfoFilePath);
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

        private CameraMetadata? ResolveSelectedMetadata()
        {
            var metadata = ResolveMetadataProvider()?.GetMetadata(cameraPosition);
            if (metadata != null || !allowJavaMetadataFallback)
            {
                return metadata;
            }

            var javaProvider = ResolveJavaMetadataProvider();
            return javaProvider?.GetMetadata(cameraPosition);
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

        private static NativeCameraPosition ToNativePosition(CameraPosition position)
        {
            return position switch
            {
                CameraPosition.Right => NativeCameraPosition.Right,
                _ => NativeCameraPosition.Left
            };
        }

        private bool CheckResult(NativeCameraResult result, string operation)
        {
            if (result == NativeCameraResult.Ok)
            {
                return true;
            }

            Debug.LogError($"[{Constants.LOG_TAG}] Failed to {operation}: {result}. {NativeCameraBridge.GetSessionLastError(sessionHandle)}");
            return false;
        }
    }
}
