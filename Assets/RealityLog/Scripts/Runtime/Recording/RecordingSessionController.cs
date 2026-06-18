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
        [SerializeField] private LiveDepthCoverageVisualizer? liveDepthCoverageVisualizer = null;
        [SerializeField] private RecordingDiagnosticsController? recordingDiagnostics = null;
        [SerializeField] private PoseLogger? poseLogger = null;
        [SerializeField] private PoseLogger[] poseLoggers = new PoseLogger[0];
        [SerializeField] private bool startRecordingOnStart = false;
        [SerializeField] private bool closeCamerasOnStop = true;
        [SerializeField, Min(0f)] private float recordingToggleCooldownSeconds = 1f;

        private readonly RecordingSessionPathProvider pathProvider = new();
        private readonly List<NativeCameraRecorder> startedCameraRecorders = new();
        private readonly List<PoseLogger> startedPoseLoggers = new();
        private RecordingSessionConfig? activeConfig;
        private RecordingSessionPaths? activePaths;
        private bool depthStarted;
        private bool liveCoverageStarted;
        private bool diagnosticsStarted;
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

            StartLiveCoverage(activeConfig);
            StartRecordingDiagnostics(activeConfig);

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

            if (config.pose.enabled && !HasPoseLoggers())
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Recording session pose logging is enabled, but no pose loggers are assigned.");
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

        private bool HasPoseLoggers()
        {
            foreach (var logger in GetConfiguredPoseLoggers())
            {
                if (logger != null)
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
            liveDepthCoverageVisualizer?.ApplyConfiguration(config.liveFeedback);
            recordingDiagnostics?.ApplyConfiguration(config.liveFeedback);
            ConfigurePoseLoggers(config.pose, paths.Pose);
        }


        private void ConfigurePoseLoggers(RecordingSessionConfig.PoseConfig config, RecordingSessionPaths.PosePaths paths)
        {
            foreach (var logger in GetConfiguredPoseLoggers())
            {
                if (logger == null)
                {
                    continue;
                }

                logger.ApplyConfiguration(config.targetSaveFps, ResolvePoseFilePath(logger, paths));
            }
        }

        private IEnumerable<PoseLogger?> GetConfiguredPoseLoggers()
        {
            if (poseLoggers != null)
            {
                foreach (var logger in poseLoggers)
                {
                    yield return logger;
                }
            }

            yield return poseLogger;
        }

        private static string ResolvePoseFilePath(PoseLogger logger, RecordingSessionPaths.PosePaths paths)
        {
            // OVRPlugin.Node serializes as numeric values in Unity scenes; these are the legacy scene values.
            return (int)logger.Node switch
            {
                12 => paths.LeftControllerFilePath,
                13 => paths.RightControllerFilePath,
                _ => paths.HmdFilePath
            };
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


        private void StartLiveCoverage(RecordingSessionConfig config)
        {
            if (!config.liveFeedback.enabled || !config.liveFeedback.coverage.enabled)
            {
                return;
            }

            if (liveDepthCoverageVisualizer == null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Live depth coverage is enabled, but no LiveDepthCoverageVisualizer is assigned.");
                return;
            }

            if (!liveDepthCoverageVisualizer.TryStartVisualization())
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Live depth coverage failed to start. Recording will continue without coverage visualization.");
                return;
            }

            liveCoverageStarted = true;
        }


        private void StartRecordingDiagnostics(RecordingSessionConfig config)
        {
            if (!config.liveFeedback.enabled || !config.liveFeedback.diagnostics.enabled)
            {
                return;
            }

            if (recordingDiagnostics == null)
            {
                recordingDiagnostics = GetComponent<RecordingDiagnosticsController>() ?? gameObject.AddComponent<RecordingDiagnosticsController>();
            }

            recordingDiagnostics.ApplyConfiguration(config.liveFeedback);
            if (!recordingDiagnostics.TryStartDiagnostics())
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Recording diagnostics failed to start. Recording will continue without diagnostics overlays.");
                return;
            }

            diagnosticsStarted = true;
        }

        private bool StartPoseLogger(RecordingSessionConfig config)
        {
            if (!config.pose.enabled)
            {
                return true;
            }

            startedPoseLoggers.Clear();
            foreach (var logger in GetConfiguredPoseLoggers())
            {
                if (logger == null)
                {
                    continue;
                }

                startedPoseLoggers.Add(logger);
                if (!logger.TryStartLogging())
                {
                    return false;
                }
            }

            return true;
        }

        private bool StopStartedModules()
        {
            var success = true;

            for (var i = startedPoseLoggers.Count - 1; i >= 0; --i)
            {
                startedPoseLoggers[i].StopLogging();
            }

            startedPoseLoggers.Clear();

            if (diagnosticsStarted && recordingDiagnostics != null)
            {
                recordingDiagnostics.StopDiagnostics();
                diagnosticsStarted = false;
            }

            if (liveCoverageStarted && liveDepthCoverageVisualizer != null)
            {
                liveDepthCoverageVisualizer.StopVisualization();
                liveCoverageStarted = false;
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
            liveCoverageStarted = false;
            diagnosticsStarted = false;
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
