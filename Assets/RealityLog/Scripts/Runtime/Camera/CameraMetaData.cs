# nullable enable

using System;

namespace RealityLog.Camera
{
    [Serializable]
    public class CameraMetadata
    {
        public string cameraId = string.Empty;
        public int cameraSource;
        public int cameraPositionId;
        public string lensFacing = string.Empty;
        public string hardwareLevel = string.Empty;
        public Pose pose = default!;
        public Intrinsics intrinsics = default!;
        public float[] distortion = new float[0];
        public Sensor sensor = default!;

        public bool IsPassthroughCamera => cameraSource == 0;

        public CameraPosition CameraPosition => cameraPositionId switch
        {
            0 => CameraPosition.Left,
            1 => CameraPosition.Right,
            _ => CameraPosition.Unknown
        };

        public override string ToString()
        {
            string FormatArray<T>(T[] array) => array.Length == 0 ? "[]" : "[" + string.Join(", ", array) + "]";

            return $"CameraMetadata:\n" +
                $"- cameraId: {cameraId}\n" +
                $"- cameraSource: {cameraSource} (IsPassthroughCamera: {IsPassthroughCamera})\n" +
                $"- cameraPositionId: {cameraPositionId} (CameraPosition: {CameraPosition})\n" +
                $"- lensFacing: {lensFacing}\n" +
                $"- hardwareLevel: {hardwareLevel}\n" +
                $"- pose:\n" +
                $"    - translation: {FormatArray(pose.translation)}\n" +
                $"    - rotation: {FormatArray(pose.rotation)}\n" +
                $"    - reference: {pose.reference}\n" +
                $"- intrinsics:\n" +
                $"    - fx: {intrinsics.fx}, fy: {intrinsics.fy}, cx: {intrinsics.cx}, cy: {intrinsics.cy}, skew: {intrinsics.skew}\n" +
                $"- distortion: {FormatArray(distortion)}\n" +
                $"- sensor:\n" +
                $"    - availableFocalLengths: {FormatArray(sensor.availableFocalLengths)}\n" +
                $"    - physicalSize: ({sensor.physicalSize.width}, {sensor.physicalSize.height})\n" +
                $"    - pixelArraySize: ({sensor.pixelArraySize.width}, {sensor.pixelArraySize.height})\n" +
                $"    - preCorrectionActiveArraySize: (left: {sensor.preCorrectionActiveArraySize.left}, top: {sensor.preCorrectionActiveArraySize.top}, right: {sensor.preCorrectionActiveArraySize.right}, bottom: {sensor.preCorrectionActiveArraySize.bottom})\n" +
                $"    - activeArraySize: (left: {sensor.activeArraySize.left}, top: {sensor.activeArraySize.top}, right: {sensor.activeArraySize.right}, bottom: {sensor.activeArraySize.bottom})\n" +
                $"    - timestampSource: {sensor.timestampSource}";
        }
    }

    [Serializable]
    public class Pose
    {
        public float[] translation = new float[0];
        public float[] rotation = new float[0];
        public string reference = string.Empty;
    }

    [Serializable]
    public class Intrinsics
    {
        public float fx;
        public float fy;
        public float cx;
        public float cy;
        public float skew;
    }

    [Serializable]
    public class IntSize
    {
        public int width;
        public int height;
    }

    [Serializable]
    public class FloatSize
    {
        public float width;
        public float height;
    }

    [Serializable]
    public class IntRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [Serializable]
    public class Sensor
    {
        public float[] availableFocalLengths = new float[0];
        public FloatSize physicalSize = default!;
        public IntSize pixelArraySize = default!;
        public IntRect preCorrectionActiveArraySize = default!;
        public IntRect activeArraySize = default!;
        public string timestampSource = string.Empty;
    }

    public enum CameraPosition
    {
        Left,
        Right,
        Unknown
    }
}