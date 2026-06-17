#nullable enable

using System;
using System.IO;
using RealityLog.Recording;
using UnityEngine;

namespace RealityLog.OVR
{
    public enum PoseStateMode
    {
        Immediate,
        Raw
    }

    public class PoseLogger : MonoBehaviour
    {
        private static readonly string[] HEADER = new string[]
            {
                "unix_time", "ovr_timestamp",
                "pos_x", "pos_y", "pos_z", 
                "rot_x", "rot_y", "rot_z", "rot_w", 
            };

        [SerializeField] private OVRPlugin.Node node = OVRPlugin.Node.Head;
        [SerializeField] private PoseStateMode mode = PoseStateMode.Immediate;
        [SerializeField] private string fileName = "poses.csv";
        [SerializeField] private string directoryName = "";
        [SerializeField] private int targetSaveFps = 0;
        [SerializeField] private bool startLoggingOnStart = false;
        [Header("Optional")]
        [SerializeField] private Transform trackingSpace = default!;

        private CsvWriter? writer = null;
        private string? configuredFilePath;

        private double baseOvrTimeSec;
        private long baseUnixTimeMs;

        private double latestTimestamp;
        private double? latestSavedTimestamp;

        public string DirectoryName
        {
            get => directoryName;
            set => directoryName = value;
        }

        public void ApplyConfiguration(RecordingSessionConfig.PoseConfig config, string poseCsvFilePath)
        {
            fileName = config.fileName;
            targetSaveFps = config.targetSaveFps;
            configuredFilePath = poseCsvFilePath;
        }

        public void StartLogging()
        {
            TryStartLogging();
        }

        public bool TryStartLogging()
        {
            try
            {
                StopLogging();
                latestTimestamp = 0;
                latestSavedTimestamp = null;
                var filePath = configuredFilePath ?? Path.Combine(Application.persistentDataPath, DirectoryName, fileName);
                writer = new CsvWriter(filePath, HEADER);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to start pose logging: {ex.Message}");
                writer = null;
                return false;
            }
        }

        public void StopLogging()
        {
            try
            {
                writer?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to dispose CsvWriter: {ex.Message}");
            }

            writer = null;
        }

        private void Start()
        {
            baseOvrTimeSec = OVRPlugin.GetTimeInSeconds();
            baseUnixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Debug.Log($"[Time Log] Base OVR Time (sec): {baseOvrTimeSec}, Base Unix Time (ms): {baseUnixTimeMs}");

            if (startLoggingOnStart)
            {
                StartLogging();
            }
        }

        private void FixedUpdate()
        {
            if (writer == null)
                return;

            EnqueueRowIfNeeded(writer);
        }

        private void EnqueueRowIfNeeded(CsvWriter writer)
        {
            var poseState = mode switch 
                {
                    PoseStateMode.Immediate => OVRPlugin.GetNodePoseStateImmediate(node),
                    PoseStateMode.Raw => OVRPlugin.GetNodePoseStateRaw(node, OVRPlugin.Step.Render),
                    _ => OVRPlugin.PoseStatef.identity,
                };

            var timestamp = poseState.Time;

            if (timestamp <= latestTimestamp || !ShouldSavePose(timestamp))
            {
                return;
            }

            latestTimestamp = timestamp;
            latestSavedTimestamp = timestamp;

            var pose = poseState.Pose.ToOVRPose();

            var position = pose.position;
            var orientation = pose.orientation;

            if (trackingSpace != null)
            {
                position = trackingSpace.TransformPoint(position);
                orientation = trackingSpace.rotation * orientation;
            }

            writer.EnqueueRow(
                ConvertOvrSecToUnixTimeMs(timestamp), timestamp,
                position.x, position.y, position.z,
                orientation.x, orientation.y, orientation.z, orientation.w
            );
        }

        private bool ShouldSavePose(double timestamp)
        {
            if (targetSaveFps <= 0 || latestSavedTimestamp == null)
            {
                return true;
            }

            return timestamp - latestSavedTimestamp.Value >= 1.0 / targetSaveFps;
        }

        private long ConvertOvrSecToUnixTimeMs(double ovrTime)
        {
            var deltaSec = ovrTime - baseOvrTimeSec;
            var deltaMs = (long) (deltaSec * 1000.0);
            return baseUnixTimeMs + deltaMs;
        }

        private void OnDestroy()
        {
            writer?.Dispose();
            writer = null;
        }
    }
}