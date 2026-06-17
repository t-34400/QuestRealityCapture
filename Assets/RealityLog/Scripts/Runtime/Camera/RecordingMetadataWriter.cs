#nullable enable

using System.IO;
using UnityEngine;

namespace RealityLog.Camera
{
    public sealed class RecordingMetadataWriter
    {
        public void WriteCameraMetadata(string filePath, CameraMetadata metadata)
        {
            var metadataJson = JsonUtility.ToJson(metadata);
            File.WriteAllText(filePath, metadataJson);
        }
    }
}
