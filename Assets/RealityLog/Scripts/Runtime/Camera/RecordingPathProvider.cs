#nullable enable

using System.IO;
using UnityEngine;

namespace RealityLog.Camera
{
    public sealed class RecordingPathProvider
    {
        public RecordingPaths Create(
            string dataDirectoryName,
            string imageSubdirName,
            string cameraMetadataFileName,
            string formatInfoFileName)
        {
            var dataDirectoryPath = Path.Combine(Application.persistentDataPath, dataDirectoryName);
            var imageDirectoryPath = Path.Combine(dataDirectoryPath, imageSubdirName);
            var cameraMetadataFilePath = Path.Combine(dataDirectoryPath, cameraMetadataFileName);
            var formatInfoFilePath = Path.Combine(dataDirectoryPath, formatInfoFileName);

            Directory.CreateDirectory(dataDirectoryPath);
            Directory.CreateDirectory(imageDirectoryPath);

            return new RecordingPaths(
                dataDirectoryPath,
                imageDirectoryPath,
                cameraMetadataFilePath,
                formatInfoFilePath);
        }
    }
}
