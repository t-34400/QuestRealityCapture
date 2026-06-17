#nullable enable

using System;
using System.Runtime.InteropServices;

namespace RealityLog.Camera
{
    public enum NativeCameraResult
    {
        Ok = 0,
        InvalidState = -1,
        InvalidArgument = -2,
        CameraNotFound = -3,
        CameraOpenFailed = -4,
        SessionFailed = -5,
        Io = -6,
        PermissionDenied = -7,
        NotSupported = -8
    }

    public enum NativeCameraPosition
    {
        Left = 0,
        Right = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeCameraStats
    {
        public long receivedFrameCount;
        public long savedFrameCount;
        public long droppedFrameCount;
        public long ioErrorCount;
        public long lastImageTimestampNs;
        public long lastSavedTimestampNs;
    }

    public static class NativeCameraBridge
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const string LibraryName = "qrc_camera_native";

        [DllImport(LibraryName, CharSet = CharSet.Ansi)]
        private static extern NativeCameraResult QrcCamera_Initialize(
            int width,
            int height,
            string frameDirectory,
            string formatInfoFilePath);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_SetSaveFrameRate(int fps);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_Open(NativeCameraPosition position);

        [DllImport(LibraryName, CharSet = CharSet.Ansi)]
        private static extern NativeCameraResult QrcCamera_OpenById(string cameraId);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_StartRecording();

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_StopRecording();

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_Close();

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_GetStats(out NativeCameraStats stats);

        [DllImport(LibraryName)]
        private static extern IntPtr QrcCamera_GetLastError();

        [DllImport(LibraryName)]
        private static extern IntPtr QrcCamera_GetLastOpenedCameraId();

        [DllImport(LibraryName)]
        private static extern IntPtr QrcCamera_GetCameraIdListJson();

        [DllImport(LibraryName)]
        private static extern IntPtr QrcCamera_GetCameraMetadataJson(NativeCameraPosition position);
#endif

        public static NativeCameraResult Initialize(
            int width,
            int height,
            string frameDirectory,
            string formatInfoFilePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_Initialize(width, height, frameDirectory, formatInfoFilePath);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult SetSaveFrameRate(int fps)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_SetSaveFrameRate(fps);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult Open(NativeCameraPosition position)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_Open(position);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult OpenById(string cameraId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_OpenById(cameraId);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult StartRecording()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_StartRecording();
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult StopRecording()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_StopRecording();
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult Close()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_Close();
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult GetStats(out NativeCameraStats stats)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_GetStats(out stats);
#else
            stats = default;
            return NativeCameraResult.NotSupported;
#endif
        }

        public static string GetLastError()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcCamera_GetLastError());
#else
            return "Native camera plugin is available only on Android devices.";
#endif
        }

        public static string GetLastOpenedCameraId()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcCamera_GetLastOpenedCameraId());
#else
            return string.Empty;
#endif
        }

        public static string GetCameraIdListJson()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcCamera_GetCameraIdListJson());
#else
            return "[]";
#endif
        }

        public static string GetCameraMetadataJson(NativeCameraPosition position)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcCamera_GetCameraMetadataJson(position));
#else
            return string.Empty;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static string PtrToString(IntPtr ptr)
        {
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }
#endif
    }
}
