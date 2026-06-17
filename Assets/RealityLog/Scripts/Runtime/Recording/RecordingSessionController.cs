#nullable enable

using RealityLog.Camera;
using RealityLog.Depth;
using RealityLog.OVR;
using UnityEngine;

namespace RealityLog.Recording
{
    public sealed class RecordingSessionController : MonoBehaviour
    {
        [SerializeField] private TextAsset? configJson = null;
        [SerializeField] private string externalConfigPath = string.Empty;
        [SerializeField] private NativeCameraRecorder[] cameraRecorders = new NativeCameraRecorder[0];
        [SerializeField] private DepthMapExporter? depthExporter = null;
        [SerializeField] private PoseLogger? poseLogger = null;
        [SerializeField] private bool startRecordingOnStart = false;
        [SerializeField] private bool closeCamerasOnStop = true;

        private readonly RecordingSessionPathProvider pathProvider = new();
        private RecordingSessionConfig? activeConfig;
        private RecordingSessionPaths? activePaths;
        private bool recording;

        public RecordingSessionPaths? ActivePaths => activePaths;

        public void SetRecordingEnabled(bool enabled)
        {
            if (enabled)
            {
                StartRecording();
            }
            else
            {
                StopRecording();
            }
        }

        public bool StartRecording()
        {
            if (recording)
            {
                return true;
            }

            activeConfig = RecordingConfigLoader.Load(configJson, externalConfigPath);
            activePaths = pathProvider.Create(activeConfig);

            ConfigureModules(activeConfig, activePaths);

            if (!StartCameraRecorders())
            {
                StopRecording();
                return false;
            }

            if (activeConfig.depth.enabled)
            {
                depthExporter?.StartExport();
            }

            if (activeConfig.pose.enabled)
            {
                poseLogger?.StartLogging();
            }

            recording = true;
            Debug.Log($"[{Constants.LOG_TAG}] Recording session started: {activePaths.RootDirectoryPath}");
            return true;
        }

        public bool StopRecording()
        {
            var success = true;

            if (poseLogger != null)
            {
                poseLogger.StopLogging();
            }

            if (depthExporter != null)
            {
                depthExporter.StopExport();
            }

            foreach (var recorder in cameraRecorders)
            {
                if (recorder == null)
                {
                    continue;
                }

                success &= recorder.StopRecording();
                if (closeCamerasOnStop)
                {
                    success &= recorder.Close();
                }
            }

            recording = false;
            return success;
        }

        private void ConfigureModules(RecordingSessionConfig config, RecordingSessionPaths paths)
        {
            foreach (var recorder in cameraRecorders)
            {
                recorder?.ApplyConfiguration(config, paths);
            }

            depthExporter?.ApplyConfiguration(config.depth, paths.Depth);
            poseLogger?.ApplyConfiguration(config.pose, paths.PoseCsvFilePath);
        }

        private bool StartCameraRecorders()
        {
            if (activeConfig == null || !activeConfig.camera.enabled)
            {
                return true;
            }

            var success = true;
            foreach (var recorder in cameraRecorders)
            {
                if (recorder == null || !recorder.IsEnabledByConfiguration)
                {
                    continue;
                }

                success &= recorder.StartRecording();
                if (!success)
                {
                    break;
                }
            }

            return success;
        }

        private void Start()
        {
            if (startRecordingOnStart)
            {
                StartRecording();
            }
        }

        private void OnDestroy()
        {
            StopRecording();
        }
    }
}
