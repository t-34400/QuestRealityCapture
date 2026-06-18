#nullable enable

namespace RealityLog.Recording
{
    public sealed class RecordingSessionPaths
    {
        public RecordingSessionPaths(
            string sessionName,
            string rootDirectoryPath,
            string sessionInfoFilePath,
            CameraPaths leftCamera,
            CameraPaths rightCamera,
            MrukCameraPaths leftMrukCamera,
            MrukCameraPaths rightMrukCamera,
            string mrukStereoPairFilePath,
            DepthPaths depth,
            PosePaths pose)
        {
            SessionName = sessionName;
            RootDirectoryPath = rootDirectoryPath;
            SessionInfoFilePath = sessionInfoFilePath;
            LeftCamera = leftCamera;
            RightCamera = rightCamera;
            LeftMrukCamera = leftMrukCamera;
            RightMrukCamera = rightMrukCamera;
            MrukStereoPairFilePath = mrukStereoPairFilePath;
            Depth = depth;
            Pose = pose;
        }

        public string SessionName { get; }
        public string RootDirectoryPath { get; }
        public string SessionInfoFilePath { get; }
        public CameraPaths LeftCamera { get; }
        public CameraPaths RightCamera { get; }
        public MrukCameraPaths LeftMrukCamera { get; }
        public MrukCameraPaths RightMrukCamera { get; }
        public string MrukStereoPairFilePath { get; }
        public DepthPaths Depth { get; }
        public PosePaths Pose { get; }
        public string PoseCsvFilePath => Pose.HmdFilePath;

        public CameraPaths GetCameraPaths(RealityLog.Camera.CameraPosition position)
        {
            return position == RealityLog.Camera.CameraPosition.Right ? RightCamera : LeftCamera;
        }

        public MrukCameraPaths GetMrukCameraPaths(RealityLog.Camera.CameraPosition position)
        {
            return position == RealityLog.Camera.CameraPosition.Right ? RightMrukCamera : LeftMrukCamera;
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

        public sealed class MrukCameraPaths
        {
            public MrukCameraPaths(string imageDirectoryPath, string intrinsicsFilePath, string frameMetadataFilePath)
            {
                ImageDirectoryPath = imageDirectoryPath;
                IntrinsicsFilePath = intrinsicsFilePath;
                FrameMetadataFilePath = frameMetadataFilePath;
            }

            public string ImageDirectoryPath { get; }
            public string IntrinsicsFilePath { get; }
            public string FrameMetadataFilePath { get; }
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
