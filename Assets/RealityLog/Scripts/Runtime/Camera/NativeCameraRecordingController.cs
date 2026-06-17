#nullable enable

using RealityLog.Recording;
using UnityEngine;

namespace RealityLog.Camera
{
    public class NativeCameraRecordingController : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour? sessionController = null;
        [SerializeField] private NativeCameraRecorder recorder = default!;
        [SerializeField] private bool openOnStart = false;
        [SerializeField] private bool startRecordingOnStart = false;
        [SerializeField] private bool closeOnDestroy = true;

        private IRecordingSessionController? SessionController => sessionController as IRecordingSessionController;

        public bool Initialize()
        {
            if (SessionController != null)
            {
                return true;
            }

            if (recorder == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native camera recorder is not assigned.");
                return false;
            }

            return recorder.Initialize();
        }

        public bool OpenCamera()
        {
            if (SessionController != null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] OpenCamera was ignored because recording is owned by RecordingSessionController.");
                return true;
            }

            return Initialize() && recorder.OpenCamera();
        }

        public bool StartRecording()
        {
            var session = SessionController;
            if (session != null)
            {
                return session.StartRecording();
            }

            return Initialize() && recorder.StartRecording();
        }

        public bool StopRecording()
        {
            var session = SessionController;
            if (session != null)
            {
                return session.StopRecording();
            }

            return recorder == null || recorder.StopRecording();
        }

        public bool Close()
        {
            var session = SessionController;
            if (session != null)
            {
                return session.StopRecording();
            }

            return recorder == null || recorder.Close();
        }

        public void SetDataDirectoryName(string value)
        {
            if (SessionController != null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] SetDataDirectoryName was ignored because recording paths are owned by RecordingSessionController.");
                return;
            }

            if (recorder == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native camera recorder is not assigned.");
                return;
            }

            recorder.SetDataDirectoryName(value);
        }

        private void Start()
        {
            if (startRecordingOnStart)
            {
                StartRecording();
                return;
            }

            if (openOnStart)
            {
                OpenCamera();
            }
        }

        private void OnDestroy()
        {
            if (closeOnDestroy)
            {
                Close();
            }
        }
    }
}
