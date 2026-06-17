#nullable enable

namespace RealityLog.Camera
{
    public interface ICameraRecorder
    {
        bool Initialize();
        bool OpenCamera();
        bool StartRecording();
        bool StopRecording();
        bool Close();
        bool RefreshStats();
    }
}
