#nullable enable

using UnityEngine;

namespace RealityLog.Camera
{
    public class NativeCameraRecorder : MonoBehaviour, ICameraRecorder
    {
        [SerializeField] private CameraPermissionManager cameraPermissionManager = default!;
        [SerializeField] private JavaCameraMetadataProvider cameraMetadataProvider = default!;
        [SerializeField] private CameraPosition cameraPosition = CameraPosition.Left;
        [SerializeField] private string dataDirectoryName = string.Empty;
        [SerializeField] private string imageSubdirName = "left_camera";
        [SerializeField] private string cameraMetaDataFileName = "left_camera_characteristics.json";
        [SerializeField] private string formatInfoFileName = "left_camera_image_format.json";
        [SerializeField] private int targetSaveFps = 0;
        [SerializeField] private bool preferOpenByCameraId = true;

        private readonly RecordingPathProvider pathProvider = new();
        private readonly RecordingMetadataWriter metadataWriter = new();

        private bool initialized;
        private bool opened;
        private bool recording;
        private CameraMetadata? selectedMetadata;

        public string DataDirectoryName
        {
            get => dataDirectoryName;
            set => dataDirectoryName = value;
        }

        public NativeCameraStats LastStats { get; private set; }

        public void SetDataDirectoryName(string value)
        {
            dataDirectoryName = value;
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

            var paths = pathProvider.Create(
                dataDirectoryName,
                imageSubdirName,
                cameraMetaDataFileName,
                formatInfoFileName);

            metadataWriter.WriteCameraMetadata(paths.CameraMetadataFilePath, metadata);

            var size = metadata.sensor.pixelArraySize;
            if (!CheckResult(NativeCameraBridge.Initialize(size.width, size.height, paths.ImageDirectoryPath, paths.FormatInfoFilePath), "initialize native camera"))
            {
                return false;
            }

            if (!CheckResult(NativeCameraBridge.SetSaveFrameRate(targetSaveFps), "set native save frame rate"))
            {
                NativeCameraBridge.Close();
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
                ? NativeCameraBridge.OpenById(metadata.cameraId)
                : NativeCameraBridge.Open(ToNativePosition(cameraPosition));

            if (!CheckResult(result, "open native camera"))
            {
                return false;
            }

            opened = true;
            Debug.Log($"[{Constants.LOG_TAG}] Native camera opened. ID={NativeCameraBridge.GetLastOpenedCameraId()}");
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

            if (!CheckResult(NativeCameraBridge.StartRecording(), "start native recording"))
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

            var result = NativeCameraBridge.StopRecording();
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

            var result = NativeCameraBridge.Close();
            initialized = false;
            opened = false;
            selectedMetadata = null;
            RefreshStats();
            return CheckResult(result, "close native camera");
        }

        public bool RefreshStats()
        {
            var result = NativeCameraBridge.GetStats(out var stats);
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
        }

        private CameraMetadata? ResolveSelectedMetadata()
        {
            return ResolveMetadataProvider()?.GetMetadata(cameraPosition);
        }

        private ICameraMetadataProvider? ResolveMetadataProvider()
        {
            if (cameraMetadataProvider != null)
            {
                return cameraMetadataProvider;
            }

            if (cameraPermissionManager == null)
            {
                return null;
            }

            cameraMetadataProvider = cameraPermissionManager.GetComponent<JavaCameraMetadataProvider>()
                ?? cameraPermissionManager.gameObject.AddComponent<JavaCameraMetadataProvider>();
            cameraMetadataProvider.Configure(cameraPermissionManager);
            return cameraMetadataProvider;
        }

        private static NativeCameraPosition ToNativePosition(CameraPosition position)
        {
            return position switch
            {
                CameraPosition.Right => NativeCameraPosition.Right,
                _ => NativeCameraPosition.Left
            };
        }

        private static bool CheckResult(NativeCameraResult result, string operation)
        {
            if (result == NativeCameraResult.Ok)
            {
                return true;
            }

            Debug.LogError($"[{Constants.LOG_TAG}] Failed to {operation}: {result}. {NativeCameraBridge.GetLastError()}");
            return false;
        }
    }
}
