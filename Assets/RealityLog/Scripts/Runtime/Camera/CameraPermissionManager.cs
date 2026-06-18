#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

namespace RealityLog.Camera
{
    public class CameraPermissionManager : MonoBehaviour
    {
        private const float CAMERA_MANAGER_CHECK_INTERVAL = 0.1f;

        public const string AndroidCameraPermission = "android.permission.CAMERA";
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        private const string CAMEAR_PERMISSION_MANAGER_CLASS_NAME = "com.t34400.questcamera.core.CameraPermissionManager";

        private const string HAS_CAMERA_MANAGER_METHOD_NAME = "hasCameraManager";
        private const string GET_CAMERA_MANAGER_METHOD_NAME = "getCameraManager";

        [SerializeField] private bool requestAndroidCameraPermission = true;
        [SerializeField] private bool requestHeadsetCameraPermission = true;
        [SerializeField] private bool initializeLegacyJavaCameraManager = true;

        private readonly Queue<string> pendingPermissionRequests = new();
#if UNITY_ANDROID && !UNITY_EDITOR
        private PermissionCallbacks? permissionCallbacks;
        private bool permissionRequestInProgress;
        private bool legacyCameraManagerInitializationStarted;
#endif

        public bool HasRequiredCameraPermission => HasConfiguredCameraPermissions();

        public bool HasCameraManager => JavaInstance?.Call<bool>(HAS_CAMERA_MANAGER_METHOD_NAME) ?? false;

        public AndroidJavaObject? JavaInstance { get; private set; }
        public AndroidJavaObject? CameraManagerJavaInstance { get; private set; }

        public event Action? CameraPermissionGranted;
#pragma warning disable CS0067
        public event Action<string>? CameraPermissionDenied;
        public event Action<AndroidJavaObject>? CameraManagerInstantiated;
#pragma warning restore CS0067

        private void Start()
        {
            RequestCameraPermissionIfNeeded();
        }

        private void OnDestroy()
        {
            DestroyInstance();
        }

        public void RequestCameraPermissionIfNeeded()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (permissionRequestInProgress)
            {
                return;
            }

            pendingPermissionRequests.Clear();

            foreach (var permission in EnumerateConfiguredPermissions())
            {
                if (!Permission.HasUserAuthorizedPermission(permission))
                {
                    pendingPermissionRequests.Enqueue(permission);
                }
            }

            if (pendingPermissionRequests.Count == 0)
            {
                OnAllConfiguredPermissionsGranted();
                return;
            }

            permissionRequestInProgress = true;
            RequestNextPermission();
#else
            OnAllConfiguredPermissionsGranted();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void RequestNextPermission()
        {
            if (pendingPermissionRequests.Count == 0)
            {
                permissionRequestInProgress = false;

                if (HasConfiguredCameraPermissions())
                {
                    OnAllConfiguredPermissionsGranted();
                }

                return;
            }

            var permission = pendingPermissionRequests.Dequeue();
            permissionCallbacks = new PermissionCallbacks();
            permissionCallbacks.PermissionGranted += OnUnityPermissionGranted;
            permissionCallbacks.PermissionDenied += OnUnityPermissionDenied;
            permissionCallbacks.PermissionDeniedAndDontAskAgain += OnUnityPermissionDenied;
            Permission.RequestUserPermission(permission, permissionCallbacks);
        }

        private void OnUnityPermissionGranted(string permissionName)
        {
            Debug.Log($"[{Constants.LOG_TAG}] Camera permission granted: {permissionName}");
            RequestNextPermission();
        }

        private void OnUnityPermissionDenied(string permissionName)
        {
            Debug.LogError($"[{Constants.LOG_TAG}] Camera permission denied: {permissionName}");
            pendingPermissionRequests.Clear();
            permissionRequestInProgress = false;
            CameraPermissionDenied?.Invoke(permissionName);
        }
#endif

        private void OnAllConfiguredPermissionsGranted()
        {
            CameraPermissionGranted?.Invoke();

            if (initializeLegacyJavaCameraManager)
            {
                InstantiateLegacyCameraManager();
            }
        }

        private void InstantiateLegacyCameraManager()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (legacyCameraManagerInitializationStarted)
            {
                return;
            }

            legacyCameraManagerInitializationStarted = true;
            DestroyLegacyCameraManager();

            using (AndroidJavaClass unityPlayerClazz = new AndroidJavaClass(Constants.UNITY_PLAYER_CLASS_NAME))
            using (AndroidJavaObject currentActivity = unityPlayerClazz.GetStatic<AndroidJavaObject>(Constants.UNITY_PLAYER_CURRENT_ACTIVITY_VARIABLE_NAME))
            {
                JavaInstance = new AndroidJavaObject(CAMEAR_PERMISSION_MANAGER_CLASS_NAME, currentActivity);
                StartCoroutine(CheckCameraManagerCoroutine());
            }
#endif
        }

        private void DestroyInstance()
        {
            pendingPermissionRequests.Clear();
#if UNITY_ANDROID && !UNITY_EDITOR
            permissionRequestInProgress = false;
            permissionCallbacks = null;
            legacyCameraManagerInitializationStarted = false;
#endif
            DestroyLegacyCameraManager();
            StopAllCoroutines();
        }

        private void DestroyLegacyCameraManager()
        {
            JavaInstance?.Dispose();
            JavaInstance = null;

            CameraManagerJavaInstance?.Dispose();
            CameraManagerJavaInstance = null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator CheckCameraManagerCoroutine()
        {
            while (true)
            {
                if (HasCameraManager)
                {
                    CameraManagerJavaInstance = JavaInstance?.Call<AndroidJavaObject>(GET_CAMERA_MANAGER_METHOD_NAME);

                    if (CameraManagerJavaInstance != null)
                    {
                        CameraManagerInstantiated?.Invoke(CameraManagerJavaInstance);
                    }

                    yield break;
                }

                yield return new WaitForSeconds(CAMERA_MANAGER_CHECK_INTERVAL);
            }
        }
#endif

        private bool HasConfiguredCameraPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            foreach (var permission in EnumerateConfiguredPermissions())
            {
                if (!Permission.HasUserAuthorizedPermission(permission))
                {
                    return false;
                }
            }
#endif
            return true;
        }

        private IEnumerable<string> EnumerateConfiguredPermissions()
        {
            if (requestHeadsetCameraPermission)
            {
                yield return HeadsetCameraPermission;
            }

            if (requestAndroidCameraPermission)
            {
                yield return AndroidCameraPermission;
            }
        }
    }
}
