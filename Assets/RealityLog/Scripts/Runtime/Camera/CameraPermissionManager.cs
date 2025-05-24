# nullable enable

using System;
using System.Collections;
using UnityEngine;

namespace RealityLog.Camera
{
    class CameraPermissionManager : MonoBehaviour
    {
        private const float CAMERA_MANAGER_CHECK_INTERVAL = 0.1f;

        private const string CAMEAR_PERMISSION_MANAGER_CLASS_NAME = "com.t34400.questcamera.core.CameraPermissionManager";

        private const string REQUEST_CAMERA_PERMISSION_METHOD_NAME = "requestCameraPermissionIfNeeded";
        private const string HAS_CAMERA_MANAGER_METHOD_NAME = "hasCameraManager";
        private const string GET_CAMERA_MANAGER_METHOD_NAME = "getCameraManager";         

        private const string GET_LEFT_CAMERA_META_DATA_METHOD_NAME = "getLeftCameraMetaDataJson";
        private const string GET_RIGHT_CAMERA_META_DATA_METHOD_NAME = "getRightCameraMetaDataJson";

        public bool HasCameraManager => JavaInstance?.Call<bool>(HAS_CAMERA_MANAGER_METHOD_NAME) ?? false;

        public CameraMetadata? LeftCameraMetaData
        {
            get
            {
                var json = JavaInstance?.Call<string>(GET_LEFT_CAMERA_META_DATA_METHOD_NAME) ?? string.Empty;

                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                return JsonUtility.FromJson<CameraMetadata>(json);
            }
        }
        public CameraMetadata? RightCameraMetaData
        {
            get
            {
                var json = JavaInstance?.Call<string>(GET_RIGHT_CAMERA_META_DATA_METHOD_NAME) ?? string.Empty;

                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                return JsonUtility.FromJson<CameraMetadata>(json);
            }
        }

        public AndroidJavaObject? JavaInstance { get; private set; }
        public AndroidJavaObject? CameraManagerJavaInstance { get; private set; }

        public event Action<AndroidJavaObject>? CameraManagerInstantiated;

# if UNITY_ANDROID
        private void Start()
        {
            Instantiate();
        }

        private void OnDestroy()
        {
            DestroyInstance();            
        }

        private void Instantiate()
        {
            DestroyInstance();

            using (AndroidJavaClass unityPlayerClazz = new AndroidJavaClass(Constants.UNITY_PLAYER_CLASS_NAME))
            using (AndroidJavaObject currentActivity = unityPlayerClazz.GetStatic<AndroidJavaObject>(Constants.UNITY_PLAYER_CURRENT_ACTIVITY_VARIABLE_NAME))
            {
                JavaInstance = new AndroidJavaObject(CAMEAR_PERMISSION_MANAGER_CLASS_NAME, currentActivity);
                JavaInstance.Call(REQUEST_CAMERA_PERMISSION_METHOD_NAME);

                StartCoroutine(CheckCameraManagerCoroutine());
            }
        }

        private void DestroyInstance()
        {
            JavaInstance?.Dispose();
            JavaInstance = null;

            CameraManagerJavaInstance?.Dispose();
            CameraManagerJavaInstance = null;

            StopAllCoroutines();
        }

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
# endif        
    }
}