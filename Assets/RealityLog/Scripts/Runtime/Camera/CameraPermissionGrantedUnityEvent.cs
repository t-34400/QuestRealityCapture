#nullable enable

using UnityEngine;
using UnityEngine.Events;

namespace RealityLog.Camera
{
    class CameraPermissionGrantedUnityEvent : MonoBehaviour
    {
        [SerializeField] private CameraPermissionManager cameraPermissionManager = default!;
        [SerializeField] private UnityEvent permissionGranted = default!;

        private void Start()
        {
            if (cameraPermissionManager.HasRequiredCameraPermission)
            {
                permissionGranted.Invoke();
                return;
            }

            cameraPermissionManager.CameraPermissionGranted += OnCameraPermissionGranted;
        }

        private void OnDestroy()
        {
            cameraPermissionManager.CameraPermissionGranted -= OnCameraPermissionGranted;
        }

        private void OnCameraPermissionGranted()
        {
            permissionGranted.Invoke();
        }
    }
}
