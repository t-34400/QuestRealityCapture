#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealityLog.Depth
{
#if UNITY_EDITOR
    internal enum EditorCoverageSource
    {
        DepthProvider,
        DebugGrid,
        DebugAxes,
        DebugFrustum
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal readonly struct EditorVoxelKey
    {
        public EditorVoxelKey(int x, int y, int z, int segmentId)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.segmentId = segmentId;
        }

        public readonly int x;
        public readonly int y;
        public readonly int z;
        public readonly int segmentId;
    }
#endif

    internal enum DepthCoverageRenderMode
    {
        ParticleSystem = 0,
        ProceduralBillboard = 1
    }

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
        [SerializeField] private Transform? trackingSpace = null;
        [SerializeField] private DepthCoverageRenderMode renderMode = DepthCoverageRenderMode.ParticleSystem;
        [SerializeField, Min(0.001f)] private float pointSizeMeters = 0.015f;
        [SerializeField, Range(0.0f, 1.0f)] private float previousSegmentAlpha = 0.22f;
        [SerializeField, Min(0.05f)] private float particleRefreshIntervalSeconds = 0.25f;
        [SerializeField, Min(0.001f)] private float frustumLineWidthMeters = 0.006f;
        [SerializeField, Min(0.05f)] private float frustumDistanceMeters = 1.0f;
        [SerializeField, Range(0.0f, 1.0f)] private float frustumAlpha = 0.28f;
#if UNITY_EDITOR
        [Header("Editor Debug")]
        [SerializeField] private EditorCoverageSource editorCoverageSource = EditorCoverageSource.DepthProvider;
        [SerializeField] private bool startEditorDebugOnPlay = false;
        [SerializeField, Min(1)] private int editorDebugGridResolution = 11;
        [SerializeField, Min(0.1f)] private float editorDebugGridSizeMeters = 2.0f;
        [SerializeField, Min(0.1f)] private float editorDebugGridDistanceMeters = 2.0f;
        [SerializeField, Min(0.1f)] private float editorDebugAxisLengthMeters = 1.0f;
#endif

        private DepthCoverageSettings settings = new(
            false,
            3,
            24,
            0.15f,
            30000,
            0.3f,
            5.0f,
            DepthCoverageEye.Left,
            false,
            1.0f,
            24,
            false,
            1.0f,
            true);

        private ComputeBuffer? voxelKeysBuffer;
        private ComputeBuffer? voxelOccupancyBuffer;
        private ComputeBuffer? coveragePointsBuffer;
        private ComputeBuffer? coverageMetadataBuffer;
        private Material? runtimeCoverageMaterial;
        private DepthCoverageParticleRenderer? particleRenderer;
        private DepthFrustumHistoryRenderer? frustumHistoryRenderer;
        private int updateKernel = InvalidKernel;
        private int clearKernel = InvalidKernel;
        private bool isVisualizing;
        private bool hasBegunDepthUsage;
        private float nextUpdateRealtime;
        private float nextFrustumSampleRealtime;
        private float nextDepthPoseDiagnosticRealtime;
        private int allocatedMaxVoxels;
        private int currentSegmentId;

        public bool IsVisualizing => isVisualizing;
        public int CurrentSegmentId => currentSegmentId;
#if UNITY_EDITOR
        private bool UsesEditorDebugSource => Application.isEditor && editorCoverageSource != EditorCoverageSource.DepthProvider;
#else
        private bool UsesEditorDebugSource => false;
#endif

        public void ApplyConfiguration(DepthCoverageSettings coverageSettings)
        {
            settings = coverageSettings;
        }

        public bool TryStartVisualization()
        {
            StopVisualization();

            if (!settings.enabled && !UsesEditorDebugSource)
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
                PrepareRenderer();
                nextUpdateRealtime = 0f;
                nextFrustumSampleRealtime = 0f;
                nextDepthPoseDiagnosticRealtime = 0f;
                currentSegmentId = 0;
                PrepareFrustumRenderer();

#if UNITY_EDITOR
                if (UsesEditorDebugSource)
                {
                    FillEditorDebugCoverage();
                    isVisualizing = true;
                    return true;
                }
#endif

                depthFrameProvider!.BeginDepthUsage();
                hasBegunDepthUsage = true;
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

            ClearParticles();
            ClearFrustumHistory();
            ReleaseBuffers();
        }


        public void SetSegmentId(int segmentId)
        {
            currentSegmentId = Mathf.Max(0, segmentId);
        }

        private void Awake()
        {
            ResolveDepthFrameProvider();
            ResolveMaterial();
            ResolveKernels();
        }

#if UNITY_EDITOR
        private void Start()
        {
            if (startEditorDebugOnPlay && UsesEditorDebugSource)
            {
                _ = TryStartVisualization();
            }
        }
#endif

        private void OnDestroy()
        {
            StopVisualization();
            if (runtimeCoverageMaterial != null)
            {
                Destroy(runtimeCoverageMaterial);
                runtimeCoverageMaterial = null;
            }

            particleRenderer?.Dispose();
            particleRenderer = null;
            frustumHistoryRenderer?.Dispose();
            frustumHistoryRenderer = null;
        }

        private void Update()
        {
            if (!isVisualizing || Time.realtimeSinceStartup < nextUpdateRealtime)
            {
                return;
            }

            nextUpdateRealtime = Time.realtimeSinceStartup + settings.UpdateIntervalSeconds;
#if UNITY_EDITOR
            if (UsesEditorDebugSource)
            {
                return;
            }
#endif
            DispatchCoverageUpdate();
            RefreshParticleRendererIfNeeded();
        }

        private void OnRenderObject()
        {
            if (renderMode != DepthCoverageRenderMode.ProceduralBillboard
                || !isVisualizing
                || coveragePointsBuffer == null
                || coverageMetadataBuffer == null
                || voxelKeysBuffer == null
                || voxelOccupancyBuffer == null)
            {
                return;
            }

            var material = ResolveMaterial();
            if (material == null)
            {
                return;
            }

            material.SetBuffer("_CoveragePoints", coveragePointsBuffer);
            material.SetBuffer("_CoverageMetadata", coverageMetadataBuffer);
            material.SetBuffer("_VoxelOccupancy", voxelOccupancyBuffer);
            material.SetFloat("_PointSizeMeters", pointSizeMeters);
            material.SetFloat("_CurrentSegmentId", currentSegmentId);
            material.SetFloat("_PreviousSegmentAlpha", previousSegmentAlpha);
            material.SetFloat("_MinDepthMeters", settings.minDepthMeters);
            material.SetFloat("_MaxDepthMeters", settings.maxDepthMeters);
            material.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, settings.maxVoxels * 6);
        }

        private void DispatchCoverageUpdate()
        {
            if (depthFrameProvider == null || updateCoverageShader == null || updateKernel == InvalidKernel
                || coveragePointsBuffer == null || coverageMetadataBuffer == null || voxelKeysBuffer == null || voxelOccupancyBuffer == null)
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
            var cameraToWorld = CalculateDepthCameraToWorld(descriptor);
            LogDepthPoseDiagnosticsIfNeeded(descriptor, cameraToWorld, eyeIndex);
            AddFrustumSampleIfNeeded(descriptor, cameraToWorld);

            updateCoverageShader.SetTexture(updateKernel, "_DepthTexture", depthTexture);
            updateCoverageShader.SetBuffer(updateKernel, "_VoxelKeys", voxelKeysBuffer);
            updateCoverageShader.SetBuffer(updateKernel, "_VoxelOccupancy", voxelOccupancyBuffer);
            updateCoverageShader.SetBuffer(updateKernel, "_CoveragePoints", coveragePointsBuffer);
            updateCoverageShader.SetBuffer(updateKernel, "_CoverageMetadata", coverageMetadataBuffer);
            updateCoverageShader.SetInt("_Width", depthTexture.width);
            updateCoverageShader.SetInt("_Height", depthTexture.height);
            updateCoverageShader.SetInt("_EyeIndex", eyeIndex);
            updateCoverageShader.SetInt("_SamplingStep", settings.samplingStep);
            updateCoverageShader.SetInt("_MaxVoxels", settings.maxVoxels);
            updateCoverageShader.SetInt("_SegmentId", currentSegmentId);
            updateCoverageShader.SetFloat("_VoxelSizeMeters", settings.voxelSizeMeters);
            updateCoverageShader.SetFloat("_MinDepthMeters", settings.minDepthMeters);
            updateCoverageShader.SetFloat("_MaxDepthMeters", settings.maxDepthMeters);
            updateCoverageShader.SetFloat("_FovLeftTan", descriptor.fovLeftAngleTangent);
            updateCoverageShader.SetFloat("_FovRightTan", descriptor.fovRightAngleTangent);
            updateCoverageShader.SetFloat("_FovTopTan", descriptor.fovTopAngleTangent);
            updateCoverageShader.SetFloat("_FovDownTan", descriptor.fovDownAngleTangent);
            updateCoverageShader.SetFloat("_NearZ", descriptor.nearZ);
            updateCoverageShader.SetFloat("_FarZ", descriptor.farZ);
            updateCoverageShader.SetInt("_UseInfiniteFar", ShouldUseInfiniteFar(descriptor.nearZ, descriptor.farZ) ? 1 : 0);
            updateCoverageShader.SetInt("_FlipVerticalProjection", settings.flipVerticalProjection ? 1 : 0);
            updateCoverageShader.SetMatrix("_DepthCameraToWorld", cameraToWorld);

            var groupsX = Mathf.CeilToInt(depthTexture.width / (float)(KernelThreadGroupSize * settings.samplingStep));
            var groupsY = Mathf.CeilToInt(depthTexture.height / (float)(KernelThreadGroupSize * settings.samplingStep));
            updateCoverageShader.Dispatch(updateKernel, Mathf.Max(1, groupsX), Mathf.Max(1, groupsY), 1);
        }


        private void LogDepthPoseDiagnosticsIfNeeded(DepthFrameDesc descriptor, Matrix4x4 cameraToWorld, int eyeIndex)
        {
            if (!settings.logPoseDiagnostics || Time.realtimeSinceStartup < nextDepthPoseDiagnosticRealtime)
            {
                return;
            }

            var origin = cameraToWorld.MultiplyPoint3x4(Vector3.zero);
            var forward = cameraToWorld.MultiplyVector(Vector3.forward).normalized;
            var right = cameraToWorld.MultiplyVector(Vector3.right).normalized;
            var up = cameraToWorld.MultiplyVector(Vector3.up).normalized;
            Debug.Log(
                $"[{Constants.LOG_TAG}] Depth coverage pose diagnostic: eye={eyeIndex}, tsNs={descriptor.timestampNs}, "
                + $"posePos={descriptor.createPoseLocation}, poseRot={descriptor.createPoseRotation}, "
                + $"worldOrigin={origin}, worldForward={forward}, worldRight={right}, worldUp={up}, "
                + $"fovTan=({descriptor.fovLeftAngleTangent:F4},{descriptor.fovRightAngleTangent:F4},{descriptor.fovTopAngleTangent:F4},{descriptor.fovDownAngleTangent:F4}), "
                + $"nearFar=({descriptor.nearZ:F4},{descriptor.farZ:F4}), "
                + $"flipVerticalProjection={settings.flipVerticalProjection}, "
                + $"trackingSpace={(trackingSpace != null ? trackingSpace.name : "none")}");
            nextDepthPoseDiagnosticRealtime = Time.realtimeSinceStartup + settings.poseDiagnosticIntervalSeconds;
        }

        private Matrix4x4 CalculateDepthCameraToWorld(DepthFrameDesc descriptor)
        {
            var depthCameraToTracking = Matrix4x4.TRS(
                descriptor.createPoseLocation,
                descriptor.createPoseRotation,
                new Vector3(1.0f, 1.0f, -1.0f));

            return trackingSpace != null
                ? trackingSpace.localToWorldMatrix * depthCameraToTracking
                : depthCameraToTracking;
        }

        private static bool ShouldUseInfiniteFar(float nearZ, float farZ)
        {
            return float.IsInfinity(farZ) || float.IsNaN(farZ) || farZ < nearZ;
        }

        private bool EnsureDependencies()
        {
            ResolveDepthFrameProvider();
            ResolveKernels();
            ResolveMaterial();

#if UNITY_EDITOR
            if (UsesEditorDebugSource)
            {
                if (renderMode == DepthCoverageRenderMode.ProceduralBillboard && coveragePointMaterial == null && runtimeCoverageMaterial == null)
                {
                    Debug.LogWarning($"[{Constants.LOG_TAG}] Editor debug coverage requires a coverage point material or shader when procedural rendering is selected.");
                    return false;
                }

                return true;
            }
#endif

            if (depthFrameProvider == null)
            {
                depthFrameProvider = gameObject.AddComponent<DepthFrameProvider>();
            }

            if (updateCoverageShader == null || updateKernel == InvalidKernel || clearKernel == InvalidKernel)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Live coverage is enabled, but UpdateDepthCoverage.compute is not assigned or is missing required kernels.");
                return false;
            }

            if (renderMode == DepthCoverageRenderMode.ProceduralBillboard && coveragePointMaterial == null && runtimeCoverageMaterial == null)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Live coverage is enabled, but no coverage point material or shader is assigned for procedural rendering.");
                return false;
            }

            return true;
        }

        private void AllocateBuffers(int maxVoxels)
        {
            if (voxelKeysBuffer != null && voxelOccupancyBuffer != null && coveragePointsBuffer != null && coverageMetadataBuffer != null && allocatedMaxVoxels == maxVoxels)
            {
                return;
            }

            ReleaseBuffers();
            allocatedMaxVoxels = maxVoxels;
            voxelKeysBuffer = new ComputeBuffer(maxVoxels, sizeof(int) * 4, ComputeBufferType.Structured);
            voxelOccupancyBuffer = new ComputeBuffer(maxVoxels, sizeof(int), ComputeBufferType.Structured);
            coveragePointsBuffer = new ComputeBuffer(maxVoxels, sizeof(float) * 4, ComputeBufferType.Structured);
            coverageMetadataBuffer = new ComputeBuffer(maxVoxels, sizeof(float) * 2, ComputeBufferType.Structured);
        }

        private void ClearCoverageBuffers()
        {
            if (updateCoverageShader == null || clearKernel == InvalidKernel || voxelKeysBuffer == null || voxelOccupancyBuffer == null || coveragePointsBuffer == null || coverageMetadataBuffer == null)
            {
                return;
            }

            updateCoverageShader.SetBuffer(clearKernel, "_VoxelKeys", voxelKeysBuffer);
            updateCoverageShader.SetBuffer(clearKernel, "_VoxelOccupancy", voxelOccupancyBuffer);
            updateCoverageShader.SetBuffer(clearKernel, "_CoveragePoints", coveragePointsBuffer);
            updateCoverageShader.SetBuffer(clearKernel, "_CoverageMetadata", coverageMetadataBuffer);
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
            coverageMetadataBuffer?.Release();
            coverageMetadataBuffer = null;
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


        private void PrepareRenderer()
        {
            if (renderMode != DepthCoverageRenderMode.ParticleSystem)
            {
                return;
            }

            particleRenderer ??= new DepthCoverageParticleRenderer();
            particleRenderer.Configure(transform, settings.maxVoxels, pointSizeMeters, previousSegmentAlpha, settings.minDepthMeters, settings.maxDepthMeters);
            particleRenderer.Clear();
        }

        private void RefreshParticleRendererIfNeeded()
        {
            if (renderMode != DepthCoverageRenderMode.ParticleSystem
                || particleRenderer == null
                || coveragePointsBuffer == null
                || coverageMetadataBuffer == null
                || voxelOccupancyBuffer == null)
            {
                return;
            }

            particleRenderer.RefreshIfNeeded(
                coveragePointsBuffer,
                coverageMetadataBuffer,
                voxelOccupancyBuffer,
                currentSegmentId,
                particleRefreshIntervalSeconds);
        }

        private void ClearParticles()
        {
            particleRenderer?.Stop();
        }

        private void PrepareFrustumRenderer()
        {
            if (!settings.showSampleFrustums || settings.maxFrustumSamples <= 0)
            {
                return;
            }

            frustumHistoryRenderer ??= new DepthFrustumHistoryRenderer();
            frustumHistoryRenderer.Configure(transform, settings.maxFrustumSamples, frustumLineWidthMeters, frustumAlpha);
            frustumHistoryRenderer.Clear();
        }

        private void AddFrustumSampleIfNeeded(DepthFrameDesc descriptor, Matrix4x4 cameraToWorld)
        {
            if (!settings.showSampleFrustums || settings.maxFrustumSamples <= 0)
            {
                return;
            }

            if (Time.realtimeSinceStartup < nextFrustumSampleRealtime)
            {
                return;
            }

            frustumHistoryRenderer ??= new DepthFrustumHistoryRenderer();
            frustumHistoryRenderer.Configure(transform, settings.maxFrustumSamples, frustumLineWidthMeters, frustumAlpha);
            frustumHistoryRenderer.AddSample(descriptor, cameraToWorld, frustumDistanceMeters, currentSegmentId);
            nextFrustumSampleRealtime = Time.realtimeSinceStartup + settings.frustumSampleIntervalSeconds;
        }

        private void ClearFrustumHistory()
        {
            frustumHistoryRenderer?.Clear();
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
        [ContextMenu("Start Editor Debug Coverage")]
        private void StartEditorDebugCoverageFromContextMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"[{Constants.LOG_TAG}] Enter Play Mode before starting editor debug coverage.");
                return;
            }

            if (editorCoverageSource == EditorCoverageSource.DepthProvider)
            {
                editorCoverageSource = EditorCoverageSource.DebugGrid;
            }

            _ = TryStartVisualization();
        }

        [ContextMenu("Stop Editor Debug Coverage")]
        private void StopEditorDebugCoverageFromContextMenu()
        {
            StopVisualization();
        }

        private void FillEditorDebugCoverage()
        {
            if (coveragePointsBuffer == null || coverageMetadataBuffer == null || voxelKeysBuffer == null || voxelOccupancyBuffer == null)
            {
                return;
            }

            var maxVoxels = settings.maxVoxels;
            var points = new Vector4[maxVoxels];
            var metadata = new Vector2[maxVoxels];
            var occupancy = new int[maxVoxels];
            var keys = new EditorVoxelKey[maxVoxels];
            var count = editorCoverageSource switch
            {
                EditorCoverageSource.DebugAxes => FillEditorDebugAxes(points, occupancy, keys),
                EditorCoverageSource.DebugFrustum => FillEditorDebugFrustum(points, occupancy, keys),
                _ => FillEditorDebugGrid(points, occupancy, keys),
            };

            FillEditorMetadata(points, metadata, occupancy);
            voxelKeysBuffer.SetData(keys);
            voxelOccupancyBuffer.SetData(occupancy);
            coveragePointsBuffer.SetData(points);
            coverageMetadataBuffer.SetData(metadata);

            if (renderMode == DepthCoverageRenderMode.ParticleSystem)
            {
                particleRenderer?.ApplyImmediate(points, metadata, occupancy, currentSegmentId);
            }

            Debug.Log($"[{Constants.LOG_TAG}] Editor debug coverage generated {count} points from {editorCoverageSource}.");
        }

        private int FillEditorDebugGrid(Vector4[] points, int[] occupancy, EditorVoxelKey[] keys)
        {
            var resolution = Mathf.Max(2, editorDebugGridResolution);
            var spacing = editorDebugGridSizeMeters / (resolution - 1);
            var halfSize = editorDebugGridSizeMeters * 0.5f;
            var origin = transform.position + transform.forward * editorDebugGridDistanceMeters;
            var count = 0;

            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution && count < points.Length; x++)
                {
                    var position = origin
                        + transform.right * (-halfSize + x * spacing)
                        + transform.up * (-halfSize + y * spacing);
                    points[count] = new Vector4(position.x, position.y, position.z, currentSegmentId);
                    occupancy[count] = 1;
                    keys[count] = new EditorVoxelKey(x, y, 0, currentSegmentId);
                    count++;
                }
            }

            return count;
        }

        private int FillEditorDebugAxes(Vector4[] points, int[] occupancy, EditorVoxelKey[] keys)
        {
            var samplesPerAxis = Mathf.Max(2, editorDebugGridResolution);
            var origin = transform.position + transform.forward * editorDebugGridDistanceMeters;
            var count = 0;
            count = FillEditorDebugLine(points, occupancy, keys, count, origin, origin + Vector3.right * editorDebugAxisLengthMeters, samplesPerAxis, 0);
            count = FillEditorDebugLine(points, occupancy, keys, count, origin, origin + Vector3.up * editorDebugAxisLengthMeters, samplesPerAxis, 1);
            count = FillEditorDebugLine(points, occupancy, keys, count, origin, origin + Vector3.forward * editorDebugAxisLengthMeters, samplesPerAxis, 2);
            return count;
        }

        private int FillEditorDebugFrustum(Vector4[] points, int[] occupancy, EditorVoxelKey[] keys)
        {
            var origin = transform.position;
            var center = origin + transform.forward * editorDebugGridDistanceMeters;
            var halfSize = editorDebugGridSizeMeters * 0.5f;
            var topLeft = center - transform.right * halfSize + transform.up * halfSize;
            var topRight = center + transform.right * halfSize + transform.up * halfSize;
            var bottomLeft = center - transform.right * halfSize - transform.up * halfSize;
            var bottomRight = center + transform.right * halfSize - transform.up * halfSize;
            var samples = Mathf.Max(2, editorDebugGridResolution);
            var count = 0;
            count = FillEditorDebugLine(points, occupancy, keys, count, origin, topLeft, samples, 0);
            count = FillEditorDebugLine(points, occupancy, keys, count, origin, topRight, samples, 1);
            count = FillEditorDebugLine(points, occupancy, keys, count, origin, bottomLeft, samples, 2);
            count = FillEditorDebugLine(points, occupancy, keys, count, origin, bottomRight, samples, 3);
            count = FillEditorDebugLine(points, occupancy, keys, count, topLeft, topRight, samples, 4);
            count = FillEditorDebugLine(points, occupancy, keys, count, topRight, bottomRight, samples, 5);
            count = FillEditorDebugLine(points, occupancy, keys, count, bottomRight, bottomLeft, samples, 6);
            count = FillEditorDebugLine(points, occupancy, keys, count, bottomLeft, topLeft, samples, 7);
            return count;
        }

        private int FillEditorDebugLine(
            Vector4[] points,
            int[] occupancy,
            EditorVoxelKey[] keys,
            int startIndex,
            Vector3 start,
            Vector3 end,
            int samples,
            int lineId)
        {
            var count = startIndex;
            for (var i = 0; i < samples && count < points.Length; i++)
            {
                var t = samples == 1 ? 0.0f : i / (float)(samples - 1);
                var position = Vector3.Lerp(start, end, t);
                points[count] = new Vector4(position.x, position.y, position.z, currentSegmentId);
                occupancy[count] = 1;
                keys[count] = new EditorVoxelKey(lineId, i, 0, currentSegmentId);
                count++;
            }

            return count;
        }

        private void FillEditorMetadata(Vector4[] points, Vector2[] metadata, int[] occupancy)
        {
            for (var i = 0; i < points.Length && i < metadata.Length && i < occupancy.Length; i++)
            {
                if (occupancy[i] == 0)
                {
                    continue;
                }

                var point = new Vector3(points[i].x, points[i].y, points[i].z);
                var depth = Vector3.Distance(transform.position, point);
                metadata[i] = new Vector2(currentSegmentId, Mathf.Clamp(depth, settings.minDepthMeters, settings.maxDepthMeters));
            }
        }

#endif

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
