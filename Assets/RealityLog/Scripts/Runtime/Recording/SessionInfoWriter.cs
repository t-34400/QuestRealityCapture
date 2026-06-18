#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace RealityLog.Recording
{
    public static class SessionInfoWriter
    {
        private const int SessionFormatVersion = 2;

        [Serializable]
        private sealed class SessionInfo
        {
            public int sessionFormatVersion = SessionFormatVersion;
            public string captureBackend = RecordingSessionConfig.CameraBackend.Mruk;
        }

        public static bool TryWrite(string filePath, string captureBackend)
        {
            try
            {
                var info = new SessionInfo { captureBackend = captureBackend };
                File.WriteAllText(filePath, JsonUtility.ToJson(info, prettyPrint: true));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Failed to write session_info.json: {ex.Message}");
                return false;
            }
        }
    }
}
