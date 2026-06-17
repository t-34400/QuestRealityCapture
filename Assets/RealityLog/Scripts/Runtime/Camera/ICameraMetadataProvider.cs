#nullable enable

namespace RealityLog.Camera
{
    public interface ICameraMetadataProvider
    {
        CameraMetadata? GetMetadata(CameraPosition position);
    }
}
