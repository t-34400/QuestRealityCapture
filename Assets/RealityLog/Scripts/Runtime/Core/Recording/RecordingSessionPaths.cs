#nullable enable

namespace RealityLog.Recording
{
    public sealed class RecordingSessionPaths
    {
        public RecordingSessionPaths(
            string sessionName,
            string rootDirectoryPath,
            CameraPaths leftCamera,
            CameraPaths rightCamera,
            DepthPaths depth,
            PosePaths pose)
        {
            SessionName = sessionName;
            RootDirectoryPath = rootDirectoryPath;
            LeftCamera = leftCamera;
            RightCamera = rightCamera;
            Depth = depth;
            Pose = pose;
        }

        public string SessionName { get; }
        public string RootDirectoryPath { get; }
        public CameraPaths LeftCamera { get; }
        public CameraPaths RightCamera { get; }
        public DepthPaths Depth { get; }
        public PosePaths Pose { get; }
        public string PoseCsvFilePath => Pose.HmdFilePath;

        public CameraPaths GetCameraPaths(RealityLog.Camera.CameraPosition position)
        {
            return position == RealityLog.Camera.CameraPosition.Right ? RightCamera : LeftCamera;
        }

        public sealed class CameraPaths
        {
            public CameraPaths(string imageDirectoryPath, string metadataFilePath, string formatInfoFilePath)
            {
                ImageDirectoryPath = imageDirectoryPath;
                MetadataFilePath = metadataFilePath;
                FormatInfoFilePath = formatInfoFilePath;
            }

            public string ImageDirectoryPath { get; }
            public string MetadataFilePath { get; }
            public string FormatInfoFilePath { get; }
        }

        public sealed class PosePaths
        {
            public PosePaths(string hmdFilePath, string leftControllerFilePath, string rightControllerFilePath)
            {
                HmdFilePath = hmdFilePath;
                LeftControllerFilePath = leftControllerFilePath;
                RightControllerFilePath = rightControllerFilePath;
            }

            public string HmdFilePath { get; }
            public string LeftControllerFilePath { get; }
            public string RightControllerFilePath { get; }
        }

        public sealed class DepthPaths
        {
            public DepthPaths(
                string leftDirectoryPath,
                string rightDirectoryPath,
                string leftDescriptorFilePath,
                string rightDescriptorFilePath)
            {
                LeftDirectoryPath = leftDirectoryPath;
                RightDirectoryPath = rightDirectoryPath;
                LeftDescriptorFilePath = leftDescriptorFilePath;
                RightDescriptorFilePath = rightDescriptorFilePath;
            }

            public string LeftDirectoryPath { get; }
            public string RightDirectoryPath { get; }
            public string LeftDescriptorFilePath { get; }
            public string RightDescriptorFilePath { get; }
        }
    }
}
