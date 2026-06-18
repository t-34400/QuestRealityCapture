#nullable enable

using System;
using RealityLog.OVR;
using UnityEngine;

namespace RealityLog.Recording
{
    public enum TrackingQualityState
    {
        Unknown,
        Ok,
        Suspect,
        JumpDetected
    }

    public readonly struct TrackingPoseSample
    {
        public TrackingPoseSample(double timestamp, Vector3 position, Quaternion rotation)
        {
            this.timestamp = timestamp;
            this.position = position;
            this.rotation = rotation;
        }

        public readonly double timestamp;
        public readonly Vector3 position;
        public readonly Quaternion rotation;
    }

    public readonly struct TrackingQualityEvent
    {
        public TrackingQualityEvent(
            double timestamp,
            Vector3 position,
            TrackingQualityState state,
            string reason,
            int segmentId,
            float positionDeltaMeters,
            float rotationDeltaDegrees)
        {
            this.timestamp = timestamp;
            this.position = position;
            this.state = state;
            this.reason = reason;
            this.segmentId = segmentId;
            this.positionDeltaMeters = positionDeltaMeters;
            this.rotationDeltaDegrees = rotationDeltaDegrees;
        }

        public readonly double timestamp;
        public readonly Vector3 position;
        public readonly TrackingQualityState state;
        public readonly string reason;
        public readonly int segmentId;
        public readonly float positionDeltaMeters;
        public readonly float rotationDeltaDegrees;
    }

    public sealed class TrackingQualityMonitor : MonoBehaviour
    {
        [SerializeField] private OVRPlugin.Node node = OVRPlugin.Node.Head;
        [SerializeField] private PoseStateMode mode = PoseStateMode.Immediate;
        [SerializeField] private Transform? trackingSpace = null;

        private RecordingDiagnosticsSettings settings = new(false, true, true, true, 0.3f, 30.0f);
        private TrackingPoseSample previousSample;
        private bool hasPreviousSample;
        private bool isMonitoring;
        private int segmentId;
        private double lastEventTimestamp = double.NegativeInfinity;

        public event Action<TrackingPoseSample>? PoseSampled;
        public event Action<TrackingQualityEvent>? TrackingEventRaised;

        public TrackingQualityState CurrentState { get; private set; } = TrackingQualityState.Unknown;
        public TrackingQualityEvent? LastEvent { get; private set; }
        public int SegmentId => segmentId;
        public bool IsMonitoring => isMonitoring;

        public void ApplyConfiguration(RecordingDiagnosticsSettings diagnosticsSettings)
        {
            settings = diagnosticsSettings;
        }

        public bool TryStartMonitoring()
        {
            StopMonitoring();

            if (!settings.enabled)
            {
                return true;
            }

            hasPreviousSample = false;
            segmentId = 0;
            lastEventTimestamp = double.NegativeInfinity;
            LastEvent = null;
            CurrentState = TrackingQualityState.Unknown;
            isMonitoring = true;
            return true;
        }

        public void StopMonitoring()
        {
            isMonitoring = false;
            hasPreviousSample = false;
            CurrentState = TrackingQualityState.Unknown;
        }

        private void Update()
        {
            if (!isMonitoring)
            {
                return;
            }

            var sample = ReadPoseSample();
            PoseSampled?.Invoke(sample);
            EvaluateSample(sample);
        }

        private TrackingPoseSample ReadPoseSample()
        {
            var poseState = mode switch
            {
                PoseStateMode.Immediate => OVRPlugin.GetNodePoseStateImmediate(node),
                PoseStateMode.Raw => OVRPlugin.GetNodePoseStateRaw(node, OVRPlugin.Step.Render),
                _ => OVRPlugin.PoseStatef.identity
            };

            var pose = poseState.Pose.ToOVRPose();
            var position = pose.position;
            var rotation = pose.orientation;

            if (trackingSpace != null)
            {
                position = trackingSpace.TransformPoint(position);
                rotation = trackingSpace.rotation * rotation;
            }

            return new TrackingPoseSample(poseState.Time, position, rotation);
        }

        private void EvaluateSample(TrackingPoseSample sample)
        {
            if (!hasPreviousSample)
            {
                previousSample = sample;
                hasPreviousSample = true;
                CurrentState = TrackingQualityState.Ok;
                return;
            }

            var positionDelta = Vector3.Distance(previousSample.position, sample.position);
            var rotationDelta = Quaternion.Angle(previousSample.rotation, sample.rotation);
            var jumped = positionDelta >= settings.positionJumpMeters || rotationDelta >= settings.rotationJumpDegrees;

            if (jumped && sample.timestamp > lastEventTimestamp)
            {
                segmentId += 1;
                CurrentState = TrackingQualityState.JumpDetected;
                var reason = positionDelta >= settings.positionJumpMeters ? "position_jump" : "rotation_jump";
                var trackingEvent = new TrackingQualityEvent(
                    sample.timestamp,
                    sample.position,
                    CurrentState,
                    reason,
                    segmentId,
                    positionDelta,
                    rotationDelta);
                LastEvent = trackingEvent;
                lastEventTimestamp = sample.timestamp;
                TrackingEventRaised?.Invoke(trackingEvent);
            }
            else
            {
                CurrentState = TrackingQualityState.Ok;
            }

            previousSample = sample;
        }
    }
}
