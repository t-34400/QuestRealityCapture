#nullable enable

using UnityEngine;

namespace RealityLog.Camera
{
    public class JavaCameraMetadataProvider : MonoBehaviour, ICameraMetadataProvider
    {
        private const string GET_LEFT_CAMERA_META_DATA_METHOD_NAME = "getLeftCameraMetaDataJson";
        private const string GET_RIGHT_CAMERA_META_DATA_METHOD_NAME = "getRightCameraMetaDataJson";

        [SerializeField] private CameraPermissionManager cameraPermissionManager = default!;

        public void Configure(CameraPermissionManager manager)
        {
            cameraPermissionManager = manager;
        }

        public CameraMetadata? GetMetadata(CameraPosition position)
        {
            var methodName = position switch
            {
                CameraPosition.Left => GET_LEFT_CAMERA_META_DATA_METHOD_NAME,
                CameraPosition.Right => GET_RIGHT_CAMERA_META_DATA_METHOD_NAME,
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            var json = cameraPermissionManager?.JavaInstance?.Call<string>(methodName) ?? string.Empty;
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<CameraMetadata>(json);
        }
    }
}
