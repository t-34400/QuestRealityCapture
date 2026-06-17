#nullable enable

using UnityEngine;

namespace RealityLog.Camera
{
    public class NativeCameraRecordingController : MonoBehaviour
    {
        [SerializeField] private NativeCameraRecorder recorder = default!;
        [SerializeField] private bool openOnStart = false;
        [SerializeField] private bool startRecordingOnStart = false;
        [SerializeField] private bool closeOnDestroy = true;

        public bool Initialize()
        {
            if (recorder == null)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native camera recorder is not assigned.");
                return false;
            }

            return recorder.Initialize();
        }

        public bool OpenCamera()
        {
            return Initialize() && recorder.OpenCamera();
        }

        public bool StartRecording()
        {
            return Initialize() && recorder.StartRecording();
        }

        public bool StopRecording()
        {
            return recorder == null || recorder.StopRecording();
        }

        public bool Close()
        {
            return recorder == null || recorder.Close();
        }

        public void SetDataDirectoryName(string value)
        {
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
