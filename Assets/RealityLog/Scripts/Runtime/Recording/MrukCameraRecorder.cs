#nullable enable

using System;
using System.Globalization;
using System.IO;
using Meta.XR;
using RealityLog.Camera;
using UnityEngine;

namespace RealityLog.Recording
{
    public class MrukCameraRecorder : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess? leftCameraAccess = null;
        [SerializeField] private PassthroughCameraAccess? rightCameraAccess = null;
        [SerializeField] private bool autoDiscoverCameraAccess = true;
        [SerializeField, Min(0)] private int maxFramesPerCamera = 0;
        [SerializeField] private bool stopCameraAccessOnStop = true;

        private RecordingSessionConfig? config;
        private RecordingSessionPaths? paths;
        private MrukFrameWriter? frameWriter;
        private readonly CameraRecordingState leftState = new(PassthroughCameraAccess.CameraPositionType.Left, "Left");
        private readonly CameraRecordingState rightState = new(PassthroughCameraAccess.CameraPositionType.Right, "Right");
        private float nextSampleRealtime;
        private bool recording;
        private bool lastStopSucceeded = true;

        public bool IsRecording => recording;

        public void ApplyConfiguration(RecordingSessionConfig sessionConfig, RecordingSessionPaths sessionPaths)
        {
            config = sessionConfig;
            paths = sessionPaths;
            leftState.Enabled = sessionConfig.camera.left.enabled;
            rightState.Enabled = sessionConfig.camera.right.enabled;
        }

        public bool StartRecording()
        {
            if (recording)
            {
                return true;
            }

            if (config == null || paths == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] MRUK camera recorder has not been configured.");
                return false;
            }

            if (!ResolveCameraAccesses())
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(paths.RootDirectoryPath);
                if (leftState.Enabled)
                {
                    Directory.CreateDirectory(paths.LeftMrukCamera.ImageDirectoryPath);
                }
                if (rightState.Enabled)
                {
                    Directory.CreateDirectory(paths.RightMrukCamera.ImageDirectoryPath);
                }

                EnableConfiguredCameraAccesses();
                WriteIntrinsics(leftState, paths.LeftMrukCamera.IntrinsicsFilePath);
                WriteIntrinsics(rightState, paths.RightMrukCamera.IntrinsicsFilePath);
                frameWriter = new MrukFrameWriter(paths, leftState.Enabled, rightState.Enabled);
            }
            catch (Exception ex)
            {
                if (stopCameraAccessOnStop)
                {
                    StopConfiguredCameraAccesses();
                }
                Debug.LogError($"[{Constants.LOG_TAG}] MRUK camera recording could not start: {ex}");
                return false;
            }

            leftState.ResetRuntimeState();
            rightState.ResetRuntimeState();
            nextSampleRealtime = 0f;
            lastStopSucceeded = true;
            recording = true;
            Debug.Log($"[{Constants.LOG_TAG}] MRUK camera recording started: {paths.RootDirectoryPath}");
            return true;
        }

        public bool StopRecording()
        {
            if (frameWriter == null)
            {
                return lastStopSucceeded;
            }

            recording = false;
            var writer = frameWriter;
            frameWriter = null;
            try
            {
                writer.Complete();
                lastStopSucceeded = writer.FailedWrites == 0;
            }
            catch (Exception ex)
            {
                lastStopSucceeded = false;
                Debug.LogError($"[{Constants.LOG_TAG}] MRUK camera writer could not finish: {ex}");
            }
            finally
            {
                if (stopCameraAccessOnStop)
                {
                    StopConfiguredCameraAccesses();
                }
            }

            var summary = $"[{Constants.LOG_TAG}] MRUK camera recording stopped. "
                + $"LeftFiles={writer.LeftFilesWritten}, RightFiles={writer.RightFilesWritten}, "
                + $"LeftDropped={leftState.DroppedFrames}, RightDropped={rightState.DroppedFrames}, "
                + $"FailedWrites={writer.FailedWrites}, PeakOutstanding={writer.PeakOutstandingFrames}";
            if (!lastStopSucceeded || leftState.DroppedFrames > 0 || rightState.DroppedFrames > 0)
            {
                Debug.LogWarning(summary);
            }
            else
            {
                Debug.Log(summary);
            }
            return lastStopSucceeded;
        }

        private void OnDestroy()
        {
            StopRecording();
        }

        private void Update()
        {
            var writer = frameWriter;
            if (!recording || config == null || writer == null)
            {
                return;
            }

            if (writer.HasFailed)
            {
                StopRecording();
                return;
            }

            if (!ShouldSampleNow(config.camera.targetSaveFps))
            {
                return;
            }

            SampleCamera(leftState, writer);
            SampleCamera(rightState, writer);

            if (maxFramesPerCamera > 0
                && (!leftState.Enabled || leftState.FrameCount >= maxFramesPerCamera)
                && (!rightState.Enabled || rightState.FrameCount >= maxFramesPerCamera))
            {
                StopRecording();
            }
        }

        private bool ShouldSampleNow(int targetSaveFps)
        {
            if (targetSaveFps <= 0)
            {
                return true;
            }

            var now = Time.realtimeSinceStartup;
            if (now < nextSampleRealtime)
            {
                return false;
            }

            nextSampleRealtime = now + 1f / targetSaveFps;
            return true;
        }

        private void SampleCamera(CameraRecordingState state, MrukFrameWriter writer)
        {
            if (!state.Enabled || state.Access == null)
            {
                return;
            }

            if (maxFramesPerCamera > 0 && state.FrameCount >= maxFramesPerCamera)
            {
                return;
            }

            var access = state.Access;
            if (!access.IsPlaying)
            {
                state.LogNotPlayingWarning();
                return;
            }

            var timestampUs = ToUnixMicroseconds(access.Timestamp);
            if (timestampUs <= 0 || timestampUs == state.LastTimestampUs)
            {
                return;
            }

            state.LastTimestampUs = timestampUs;
            var frame = writer.TryRent(state.Position == PassthroughCameraAccess.CameraPositionType.Left);
            if (frame == null)
            {
                state.DroppedFrames++;
                return;
            }

            try
            {
                CaptureFrame(state, access, timestampUs, frame);
            }
            catch
            {
                writer.Return(frame);
                throw;
            }
            writer.Enqueue(frame);
            state.FrameCount++;
        }

        private static void CaptureFrame(CameraRecordingState state, PassthroughCameraAccess access,
            long timestampUs, MrukFrameWriter.PendingFrame frame)
        {
            var isUpdatedThisFrame = access.IsUpdatedThisFrame;
            var timestampMs = timestampUs / 1000L;
            var currentResolution = access.CurrentResolution;
            var width = currentResolution.x;
            var height = currentResolution.y;
            var colorsArrayLength = 0;
            var expectedPixelCount = 0;
            var rgbaByteCount = 0;
            var getTextureOk = false;
            var getColorsOk = false;
            var fileName = string.Empty;
            var error = string.Empty;

            try
            {
                var texture = access.GetTexture();
                if (texture != null)
                {
                    width = texture.width;
                    height = texture.height;
                    getTextureOk = true;
                }
                else
                {
                    error = AppendError(error, "GetTexture returned null");
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, $"GetTexture failed: {ex.Message}");
            }

            try
            {
                var colors = access.GetColors();
                if (colors.IsCreated)
                {
                    getColorsOk = true;
                    colorsArrayLength = colors.Length;
                    expectedPixelCount = Math.Max(0, width) * Math.Max(0, height);
                    rgbaByteCount = expectedPixelCount * 4;
                    if (expectedPixelCount > 0 && colors.Length >= expectedPixelCount)
                    {
                        if (frame.Pixels.Length != rgbaByteCount)
                        {
                            frame.Pixels = new byte[rgbaByteCount];
                        }

                        // The SDK owns the source buffer. Copy before handing off to the writer.
                        var bytes = colors.GetSubArray(0, expectedPixelCount).Reinterpret<byte>(4);
                        bytes.CopyTo(frame.Pixels);
                        fileName = $"{timestampUs}.rgba";
                    }
                    else
                    {
                        error = AppendError(error, $"GetColors length {colors.Length} is smaller than expected pixel count {expectedPixelCount}");
                    }
                }
                else
                {
                    error = AppendError(error, "GetColors returned an uncreated NativeArray");
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, $"GetColors failed: {ex.Message}");
            }

            var pose = default(UnityEngine.Pose);
            var poseOk = false;
            try
            {
                pose = access.GetCameraPose();
                poseOk = true;
            }
            catch (Exception ex)
            {
                error = AppendError(error, $"GetCameraPose failed: {ex.Message}");
            }

            frame.Index = state.FrameCount + 1;
            frame.TimestampUs = timestampUs;
            frame.Metadata = new[]
            {
                frame.Index.ToString(CultureInfo.InvariantCulture),
                fileName,
                timestampMs.ToString(CultureInfo.InvariantCulture),
                timestampUs.ToString(CultureInfo.InvariantCulture),
                width.ToString(CultureInfo.InvariantCulture),
                height.ToString(CultureInfo.InvariantCulture),
                "RGBA32",
                state.CameraName,
                FloatText(pose.position.x),
                FloatText(pose.position.y),
                FloatText(pose.position.z),
                FloatText(pose.rotation.x),
                FloatText(pose.rotation.y),
                FloatText(pose.rotation.z),
                FloatText(pose.rotation.w),
                BoolText(poseOk),
                BoolText(access.IsPlaying),
                BoolText(isUpdatedThisFrame),
                currentResolution.x.ToString(CultureInfo.InvariantCulture),
                currentResolution.y.ToString(CultureInfo.InvariantCulture),
                expectedPixelCount.ToString(CultureInfo.InvariantCulture),
                colorsArrayLength.ToString(CultureInfo.InvariantCulture),
                rgbaByteCount.ToString(CultureInfo.InvariantCulture),
                BoolText(getTextureOk),
                BoolText(getColorsOk),
                error
            };
        }

        private bool ResolveCameraAccesses()
        {
            if (autoDiscoverCameraAccess)
            {
                leftCameraAccess ??= FindCameraAccess(PassthroughCameraAccess.CameraPositionType.Left);
                rightCameraAccess ??= FindCameraAccess(PassthroughCameraAccess.CameraPositionType.Right);
            }

            leftState.Access = leftCameraAccess;
            rightState.Access = rightCameraAccess;

            if (leftState.Enabled && leftState.Access == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] MRUK left camera is enabled, but no PassthroughCameraAccess component was assigned or found.");
                return false;
            }

            if (rightState.Enabled && rightState.Access == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] MRUK right camera is enabled, but no PassthroughCameraAccess component was assigned or found.");
                return false;
            }

            return true;
        }

        private static PassthroughCameraAccess? FindCameraAccess(PassthroughCameraAccess.CameraPositionType position)
        {
            foreach (var access in FindObjectsByType<PassthroughCameraAccess>(FindObjectsInactive.Include))
            {
                if (access != null && access.CameraPosition == position)
                {
                    return access;
                }
            }

            return null;
        }


        private void EnableConfiguredCameraAccesses()
        {
            EnableCameraAccess(leftState);
            EnableCameraAccess(rightState);
        }

        private static void EnableCameraAccess(CameraRecordingState state)
        {
            if (!state.Enabled || state.Access == null || state.Access.enabled)
            {
                return;
            }

            state.Access.enabled = true;
        }

        private void StopConfiguredCameraAccesses()
        {
            StopCameraAccess(leftState);
            StopCameraAccess(rightState);
        }

        private static void StopCameraAccess(CameraRecordingState state)
        {
            var access = state.Access;
            if (!state.Enabled || access == null || !access.enabled)
            {
                return;
            }

            access.enabled = false;
        }

        private void WriteIntrinsics(CameraRecordingState state, string filePath)
        {
            if (!state.Enabled || state.Access == null)
            {
                return;
            }

            var info = MrukIntrinsicsMetadata.FromAccess(state.CameraName, state.Access);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? string.Empty);
            File.WriteAllText(filePath, JsonUtility.ToJson(info, prettyPrint: true));
        }

        private static long ToUnixMicroseconds(DateTime timestamp)
        {
            return (timestamp.Ticks - DateTime.UnixEpoch.Ticks) / 10L;
        }

        private static string FloatText(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string BoolText(bool value)
        {
            return value ? "true" : "false";
        }

        private static string AppendError(string current, string next)
        {
            return string.IsNullOrEmpty(current) ? next : current + "; " + next;
        }

        private sealed class CameraRecordingState
        {
            public CameraRecordingState(PassthroughCameraAccess.CameraPositionType position, string cameraName)
            {
                Position = position;
                CameraName = cameraName;
            }

            public PassthroughCameraAccess.CameraPositionType Position { get; }
            public string CameraName { get; }
            public bool Enabled { get; set; }
            public PassthroughCameraAccess? Access { get; set; }
            public int FrameCount { get; set; }
            public int DroppedFrames { get; set; }
            public long LastTimestampUs { get; set; }
            private float nextNotPlayingWarningRealtime;

            public void ResetRuntimeState()
            {
                FrameCount = 0;
                DroppedFrames = 0;
                LastTimestampUs = 0L;
                nextNotPlayingWarningRealtime = 0f;
            }

            public void LogNotPlayingWarning()
            {
                var now = Time.realtimeSinceStartup;
                if (now < nextNotPlayingWarningRealtime)
                {
                    return;
                }

                nextNotPlayingWarningRealtime = now + 1f;
                Debug.LogWarning($"[{Constants.LOG_TAG}] MRUK {CameraName} camera is enabled but not playing yet.");
            }
        }

        [Serializable]
        private sealed class MrukIntrinsicsMetadata
        {
            public string backend = "MRUK";
            public string cameraPosition = string.Empty;
            public string imageFormat = "RGBA32";
            public Vector2Data focalLength = new();
            public Vector2Data principalPoint = new();
            public Vector2IntData resolution = new();
            public Vector2IntData sensorResolution = new();
            public Vector2IntData currentResolution = new();
            public PoseData lensOffset = new();
            public string error = string.Empty;

            public static MrukIntrinsicsMetadata FromAccess(string cameraPosition, PassthroughCameraAccess access)
            {
                var result = new MrukIntrinsicsMetadata
                {
                    cameraPosition = cameraPosition,
                    currentResolution = Vector2IntData.FromVector(access.CurrentResolution),
                    resolution = Vector2IntData.FromVector(access.CurrentResolution)
                };

                try
                {
                    var intrinsics = access.Intrinsics;
                    result.focalLength = Vector2Data.FromVector(intrinsics.FocalLength);
                    result.principalPoint = Vector2Data.FromVector(intrinsics.PrincipalPoint);
                    result.sensorResolution = Vector2IntData.FromVector(intrinsics.SensorResolution);
                    result.lensOffset = PoseData.FromPose(intrinsics.LensOffset);
                }
                catch (Exception ex)
                {
                    result.error = ex.Message;
                }

                return result;
            }
        }

        [Serializable]
        private sealed class Vector2Data
        {
            public float x;
            public float y;

            public static Vector2Data FromVector(Vector2 vector)
            {
                return new Vector2Data { x = vector.x, y = vector.y };
            }
        }

        [Serializable]
        private sealed class Vector2IntData
        {
            public int width;
            public int height;

            public static Vector2IntData FromVector(Vector2Int vector)
            {
                return new Vector2IntData { width = vector.x, height = vector.y };
            }
        }

        [Serializable]
        private sealed class PoseData
        {
            public Vector3Data position = new();
            public QuaternionData rotation = new();

            public static PoseData FromPose(UnityEngine.Pose pose)
            {
                return new PoseData
                {
                    position = new Vector3Data { x = pose.position.x, y = pose.position.y, z = pose.position.z },
                    rotation = new QuaternionData { x = pose.rotation.x, y = pose.rotation.y, z = pose.rotation.z, w = pose.rotation.w }
                };
            }
        }

        [Serializable]
        private sealed class Vector3Data
        {
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        private sealed class QuaternionData
        {
            public float x;
            public float y;
            public float z;
            public float w = 1f;
        }
    }
}
