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
        [SerializeField] private string externalConfigPath = RecordingConfigLoader.DefaultExternalConfigPath;
        [SerializeField] private NativeCameraRecorder[] cameraRecorders = new NativeCameraRecorder[0];
        [SerializeField] private DepthMapExporter? depthExporter = null;
        [SerializeField] private PoseLogger? poseLogger = null;
        [SerializeField] private bool startRecordingOnStart = false;
        [SerializeField] private bool closeCamerasOnStop = true;
        [SerializeField, Min(0f)] private float recordingToggleCooldownSeconds = 1f;

        private readonly RecordingSessionPathProvider pathProvider = new();
        private readonly List<NativeCameraRecorder> startedCameraRecorders = new();
        private RecordingSessionConfig? activeConfig;
        private RecordingSessionPaths? activePaths;
        private bool depthStarted;
        private bool poseStarted;
        private bool recording;
        private float nextRecordingToggleRealtime;

        public bool IsRecording => recording;
        public RecordingSessionPaths? ActivePaths => activePaths;

        public void SetRecordingEnabled(bool enabled)
        {
            if (enabled == recording)
            {
                return;
            }

            if (!CanAcceptRecordingToggle())
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Recording toggle ignored during cooldown.");
                return;
            }

            ArmRecordingToggleCooldown();
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

        private bool CanAcceptRecordingToggle()
        {
            return recordingToggleCooldownSeconds <= 0f || Time.realtimeSinceStartup >= nextRecordingToggleRealtime;
        }

        private void ArmRecordingToggleCooldown()
        {
            if (recordingToggleCooldownSeconds <= 0f)
            {
                return;
            }

            nextRecordingToggleRealtime = Time.realtimeSinceStartup + recordingToggleCooldownSeconds;
        }

        private bool ValidateConfiguredModules(RecordingSessionConfig config)
        {
            if (config.camera.enabled && config.camera.left.enabled && !HasEnabledCameraRecorder(CameraPosition.Left))
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Recording session left camera is enabled, but no left native camera recorder is assigned.");
                return false;
            }

            if (config.camera.enabled && config.camera.right.enabled && !HasEnabledCameraRecorder(CameraPosition.Right))
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Recording session right camera is enabled, but no right native camera recorder is assigned.");
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

        private bool HasEnabledCameraRecorder(CameraPosition position)
        {
            foreach (var recorder in cameraRecorders)
            {
                if (recorder != null && recorder.CameraPosition == position)
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

                startedCameraRecorders.Add(recorder);
                if (!recorder.StartRecording())
                {
                    return false;
                }
            }

            return true;
        }

        private bool StartDepthExporter(RecordingSessionConfig config)
        {
            if (!config.depth.enabled)
            {
                return true;
            }

            if (!depthExporter!.TryStartExport())
            {
                return false;
            }

            depthStarted = true;
            return true;
        }

        private bool StartPoseLogger(RecordingSessionConfig config)
        {
            if (!config.pose.enabled)
            {
                return true;
            }

            if (!poseLogger!.TryStartLogging())
            {
                return false;
            }

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
