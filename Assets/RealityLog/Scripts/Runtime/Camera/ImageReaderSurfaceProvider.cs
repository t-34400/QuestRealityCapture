# nullable enable

using System;
using System.IO;
using UnityEngine;

namespace RealityLog.Camera
{
    public class ImageReaderSurfaceProvider : SurfaceProviderBase
    {
        private const string IMAGE_READER_SURFACE_PROVIDER_CLASS_NAME = "com.t34400.questcamera.io.ImageReaderSurfaceProvider";
        
        private const string SET_SHOULD_SAVE_FRAME_METHOD_NAME = "setShouldSaveFrame";
        private const string CLOSE_METHOD_NAME = "close";

        [SerializeField] private string dataDirectoryName = string.Empty;
        [SerializeField] private string imageSubdirName = "left_camera";
        [SerializeField] private string cameraMetaDataFileName = "left_camera_characteristics.json";
        [SerializeField] private string formatInfoFileName = "left_camera_image_format.json";
        [SerializeField] private int bufferPoolSize = 5;

        private AndroidJavaObject? currentInstance;

        public string DataDirectoryName
        {
            get => dataDirectoryName;
            set => dataDirectoryName = value;
        }

        public override AndroidJavaObject? GetJavaInstance(CameraMetadata metadata)
        {
            Close();

            var dataDirPath = Path.Join(Application.persistentDataPath, dataDirectoryName);

            var metaDataFilePath = Path.Join(dataDirPath, cameraMetaDataFileName);
            var metaDataJson = JsonUtility.ToJson(metadata);
            try
            {
                File.WriteAllText(metaDataFilePath, metaDataJson);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            var imageFileDirPath = Path.Join(dataDirPath, imageSubdirName);
            var formatInfoFilePath = Path.Join(dataDirPath, formatInfoFileName);

            var size = metadata.sensor.pixelArraySize;

            currentInstance = new AndroidJavaObject(
                IMAGE_READER_SURFACE_PROVIDER_CLASS_NAME,
                size.width,
                size.height,
                imageFileDirPath,
                formatInfoFilePath,
                bufferPoolSize
            );
            currentInstance?.Call(SET_SHOULD_SAVE_FRAME_METHOD_NAME, true);

            return currentInstance;
        }

        private void OnDestroy()
        {
            Close();
        }

        private void Close()
        {
            currentInstance?.Call(CLOSE_METHOD_NAME);
            currentInstance?.Dispose();
            currentInstance = null;
        }
    }
}