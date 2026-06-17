#nullable enable

namespace RealityLog.Recording
{
    public interface IRecordingSessionController
    {
        bool StartRecording();
        bool StopRecording();
        void SetRecordingEnabled(bool enabled);
    }
}
