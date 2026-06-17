#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace RealityLog.Camera
{
    public class NativeCameraRecorder : MonoBehaviour
    {
        [SerializeField] private CameraPermissionManager cameraPermissionManager = default!;
        [SerializeField] private CameraPosition cameraPosition = CameraPosition.Left;
        [SerializeField] private string dataDirectoryName = string.Empty;
        [SerializeField] private string imageSubdirName = "left_camera";
        [SerializeField] private string cameraMetaDataFileName = "left_camera_characteristics.json";
        [SerializeField] private string formatInfoFileName = "left_camera_image_format.json";
        [SerializeField] private int targetSaveFps = 0;
        [SerializeField] private bool preferOpenByCameraId = true;

        private bool initialized;
        private bool opened;
        private bool recording;

        public string DataDirectoryName
        {
            get => dataDirectoryName;
            set => dataDirectoryName = value;
        }

        public NativeCameraStats LastStats { get; private set; }

        public bool Initialize()
        {
            if (initialized)
            {
                return true;
            }

            var metadata = GetSelectedMetadata();
            if (metadata == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native camera metadata is unavailable for {cameraPosition}.");
                return false;
            }

            var dataDirPath = Path.Join(Application.persistentDataPath, dataDirectoryName);
            Directory.CreateDirectory(dataDirPath);

            WriteCameraMetadata(dataDirPath, metadata);

            var imageFileDirPath = Path.Join(dataDirPath, imageSubdirName);
            Directory.CreateDirectory(imageFileDirPath);

            var formatInfoFilePath = Path.Join(dataDirPath, formatInfoFileName);
            var size = metadata.sensor.pixelArraySize;

            if (!CheckResult(NativeCameraBridge.Initialize(size.width, size.height, imageFileDirPath, formatInfoFilePath), "initialize native camera"))
            {
                return false;
            }

            if (!CheckResult(NativeCameraBridge.SetSaveFrameRate(targetSaveFps), "set native save frame rate"))
            {
                NativeCameraBridge.Close();
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

            var metadata = GetSelectedMetadata();
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

        private CameraMetadata? GetSelectedMetadata()
        {
            return cameraPosition switch
            {
                CameraPosition.Left => cameraPermissionManager.LeftCameraMetaData,
                CameraPosition.Right => cameraPermissionManager.RightCameraMetaData,
                _ => null
            };
        }

        private void WriteCameraMetadata(string dataDirPath, CameraMetadata metadata)
        {
            var metaDataFilePath = Path.Join(dataDirPath, cameraMetaDataFileName);
            var metaDataJson = JsonUtility.ToJson(metadata);
            File.WriteAllText(metaDataFilePath, metaDataJson);
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
