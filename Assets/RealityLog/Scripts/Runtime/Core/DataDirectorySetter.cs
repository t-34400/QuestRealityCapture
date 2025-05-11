# nullable enable

using System;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace RealityLog
{
    public class DataDirectorySetter : MonoBehaviour
    {
        [SerializeField] private bool createOnAwake = false;
        [SerializeField] private UnityEvent<string> dataDirectoryCreated = default!;

        public string DataDirectoryPath { get; private set; } = string.Empty;

        public void CreateDirectory()
        {
            var now = DateTime.Now;
            var subDirName = now.ToString("yyyyMMdd_HHmmss");

            DataDirectoryPath = Path.Join(Application.persistentDataPath, subDirName);
            Directory.CreateDirectory(DataDirectoryPath);

            dataDirectoryCreated?.Invoke(subDirName);
        }

        private void Awake()
        {
            if (createOnAwake)
            {
                CreateDirectory();
            }
        }
    }
}