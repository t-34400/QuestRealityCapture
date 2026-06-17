#nullable enable

using UnityEngine;

namespace RealityLog.Camera
{
    public class NativeCameraMetadataProvider : MonoBehaviour, ICameraMetadataProvider
    {
        public CameraMetadata? GetMetadata(CameraPosition position)
        {
            var nativePosition = position switch
            {
                CameraPosition.Right => NativeCameraPosition.Right,
                CameraPosition.Left => NativeCameraPosition.Left,
                _ => (NativeCameraPosition?)null
            };

            if (nativePosition == null)
            {
                return null;
            }

            var json = NativeCameraBridge.GetCameraMetadataJson(nativePosition.Value);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native camera metadata JSON is empty. {NativeCameraBridge.GetLastError()}");
                return null;
            }

            var metadata = JsonUtility.FromJson<CameraMetadata>(json);
            if (metadata == null || string.IsNullOrEmpty(metadata.cameraId))
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Native camera metadata JSON did not contain a cameraId: {json}");
                return null;
            }

            return metadata;
        }
    }
}
