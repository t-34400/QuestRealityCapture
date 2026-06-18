#nullable enable

using System;
using RealityLog.Recording;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealityLog.Depth
{
    public sealed class LiveDepthCoverageVisualizer : MonoBehaviour
    {
        private const int KernelThreadGroupSize = 8;
        private const int ClearKernelThreadGroupSize = 64;
        private const int InvalidKernel = -1;
        private const int FrameDescriptorCount = 2;

        [SerializeField] private DepthFrameProvider? depthFrameProvider = null;
        [SerializeField] private ComputeShader? updateCoverageShader = null;
        [SerializeField] private Shader? coveragePointShader = null;
        [SerializeField] private Material? coveragePointMaterial = null;
        [SerializeField, Min(0.001f)] private float pointSizeMeters = 0.03f;

        private DepthCoverageSettings settings = new(
            false,
            3,
            24,
            0.15f,
            30000,
            0.3f,
            5.0f,
            DepthCoverageEye.Left);

        private ComputeBuffer? voxelKeysBuffer;
        private ComputeBuffer? voxelOccupancyBuffer;
        private ComputeBuffer? coveragePointsBuffer;
        private Material? runtimeCoverageMaterial;
        private int updateKernel = InvalidKernel;
        private int clearKernel = InvalidKernel;
        private bool isVisualizing;
        private bool hasBegunDepthUsage;
        private float nextUpdateRealtime;
        private int allocatedMaxVoxels;

        public bool IsVisualizing => isVisualizing;

        public void ApplyConfiguration(RecordingSessionConfig.LiveFeedbackConfig config)
        {
            settings = DepthCoverageSettings.FromConfig(config.coverage);
        }

        public bool TryStartVisualization()
        {
            StopVisualization();

            if (!settings.enabled)
            {
                return true;
            }

            if (!EnsureDependencies())
            {
                return false;
            }

            try
            {
                AllocateBuffers(settings.maxVoxels);
                ClearCoverageBuffers();
                depthFrameProvider!.BeginDepthUsage();
                hasBegunDepthUsage = true;
                nextUpdateRealtime = 0f;
                isVisualizing = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Failed to start live depth coverage visualization: {ex.Message}");
                StopVisualization();
                return false;
            }
        }

        public void StopVisualization()
        {
            isVisualizing = false;

            if (hasBegunDepthUsage)
            {
                depthFrameProvider?.EndDepthUsage();
                hasBegunDepthUsage = false;
            }

            ReleaseBuffers();
        }

        private void Awake()
        {
            ResolveDepthFrameProvider();
            ResolveMaterial();
            ResolveKernels();
        }

        private void OnDestroy()
        {
            StopVisualization();
            if (runtimeCoverageMaterial != null)
            {
                Destroy(runtimeCoverageMaterial);
                runtimeCoverageMaterial = null;
            }
        }

        private void Update()
        {
            if (!isVisualizing || Time.realtimeSinceStartup < nextUpdateRealtime)
            {
                return;
            }

            nextUpdateRealtime = Time.realtimeSinceStartup + settings.UpdateIntervalSeconds;
            DispatchCoverageUpdate();
        }

        private void OnRenderObject()
        {
            if (!isVisualizing || coveragePointsBuffer == null || voxelKeysBuffer == null || voxelOccupancyBuffer == null)
            {
                return;
            }

            var material = ResolveMaterial();
            if (material == null)
            {
                return;
            }

            material.SetBuffer("_CoveragePoints", coveragePointsBuffer);
            material.SetBuffer("_VoxelOccupancy", voxelOccupancyBuffer);
            material.SetFloat("_PointSizeMeters", pointSizeMeters);
            material.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, settings.maxVoxels * 6);
        }

        private void DispatchCoverageUpdate()
        {
            if (depthFrameProvider == null || updateCoverageShader == null || updateKernel == InvalidKernel
                || coveragePointsBuffer == null || voxelKeysBuffer == null || voxelOccupancyBuffer == null)
            {
                return;
            }

            if (!depthFrameProvider.TryGetLatestFrame(out var depthTexture, out var frameDescriptors))
            {
                return;
            }

            if (frameDescriptors.Length != FrameDescriptorCount)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Live coverage expected two depth descriptors but received {frameDescriptors.Length}.");
                return;
            }

            var eyeIndex = Mathf.Clamp((int)settings.eye, 0, FrameDescriptorCount - 1);
            var descriptor = frameDescriptors[eyeIndex];
            var cameraToWorld = Matrix4x4.TRS(descriptor.createPoseLocation, descriptor.createPoseRotation, Vector3.one);

            updateCoverageShader.SetTexture(updateKernel, "_DepthTexture", depthTexture);
            updateCoverageShader.SetBuffer(updateKernel, "_VoxelKeys", voxelKeysBuffer);
            updateCoverageShader.SetBuffer(updateKernel, "_VoxelOccupancy", voxelOccupancyBuffer);
            updateCoverageShader.SetBuffer(updateKernel, "_CoveragePoints", coveragePointsBuffer);
            updateCoverageShader.SetInt("_Width", depthTexture.width);
            updateCoverageShader.SetInt("_Height", depthTexture.height);
            updateCoverageShader.SetInt("_EyeIndex", eyeIndex);
            updateCoverageShader.SetInt("_SamplingStep", settings.samplingStep);
            updateCoverageShader.SetInt("_MaxVoxels", settings.maxVoxels);
            updateCoverageShader.SetFloat("_VoxelSizeMeters", settings.voxelSizeMeters);
            updateCoverageShader.SetFloat("_MinDepthMeters", settings.minDepthMeters);
            updateCoverageShader.SetFloat("_MaxDepthMeters", settings.maxDepthMeters);
            updateCoverageShader.SetFloat("_FovLeftTan", descriptor.fovLeftAngleTangent);
            updateCoverageShader.SetFloat("_FovRightTan", descriptor.fovRightAngleTangent);
            updateCoverageShader.SetFloat("_FovTopTan", descriptor.fovTopAngleTangent);
            updateCoverageShader.SetFloat("_FovDownTan", descriptor.fovDownAngleTangent);
            updateCoverageShader.SetMatrix("_DepthCameraToWorld", cameraToWorld);

            var groupsX = Mathf.CeilToInt(depthTexture.width / (float)(KernelThreadGroupSize * settings.samplingStep));
            var groupsY = Mathf.CeilToInt(depthTexture.height / (float)(KernelThreadGroupSize * settings.samplingStep));
            updateCoverageShader.Dispatch(updateKernel, Mathf.Max(1, groupsX), Mathf.Max(1, groupsY), 1);
        }

        private bool EnsureDependencies()
        {
            ResolveDepthFrameProvider();
            ResolveKernels();
            ResolveMaterial();

            if (depthFrameProvider == null)
            {
                depthFrameProvider = gameObject.AddComponent<DepthFrameProvider>();
            }

            if (updateCoverageShader == null || updateKernel == InvalidKernel || clearKernel == InvalidKernel)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Live coverage is enabled, but UpdateDepthCoverage.compute is not assigned or is missing required kernels.");
                return false;
            }

            if (coveragePointMaterial == null && runtimeCoverageMaterial == null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Live coverage is enabled, but no coverage point material or shader is assigned.");
                return false;
            }

            return true;
        }

        private void AllocateBuffers(int maxVoxels)
        {
            if (voxelKeysBuffer != null && voxelOccupancyBuffer != null && coveragePointsBuffer != null && allocatedMaxVoxels == maxVoxels)
            {
                return;
            }

            ReleaseBuffers();
            allocatedMaxVoxels = maxVoxels;
            voxelKeysBuffer = new ComputeBuffer(maxVoxels, sizeof(int) * 4, ComputeBufferType.Structured);
            voxelOccupancyBuffer = new ComputeBuffer(maxVoxels, sizeof(int), ComputeBufferType.Structured);
            coveragePointsBuffer = new ComputeBuffer(maxVoxels, sizeof(float) * 4, ComputeBufferType.Structured);
        }

        private void ClearCoverageBuffers()
        {
            if (updateCoverageShader == null || clearKernel == InvalidKernel || voxelKeysBuffer == null || voxelOccupancyBuffer == null || coveragePointsBuffer == null)
            {
                return;
            }

            updateCoverageShader.SetBuffer(clearKernel, "_VoxelKeys", voxelKeysBuffer);
            updateCoverageShader.SetBuffer(clearKernel, "_VoxelOccupancy", voxelOccupancyBuffer);
            updateCoverageShader.SetBuffer(clearKernel, "_CoveragePoints", coveragePointsBuffer);
            updateCoverageShader.SetInt("_MaxVoxels", settings.maxVoxels);
            var groups = Mathf.CeilToInt(settings.maxVoxels / (float)ClearKernelThreadGroupSize);
            updateCoverageShader.Dispatch(clearKernel, Mathf.Max(1, groups), 1, 1);
        }

        private void ReleaseBuffers()
        {
            voxelKeysBuffer?.Release();
            voxelKeysBuffer = null;
            voxelOccupancyBuffer?.Release();
            voxelOccupancyBuffer = null;
            coveragePointsBuffer?.Release();
            coveragePointsBuffer = null;
            allocatedMaxVoxels = 0;
        }

        private void ResolveDepthFrameProvider()
        {
            if (depthFrameProvider != null)
            {
                return;
            }

            depthFrameProvider = GetComponent<DepthFrameProvider>();
            if (depthFrameProvider == null)
            {
                depthFrameProvider = FindAnyObjectByType<DepthFrameProvider>();
            }
        }

        private Material? ResolveMaterial()
        {
            if (coveragePointMaterial != null)
            {
                return coveragePointMaterial;
            }

            if (runtimeCoverageMaterial != null)
            {
                return runtimeCoverageMaterial;
            }

            if (coveragePointShader == null)
            {
                coveragePointShader = Shader.Find("RealityLog/DepthCoveragePoints");
            }

            if (coveragePointShader != null)
            {
                runtimeCoverageMaterial = new Material(coveragePointShader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            return runtimeCoverageMaterial;
        }

        private void ResolveKernels()
        {
            updateKernel = InvalidKernel;
            clearKernel = InvalidKernel;

            if (updateCoverageShader == null)
            {
                return;
            }

            if (updateCoverageShader.HasKernel("UpdateCoverage"))
            {
                updateKernel = updateCoverageShader.FindKernel("UpdateCoverage");
            }

            if (updateCoverageShader.HasKernel("ClearCoverage"))
            {
                clearKernel = updateCoverageShader.FindKernel("ClearCoverage");
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            const string UPDATE_COVERAGE_SHADER_PATH = "Assets/RealityLog/ComputeShaders/UpdateDepthCoverage.compute";
            const string COVERAGE_POINT_SHADER_PATH = "Assets/RealityLog/Shaders/DepthCoveragePoints.shader";

            if (updateCoverageShader == null)
            {
                updateCoverageShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(UPDATE_COVERAGE_SHADER_PATH);
            }

            if (coveragePointShader == null)
            {
                coveragePointShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(COVERAGE_POINT_SHADER_PATH);
            }

            ResolveKernels();
        }
#endif
    }
}
