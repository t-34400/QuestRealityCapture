#nullable enable

using System;
using System.Globalization;
using System.IO;
using Meta.XR;
using RealityLog.Camera;
using Unity.Collections;
using UnityEngine;

namespace RealityLog.Recording
{
    public sealed class MrukCameraProbeRecorder : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess? leftCameraAccess = null;
        [SerializeField] private PassthroughCameraAccess? rightCameraAccess = null;
        [SerializeField] private bool autoDiscoverCameraAccess = true;
        [SerializeField, Min(0)] private int maxFramesPerCamera = 120;
        [SerializeField, Min(0)] private int sampleRgbaFilesPerCamera = 1;

        private RecordingSessionConfig? config;
        private RecordingSessionPaths? paths;
        private StreamWriter? probeWriter;
        private StreamWriter? pairWriter;
        private readonly CameraProbeState leftState = new(PassthroughCameraAccess.CameraPositionType.Left, "Left");
        private readonly CameraProbeState rightState = new(PassthroughCameraAccess.CameraPositionType.Right, "Right");
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
                Debug.LogError($"[{Constants.LOG_TAG}] MRUK probe recorder has not been configured.");
                return false;
            }

            if (!ResolveCameraAccesses())
            {
                return false;
            }

            Directory.CreateDirectory(paths.RootDirectoryPath);
            probeWriter = CreateCsv(Path.Combine(paths.RootDirectoryPath, "mruk_probe.csv"),
                "frame_index,camera,is_playing,is_updated_this_frame,timestamp_unix_ms,timestamp_us_realtime,width,height,current_resolution_width,current_resolution_height,color_count,pose_ok,pose_pos_x,pose_pos_y,pose_pos_z,pose_rot_x,pose_rot_y,pose_rot_z,pose_rot_w,get_texture_ok,get_colors_ok,rgba_file_name,error");
            pairWriter = CreateCsv(Path.Combine(paths.RootDirectoryPath, "mruk_probe_pairs.csv"),
                "pair_index,left_frame_index,right_frame_index,left_timestamp_us_realtime,right_timestamp_us_realtime,time_delta_us");

            WriteIntrinsicsProbe(leftState, paths.LeftMrukCamera.IntrinsicsFilePath);
            WriteIntrinsicsProbe(rightState, paths.RightMrukCamera.IntrinsicsFilePath);

            leftState.ResetRuntimeState();
            rightState.ResetRuntimeState();
            pairIndex = 0;
            nextSampleRealtime = 0f;
            recording = true;
            Debug.Log($"[{Constants.LOG_TAG}] MRUK camera probe recording started: {paths.RootDirectoryPath}");
            return true;
        }

        public bool StopRecording()
        {
            if (!recording)
            {
                return true;
            }

            recording = false;
            probeWriter?.Dispose();
            probeWriter = null;
            pairWriter?.Dispose();
            pairWriter = null;
            Debug.Log($"[{Constants.LOG_TAG}] MRUK camera probe recording stopped. LeftFrames={leftState.FrameCount}, RightFrames={rightState.FrameCount}");
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

            var leftUpdated = SampleCamera(leftState, paths.LeftMrukCamera, probeWriter);
            var rightUpdated = SampleCamera(rightState, paths.RightMrukCamera, probeWriter);
            if (leftUpdated && rightUpdated)
            {
                WritePairRow();
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

        private bool SampleCamera(CameraProbeState state, RecordingSessionPaths.MrukCameraPaths cameraPaths, StreamWriter? writer)
        {
            if (!state.Enabled || state.Access == null)
            {
                return false;
            }

            if (maxFramesPerCamera > 0 && state.FrameCount >= maxFramesPerCamera)
            {
                return false;
            }

            var access = state.Access;
            var isUpdatedThisFrame = access.IsUpdatedThisFrame;
            if (!isUpdatedThisFrame)
            {
                return false;
            }

            state.FrameCount++;
            var timestampUs = ToUnixMicroseconds(access.Timestamp);
            var timestampMs = timestampUs / 1000L;
            var currentResolution = access.CurrentResolution;
            var width = 0;
            var height = 0;
            var colorCount = 0;
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
                    colorCount = colors.Length;
                    if (state.SavedRgbaFiles < sampleRgbaFilesPerCamera)
                    {
                        fileName = $"mruk_probe_{state.FilePrefix}_{timestampUs}.rgba";
                        var outputPath = Path.Combine(cameraPaths.ImageDirectoryPath, fileName);
                        Directory.CreateDirectory(cameraPaths.ImageDirectoryPath);
                        WriteRgbaFile(outputPath, colors);
                        state.SavedRgbaFiles++;
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

            state.LastTimestampUs = timestampUs;
            state.LastFrameIndex = state.FrameCount;
            writer?.WriteLine(string.Join(",",
                state.FrameCount.ToString(CultureInfo.InvariantCulture),
                state.CameraName,
                BoolText(access.IsPlaying),
                BoolText(isUpdatedThisFrame),
                timestampMs.ToString(CultureInfo.InvariantCulture),
                timestampUs.ToString(CultureInfo.InvariantCulture),
                width.ToString(CultureInfo.InvariantCulture),
                height.ToString(CultureInfo.InvariantCulture),
                currentResolution.x.ToString(CultureInfo.InvariantCulture),
                currentResolution.y.ToString(CultureInfo.InvariantCulture),
                colorCount.ToString(CultureInfo.InvariantCulture),
                BoolText(poseOk),
                FloatText(pose.position.x),
                FloatText(pose.position.y),
                FloatText(pose.position.z),
                FloatText(pose.rotation.x),
                FloatText(pose.rotation.y),
                FloatText(pose.rotation.z),
                FloatText(pose.rotation.w),
                BoolText(getTextureOk),
                BoolText(getColorsOk),
                fileName,
                EscapeCsv(error)));
            writer?.Flush();
            return true;
        }

        private void WritePairRow()
        {
            if (pairWriter == null || leftState.LastFrameIndex <= 0 || rightState.LastFrameIndex <= 0)
            {
                return;
            }

            pairIndex++;
            var deltaUs = rightState.LastTimestampUs - leftState.LastTimestampUs;
            pairWriter.WriteLine(string.Join(",",
                pairIndex.ToString(CultureInfo.InvariantCulture),
                leftState.LastFrameIndex.ToString(CultureInfo.InvariantCulture),
                rightState.LastFrameIndex.ToString(CultureInfo.InvariantCulture),
                leftState.LastTimestampUs.ToString(CultureInfo.InvariantCulture),
                rightState.LastTimestampUs.ToString(CultureInfo.InvariantCulture),
                deltaUs.ToString(CultureInfo.InvariantCulture)));
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
                Debug.LogError($"[{Constants.LOG_TAG}] MRUK probe left camera is enabled, but no PassthroughCameraAccess component was assigned or found.");
                return false;
            }

            if (rightState.Enabled && rightState.Access == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] MRUK probe right camera is enabled, but no PassthroughCameraAccess component was assigned or found.");
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

        private void WriteIntrinsicsProbe(CameraProbeState state, string filePath)
        {
            if (!state.Enabled || state.Access == null)
            {
                return;
            }

            var info = MrukIntrinsicsProbe.FromAccess(state.CameraName, state.Access);
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

        private static void WriteRgbaFile(string outputPath, NativeArray<Color32> colors)
        {
            var bytes = new byte[colors.Length * 4];
            for (var i = 0; i < colors.Length; ++i)
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

        private sealed class CameraProbeState
        {
            public CameraProbeState(PassthroughCameraAccess.CameraPositionType position, string cameraName)
            {
                Position = position;
                CameraName = cameraName;
                FilePrefix = cameraName.ToLowerInvariant();
            }

            public PassthroughCameraAccess.CameraPositionType Position { get; }
            public string CameraName { get; }
            public string FilePrefix { get; }
            public bool Enabled { get; set; }
            public PassthroughCameraAccess? Access { get; set; }
            public int FrameCount { get; set; }
            public int SavedRgbaFiles { get; set; }
            public int LastFrameIndex { get; set; }
            public long LastTimestampUs { get; set; }

            public void ResetRuntimeState()
            {
                FrameCount = 0;
                SavedRgbaFiles = 0;
                LastFrameIndex = 0;
                LastTimestampUs = 0L;
            }
        }

        [Serializable]
        private sealed class MrukIntrinsicsProbe
        {
            public string backend = "MRUK";
            public string probe = nameof(PassthroughCameraAccess);
            public string cameraPosition = string.Empty;
            public string imageFormat = "RGBA32";
            public Vector2Data focalLength = new();
            public Vector2Data principalPoint = new();
            public Vector2IntData sensorResolution = new();
            public Vector2IntData currentResolution = new();
            public PoseData lensOffset = new();
            public string error = string.Empty;

            public static MrukIntrinsicsProbe FromAccess(string cameraPosition, PassthroughCameraAccess access)
            {
                var result = new MrukIntrinsicsProbe
                {
                    cameraPosition = cameraPosition,
                    currentResolution = Vector2IntData.FromVector(access.CurrentResolution)
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
