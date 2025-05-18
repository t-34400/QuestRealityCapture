# nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace RealityLog.Camera
{
    public class CameraSessionManager : MonoBehaviour
    {
        private const string CAMEAR_SESSION_MANAGER_CLASS_NAME = "com.t34400.questcamera.core.CameraSessionManager";

        private const string REGISTER_SURFACE_PROVIDER_METHOD_NAME = "registerSurfaceProvider";
        private const string OPEN_CAMERA_METHOD_NAME = "openCamera";
        private const string CLOSE_METHOD_NAME = "close";

        [SerializeField] private CameraPermissionManager cameraPermissionManager = default!;
        [SerializeField] private List<SurfaceProviderBase> surfaceProviders = new();
        [SerializeField] private CameraPosition cameraPosition = CameraPosition.Left;

        public AndroidJavaObject? SessionManagerJavaInstance { get; private set; }

# if UNITY_ANDROID
        private void OnEnable()
        {
            var cameraManagerJavaInstance = cameraPermissionManager.CameraManagerJavaInstance;

            if (cameraManagerJavaInstance == null)
            {
                Debug.Log($"[{Constants.LOG_TAG}] CameraManager not instantiated. Waiting for initialization...");
                cameraPermissionManager.CameraManagerInstantiated += OnCameraManagerInstantiated;
            }
            else
            {
                Instantiate(cameraManagerJavaInstance);
            }
        }

        private void OnDisable()
        {
            DestroyInstance();
            cameraPermissionManager.CameraManagerInstantiated -= OnCameraManagerInstantiated;
        }

        private void OnCameraManagerInstantiated(AndroidJavaObject cameraManagerJavaInstance)
        {
            Debug.Log($"[{Constants.LOG_TAG}] OnCameraManagerInstantiated");
            Instantiate(cameraManagerJavaInstance);
        }

        private void Instantiate(AndroidJavaObject cameraManagerJavaInstance)
        {
            if (SessionManagerJavaInstance != null)
                return;

            if (surfaceProviders.Count == 0)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] No Surface Provider registered.");
                return;
            }

            var metaData = cameraPosition switch
                {
                    CameraPosition.Left => cameraPermissionManager.LeftCameraMetaData,
                    CameraPosition.Right => cameraPermissionManager.RightCameraMetaData,
                    _ => null
                };

            if (metaData == null)
            {
                return;
            }

            Debug.Log($"[{Constants.LOG_TAG}] {metaData}");

            SessionManagerJavaInstance = new AndroidJavaObject(CAMEAR_SESSION_MANAGER_CLASS_NAME);

            foreach (ISurfaceProvider provider in surfaceProviders)
            {
                var providerJavaInstance = provider.GetJavaInstance(metaData);

                if (providerJavaInstance == null)
                {
                    Debug.LogWarning($"[{Constants.LOG_TAG}] Failed to create Surface Provider AndroidJavaObject.");
                    continue;
                }

                SessionManagerJavaInstance.Call(REGISTER_SURFACE_PROVIDER_METHOD_NAME, providerJavaInstance);
            }

            using (AndroidJavaClass unityPlayerClazz = new AndroidJavaClass(Constants.UNITY_PLAYER_CLASS_NAME))
            using (AndroidJavaObject currentActivity = unityPlayerClazz.GetStatic<AndroidJavaObject>(Constants.UNITY_PLAYER_CURRENT_ACTIVITY_VARIABLE_NAME))
            {
                SessionManagerJavaInstance.Call(
                    OPEN_CAMERA_METHOD_NAME,
                    currentActivity,
                    cameraManagerJavaInstance,
                    metaData.cameraId
                );
            }

            Debug.Log($"[{Constants.LOG_TAG}] Camera Session ID={metaData.cameraId} started.");            
        }

        private void DestroyInstance()
        {
            SessionManagerJavaInstance?.Call(CLOSE_METHOD_NAME);
            SessionManagerJavaInstance?.Dispose();
            SessionManagerJavaInstance = null;
        }
# endif

        enum CameraPosition
        {
            Left,
            Right
        }
    }
}