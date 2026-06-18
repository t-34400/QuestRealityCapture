#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Meta.XR;
using RealityLog.Camera;
using Unity.Collections;
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
        private StreamWriter? leftFrameWriter;
        private StreamWriter? rightFrameWriter;
        private StreamWriter? pairWriter;
        private readonly CameraRecordingState leftState = new(PassthroughCameraAccess.CameraPositionType.Left, "Left");
        private readonly CameraRecordingState rightState = new(PassthroughCameraAccess.CameraPositionType.Right, "Right");
        private readonly List<RecordedFrame> leftRecordedFrames = new();
        private readonly List<RecordedFrame> rightRecordedFrames = new();
        private float nextSampleRealtime;
        private bool recording;
        private int pairIndex;

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

            EnableConfiguredCameraAccesses();

            Directory.CreateDirectory(paths.RootDirectoryPath);
            leftFrameWriter = leftState.Enabled
                ? CreateCsv(paths.LeftMrukCamera.FrameMetadataFilePath, FrameMetadataHeader)
                : null;
            rightFrameWriter = rightState.Enabled
                ? CreateCsv(paths.RightMrukCamera.FrameMetadataFilePath, FrameMetadataHeader)
                : null;
            pairWriter = leftState.Enabled && rightState.Enabled
                ? CreateCsv(paths.MrukStereoPairFilePath, PairMetadataHeader)
                : null;

            WriteIntrinsics(leftState, paths.LeftMrukCamera.IntrinsicsFilePath);
            WriteIntrinsics(rightState, paths.RightMrukCamera.IntrinsicsFilePath);

            leftState.ResetRuntimeState();
            rightState.ResetRuntimeState();
            pairIndex = 0;
            leftRecordedFrames.Clear();
            rightRecordedFrames.Clear();
            nextSampleRealtime = 0f;
            recording = true;
            Debug.Log($"[{Constants.LOG_TAG}] MRUK camera recording started: {paths.RootDirectoryPath}");
            return true;
        }

        public bool StopRecording()
        {
            if (!recording)
            {
                return true;
            }

            WriteNearestPairRows();
            recording = false;
            leftFrameWriter?.Dispose();
            leftFrameWriter = null;
            rightFrameWriter?.Dispose();
            rightFrameWriter = null;
            pairWriter?.Dispose();
            pairWriter = null;

            if (stopCameraAccessOnStop)
            {
                StopConfiguredCameraAccesses();
            }

            Debug.Log($"[{Constants.LOG_TAG}] MRUK camera recording stopped. LeftFrames={leftState.FrameCount}, RightFrames={rightState.FrameCount}");
            return true;
        }

        private void Update()
        {
            if (!recording || config == null || paths == null)
            {
                return;
            }

            if (!ShouldSampleNow(config.camera.targetSaveFps))
            {
                return;
            }

            var leftFrame = SampleCamera(leftState, paths.LeftMrukCamera, leftFrameWriter);
            var rightFrame = SampleCamera(rightState, paths.RightMrukCamera, rightFrameWriter);
            if (leftFrame.HasValue)
            {
                leftRecordedFrames.Add(leftFrame.Value);
            }
            if (rightFrame.HasValue)
            {
                rightRecordedFrames.Add(rightFrame.Value);
            }

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

        private RecordedFrame? SampleCamera(
            CameraRecordingState state,
            RecordingSessionPaths.MrukCameraPaths cameraPaths,
            StreamWriter? writer)
        {
            if (!state.Enabled || state.Access == null || writer == null)
            {
                return null;
            }

            if (maxFramesPerCamera > 0 && state.FrameCount >= maxFramesPerCamera)
            {
                return null;
            }

            var access = state.Access;
            var isUpdatedThisFrame = access.IsUpdatedThisFrame;
            if (!access.IsPlaying)
            {
                state.LogNotPlayingWarning();
                return null;
            }

            var timestampUs = ToUnixMicroseconds(access.Timestamp);
            if (timestampUs <= 0 || timestampUs == state.LastTimestampUs)
            {
                return null;
            }
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

            NativeArray<Color32> colors = default;
            try
            {
                colors = access.GetColors();
                if (colors.IsCreated)
                {
                    getColorsOk = true;
                    colorsArrayLength = colors.Length;
                    expectedPixelCount = Math.Max(0, width) * Math.Max(0, height);
                    rgbaByteCount = expectedPixelCount * 4;
                    if (expectedPixelCount > 0 && colors.Length >= expectedPixelCount)
                    {
                        fileName = $"{timestampUs}.rgba";
                        var outputPath = Path.Combine(cameraPaths.ImageDirectoryPath, fileName);
                        Directory.CreateDirectory(cameraPaths.ImageDirectoryPath);
                        WriteRgbaFile(outputPath, colors, expectedPixelCount);
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

            state.FrameCount++;
            state.LastTimestampUs = timestampUs;
            state.LastFrameIndex = state.FrameCount;
            state.LastFileName = fileName;
            writer.WriteLine(string.Join(",",
                state.FrameCount.ToString(CultureInfo.InvariantCulture),
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
                EscapeCsv(error)));
            writer.Flush();
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            return new RecordedFrame(state.LastFrameIndex, state.LastFileName, state.LastTimestampUs);
        }

        private void WriteNearestPairRows()
        {
            if (pairWriter == null || pairIndex > 0)
            {
                return;
            }

            var unpairedLeftFrames = new List<RecordedFrame>(leftRecordedFrames);
            var unpairedRightFrames = new List<RecordedFrame>(rightRecordedFrames);
            while (unpairedLeftFrames.Count > 0 && unpairedRightFrames.Count > 0)
            {
                FindNearestPair(unpairedLeftFrames, unpairedRightFrames, out var leftIndex, out var rightIndex);
                var leftFrame = unpairedLeftFrames[leftIndex];
                var rightFrame = unpairedRightFrames[rightIndex];
                WritePairRow(leftFrame, rightFrame);
                unpairedLeftFrames.RemoveAt(leftIndex);
                unpairedRightFrames.RemoveAt(rightIndex);
            }
        }

        private static void FindNearestPair(
            IReadOnlyList<RecordedFrame> leftFrames,
            IReadOnlyList<RecordedFrame> rightFrames,
            out int leftIndex,
            out int rightIndex)
        {
            leftIndex = 0;
            rightIndex = 0;
            var bestAbsDeltaUs = long.MaxValue;

            for (var leftCandidate = 0; leftCandidate < leftFrames.Count; ++leftCandidate)
            {
                var leftTimestampUs = leftFrames[leftCandidate].TimestampUs;
                for (var rightCandidate = 0; rightCandidate < rightFrames.Count; ++rightCandidate)
                {
                    var deltaUs = rightFrames[rightCandidate].TimestampUs - leftTimestampUs;
                    var absDeltaUs = deltaUs == long.MinValue ? long.MaxValue : Math.Abs(deltaUs);
                    if (absDeltaUs >= bestAbsDeltaUs)
                    {
                        continue;
                    }

                    bestAbsDeltaUs = absDeltaUs;
                    leftIndex = leftCandidate;
                    rightIndex = rightCandidate;
                }
            }
        }

        private void WritePairRow(RecordedFrame leftFrame, RecordedFrame rightFrame)
        {
            if (pairWriter == null)
            {
                return;
            }

            pairIndex++;
            var deltaUs = rightFrame.TimestampUs - leftFrame.TimestampUs;
            pairWriter.WriteLine(string.Join(",",
                pairIndex.ToString(CultureInfo.InvariantCulture),
                leftFrame.FileName,
                rightFrame.FileName,
                leftFrame.TimestampUs.ToString(CultureInfo.InvariantCulture),
                rightFrame.TimestampUs.ToString(CultureInfo.InvariantCulture),
                deltaUs.ToString(CultureInfo.InvariantCulture),
                leftFrame.FrameIndex.ToString(CultureInfo.InvariantCulture),
                rightFrame.FrameIndex.ToString(CultureInfo.InvariantCulture)));
            pairWriter.Flush();
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

        private static StreamWriter CreateCsv(string path, string header)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            var writer = new StreamWriter(path, append: false);
            writer.WriteLine(header);
            writer.Flush();
            return writer;
        }

        private static void WriteRgbaFile(string outputPath, NativeArray<Color32> colors, int pixelCount)
        {
            var bytes = new byte[pixelCount * 4];
            for (var i = 0; i < pixelCount; ++i)
            {
                var color = colors[i];
                var offset = i * 4;
                bytes[offset] = color.r;
                bytes[offset + 1] = color.g;
                bytes[offset + 2] = color.b;
                bytes[offset + 3] = color.a;
            }

            File.WriteAllBytes(outputPath, bytes);
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

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private const string FrameMetadataHeader =
            "frame_index,file_name,unix_time_ms,timestamp_us_realtime,width,height,format,camera_position,pose_pos_x,pose_pos_y,pose_pos_z,pose_rot_x,pose_rot_y,pose_rot_z,pose_rot_w,pose_ok,is_playing,is_updated_this_frame,current_resolution_width,current_resolution_height,pixel_count,colors_array_length,rgba_byte_count,get_texture_ok,get_colors_ok,error";

        private const string PairMetadataHeader =
            "pair_index,left_file_name,right_file_name,left_timestamp_us_realtime,right_timestamp_us_realtime,time_delta_us,left_frame_index,right_frame_index";


        private readonly struct RecordedFrame
        {
            public RecordedFrame(int frameIndex, string fileName, long timestampUs)
            {
                FrameIndex = frameIndex;
                FileName = fileName;
                TimestampUs = timestampUs;
            }

            public int FrameIndex { get; }
            public string FileName { get; }
            public long TimestampUs { get; }
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
            public int LastFrameIndex { get; set; }
            public long LastTimestampUs { get; set; }
            public string LastFileName { get; set; } = string.Empty;
            private float nextNotPlayingWarningRealtime;

            public void ResetRuntimeState()
            {
                FrameCount = 0;
                LastFrameIndex = 0;
                LastTimestampUs = 0L;
                LastFileName = string.Empty;
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
