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

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeStereoCameraStats
    {
        public long leftReceivedFrameCount;
        public long rightReceivedFrameCount;
        public long savedPairCount;
        public long droppedFrameCount;
        public long ioErrorCount;
        public long lastLeftImageTimestampNs;
        public long lastRightImageTimestampNs;
        public long lastSavedLeftTimestampNs;
        public long lastSavedRightTimestampNs;
        public long lastSavedDeltaNs;
    }

    public static class NativeCameraBridge
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const string LibraryName = "qrc_camera_native";

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_CreateSession(out IntPtr handle);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_DestroySession(IntPtr handle);

        [DllImport(LibraryName, CharSet = CharSet.Ansi)]
        private static extern NativeCameraResult QrcCamera_InitializeSession(
            IntPtr handle,
            int width,
            int height,
            string frameDirectory,
            string formatInfoFilePath);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_SetSessionSaveFrameRate(IntPtr handle, int fps);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_OpenSession(IntPtr handle, NativeCameraPosition position);

        [DllImport(LibraryName, CharSet = CharSet.Ansi)]
        private static extern NativeCameraResult QrcCamera_OpenSessionById(IntPtr handle, string cameraId);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_StartSessionRecording(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_StopSessionRecording(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_CloseSession(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcCamera_GetSessionStats(IntPtr handle, out NativeCameraStats stats);

        [DllImport(LibraryName)]
        private static extern IntPtr QrcCamera_GetSessionLastError(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern IntPtr QrcCamera_GetSessionLastOpenedCameraId(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern IntPtr QrcCamera_GetCameraIdListJson();

        [DllImport(LibraryName)]
        private static extern IntPtr QrcCamera_GetCameraMetadataJson(NativeCameraPosition position);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcStereoCamera_CreateSession(out IntPtr handle);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcStereoCamera_DestroySession(IntPtr handle);

        [DllImport(LibraryName, CharSet = CharSet.Ansi)]
        private static extern NativeCameraResult QrcStereoCamera_InitializeSession(
            IntPtr handle,
            int width,
            int height,
            string leftFrameDirectory,
            string rightFrameDirectory,
            string leftFormatInfoFilePath,
            string rightFormatInfoFilePath,
            string pairCsvFilePath,
            long maxTimeDeltaNs);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcStereoCamera_SetSessionSaveFrameRate(IntPtr handle, int fps);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcStereoCamera_OpenSession(IntPtr handle);

        [DllImport(LibraryName, CharSet = CharSet.Ansi)]
        private static extern NativeCameraResult QrcStereoCamera_OpenSessionByIds(IntPtr handle, string leftCameraId, string rightCameraId);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcStereoCamera_StartSessionRecording(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcStereoCamera_StopSessionRecording(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcStereoCamera_CloseSession(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern NativeCameraResult QrcStereoCamera_GetSessionStats(IntPtr handle, out NativeStereoCameraStats stats);

        [DllImport(LibraryName)]
        private static extern IntPtr QrcStereoCamera_GetSessionLastError(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern IntPtr QrcStereoCamera_GetSessionLastOpenedLeftCameraId(IntPtr handle);

        [DllImport(LibraryName)]
        private static extern IntPtr QrcStereoCamera_GetSessionLastOpenedRightCameraId(IntPtr handle);
#endif

        public static NativeCameraResult CreateSession(out IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_CreateSession(out handle);
#else
            handle = IntPtr.Zero;
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult DestroySession(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_DestroySession(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult InitializeSession(
            IntPtr handle,
            int width,
            int height,
            string frameDirectory,
            string formatInfoFilePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_InitializeSession(handle, width, height, frameDirectory, formatInfoFilePath);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult SetSessionSaveFrameRate(IntPtr handle, int fps)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_SetSessionSaveFrameRate(handle, fps);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult OpenSession(IntPtr handle, NativeCameraPosition position)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_OpenSession(handle, position);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult OpenSessionById(IntPtr handle, string cameraId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_OpenSessionById(handle, cameraId);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult StartSessionRecording(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_StartSessionRecording(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult StopSessionRecording(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_StopSessionRecording(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult CloseSession(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_CloseSession(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult GetSessionStats(IntPtr handle, out NativeCameraStats stats)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcCamera_GetSessionStats(handle, out stats);
#else
            stats = default;
            return NativeCameraResult.NotSupported;
#endif
        }

        public static string GetSessionLastError(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcCamera_GetSessionLastError(handle));
#else
            return "Native camera plugin is available only on Android devices.";
#endif
        }

        public static string GetSessionLastOpenedCameraId(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcCamera_GetSessionLastOpenedCameraId(handle));
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

        public static NativeCameraResult CreateStereoSession(out IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_CreateSession(out handle);
#else
            handle = IntPtr.Zero;
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult DestroyStereoSession(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_DestroySession(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult InitializeStereoSession(
            IntPtr handle,
            int width,
            int height,
            string leftFrameDirectory,
            string rightFrameDirectory,
            string leftFormatInfoFilePath,
            string rightFormatInfoFilePath,
            string pairCsvFilePath,
            long maxTimeDeltaNs)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_InitializeSession(
                handle,
                width,
                height,
                leftFrameDirectory,
                rightFrameDirectory,
                leftFormatInfoFilePath,
                rightFormatInfoFilePath,
                pairCsvFilePath,
                maxTimeDeltaNs);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult SetStereoSessionSaveFrameRate(IntPtr handle, int fps)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_SetSessionSaveFrameRate(handle, fps);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult OpenStereoSession(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_OpenSession(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult OpenStereoSessionByIds(IntPtr handle, string leftCameraId, string rightCameraId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_OpenSessionByIds(handle, leftCameraId, rightCameraId);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult StartStereoSessionRecording(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_StartSessionRecording(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult StopStereoSessionRecording(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_StopSessionRecording(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult CloseStereoSession(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_CloseSession(handle);
#else
            return NativeCameraResult.NotSupported;
#endif
        }

        public static NativeCameraResult GetStereoSessionStats(IntPtr handle, out NativeStereoCameraStats stats)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return QrcStereoCamera_GetSessionStats(handle, out stats);
#else
            stats = default;
            return NativeCameraResult.NotSupported;
#endif
        }

        public static string GetStereoSessionLastError(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcStereoCamera_GetSessionLastError(handle));
#else
            return "Native stereo camera plugin is available only on Android devices.";
#endif
        }

        public static string GetStereoSessionLastOpenedLeftCameraId(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcStereoCamera_GetSessionLastOpenedLeftCameraId(handle));
#else
            return string.Empty;
#endif
        }

        public static string GetStereoSessionLastOpenedRightCameraId(IntPtr handle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PtrToString(QrcStereoCamera_GetSessionLastOpenedRightCameraId(handle));
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
