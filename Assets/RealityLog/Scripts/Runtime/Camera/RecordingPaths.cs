#nullable enable

namespace RealityLog.Camera
{
    public sealed class RecordingPaths
    {
        public RecordingPaths(
            string dataDirectoryPath,
            string imageDirectoryPath,
            string cameraMetadataFilePath,
            string formatInfoFilePath)
        {
            DataDirectoryPath = dataDirectoryPath;
            ImageDirectoryPath = imageDirectoryPath;
            CameraMetadataFilePath = cameraMetadataFilePath;
            FormatInfoFilePath = formatInfoFilePath;
        }

        public string DataDirectoryPath { get; }
        public string ImageDirectoryPath { get; }
        public string CameraMetadataFilePath { get; }
        public string FormatInfoFilePath { get; }
    }
}
