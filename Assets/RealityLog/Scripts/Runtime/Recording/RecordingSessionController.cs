#nullable enable

using System.Collections.Generic;
using RealityLog.Camera;
using RealityLog.Depth;
using RealityLog.OVR;
using UnityEngine;

namespace RealityLog.Recording
{
    public sealed class RecordingSessionController : MonoBehaviour, IRecordingSessionController
    {
        [SerializeField] private TextAsset? configJson = null;
        [SerializeField] private string externalConfigPath = string.Empty;
        [SerializeField] private NativeCameraRecorder[] cameraRecorders = new NativeCameraRecorder[0];
        [SerializeField] private DepthMapExporter? depthExporter = null;
        [SerializeField] private PoseLogger? poseLogger = null;
        [SerializeField] private bool startRecordingOnStart = false;
        [SerializeField] private bool closeCamerasOnStop = true;

        private readonly RecordingSessionPathProvider pathProvider = new();
        private readonly List<NativeCameraRecorder> startedCameraRecorders = new();
        private RecordingSessionConfig? activeConfig;
        private RecordingSessionPaths? activePaths;
        private bool depthStarted;
        private bool poseStarted;
        private bool recording;

        public bool IsRecording => recording;
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
            if (!ValidateConfiguredModules(activeConfig))
            {
                activeConfig = null;
                return false;
            }

            activePaths = pathProvider.Create(activeConfig);
            ConfigureModules(activeConfig, activePaths);

            if (!StartCameraRecorders(activeConfig))
            {
                StopStartedModules();
                ClearActiveSessionState();
                return false;
            }

            if (!StartDepthExporter(activeConfig))
            {
                StopStartedModules();
                ClearActiveSessionState();
                return false;
            }

            if (!StartPoseLogger(activeConfig))
            {
                StopStartedModules();
                ClearActiveSessionState();
                return false;
            }

            recording = true;
            Debug.Log($"[{Constants.LOG_TAG}] Recording session started: {activePaths.RootDirectoryPath}");
            return true;
        }

        public bool StopRecording()
        {
            var success = StopStartedModules();
            ClearActiveSessionState();
            return success;
        }

        private bool ValidateConfiguredModules(RecordingSessionConfig config)
        {
            if (config.camera.enabled && !HasEnabledCameraRecorder(config))
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Recording session requires at least one enabled native camera recorder.");
                return false;
            }

            if (config.depth.enabled && depthExporter == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Recording session depth export is enabled, but no depth exporter is assigned.");
                return false;
            }

            if (config.pose.enabled && poseLogger == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Recording session pose logging is enabled, but no pose logger is assigned.");
                return false;
            }

            return true;
        }

        private bool HasEnabledCameraRecorder(RecordingSessionConfig config)
        {
            foreach (var recorder in cameraRecorders)
            {
                if (recorder == null)
                {
                    continue;
                }

                var side = recorder.CameraPosition == CameraPosition.Right ? config.camera.right : config.camera.left;
                if (side.enabled)
                {
                    return true;
                }
            }

            return false;
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

        private bool StartCameraRecorders(RecordingSessionConfig config)
        {
            if (!config.camera.enabled)
            {
                return true;
            }

            startedCameraRecorders.Clear();
            foreach (var recorder in cameraRecorders)
            {
                if (recorder == null || !recorder.IsEnabledByConfiguration)
                {
                    continue;
                }

                if (!recorder.StartRecording())
                {
                    return false;
                }

                startedCameraRecorders.Add(recorder);
            }

            return true;
        }

        private bool StartDepthExporter(RecordingSessionConfig config)
        {
            if (!config.depth.enabled)
            {
                return true;
            }

            depthExporter!.StartExport();
            depthStarted = true;
            return true;
        }

        private bool StartPoseLogger(RecordingSessionConfig config)
        {
            if (!config.pose.enabled)
            {
                return true;
            }

            poseLogger!.StartLogging();
            poseStarted = true;
            return true;
        }

        private bool StopStartedModules()
        {
            var success = true;

            if (poseStarted && poseLogger != null)
            {
                poseLogger.StopLogging();
                poseStarted = false;
            }

            if (depthStarted && depthExporter != null)
            {
                depthExporter.StopExport();
                depthStarted = false;
            }

            for (var i = startedCameraRecorders.Count - 1; i >= 0; --i)
            {
                var recorder = startedCameraRecorders[i];
                success &= recorder.StopRecording();
                if (closeCamerasOnStop)
                {
                    success &= recorder.Close();
                }
            }

            startedCameraRecorders.Clear();
            return success;
        }

        private void ClearActiveSessionState()
        {
            recording = false;
            activeConfig = null;
            activePaths = null;
            depthStarted = false;
            poseStarted = false;
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
