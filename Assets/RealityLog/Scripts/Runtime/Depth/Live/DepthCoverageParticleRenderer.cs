#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealityLog.Depth
{
    internal sealed class DepthCoverageParticleRenderer : IDisposable
    {
        private ParticleSystem? particleSystem;
        private ParticleSystem.Particle[]? particles;
        private Vector4[]? readbackPoints;
        private Vector2[]? readbackMetadata;
        private int[]? readbackOccupancy;
        private bool isReadbackPending;
        private bool hasPointsReadback;
        private bool hasMetadataReadback;
        private bool hasOccupancyReadback;
        private float nextRefreshRealtime;
        private int maxParticles;
        private float pointSizeMeters;
        private float previousSegmentAlpha;
        private float minDepthMeters;
        private float maxDepthMeters;
        private Material? particleMaterial;
        private int currentSegmentId;
        private bool isActive;

        public void Configure(
            Transform parent,
            int maxVoxels,
            float pointSizeMeters,
            float previousSegmentAlpha,
            float minDepthMeters,
            float maxDepthMeters)
        {
            maxParticles = Mathf.Max(1, maxVoxels);
            this.pointSizeMeters = Mathf.Max(0.001f, pointSizeMeters);
            this.previousSegmentAlpha = Mathf.Clamp01(previousSegmentAlpha);
            this.minDepthMeters = Mathf.Max(0.0f, minDepthMeters);
            this.maxDepthMeters = Mathf.Max(this.minDepthMeters + 0.01f, maxDepthMeters);
            isActive = true;
            EnsureParticleSystem(parent);
            EnsureArrays(maxParticles);
            ConfigureParticleSystem();
        }

        public void Clear()
        {
            particleSystem?.Clear(true);
            FinishReadback();
        }

        public void Stop()
        {
            isActive = false;
            Clear();
        }

        public void Dispose()
        {
            if (particleSystem != null)
            {
                UnityEngine.Object.Destroy(particleSystem.gameObject);
                particleSystem = null;
            }

            if (particleMaterial != null)
            {
                UnityEngine.Object.Destroy(particleMaterial);
                particleMaterial = null;
            }

            particles = null;
            readbackPoints = null;
            readbackMetadata = null;
            readbackOccupancy = null;
            FinishReadback();
        }

        public void RefreshIfNeeded(
            ComputeBuffer coveragePointsBuffer,
            ComputeBuffer coverageMetadataBuffer,
            ComputeBuffer voxelOccupancyBuffer,
            int currentSegmentId,
            float refreshIntervalSeconds)
        {
            if (!isActive
                || particles == null || readbackPoints == null || readbackMetadata == null || readbackOccupancy == null
                || Time.realtimeSinceStartup < nextRefreshRealtime
                || isReadbackPending)
            {
                return;
            }

            this.currentSegmentId = currentSegmentId;
            nextRefreshRealtime = Time.realtimeSinceStartup + Mathf.Max(0.05f, refreshIntervalSeconds);
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                coveragePointsBuffer.GetData(readbackPoints);
                coverageMetadataBuffer.GetData(readbackMetadata);
                voxelOccupancyBuffer.GetData(readbackOccupancy);
                ApplyReadback();
                return;
            }

            isReadbackPending = true;
            hasPointsReadback = false;
            hasMetadataReadback = false;
            hasOccupancyReadback = false;
            AsyncGPUReadback.Request(coveragePointsBuffer, OnPointsReadback);
            AsyncGPUReadback.Request(coverageMetadataBuffer, OnMetadataReadback);
            AsyncGPUReadback.Request(voxelOccupancyBuffer, OnOccupancyReadback);
        }

        public void ApplyImmediate(Vector4[] points, Vector2[] metadata, int[] occupancy, int currentSegmentId)
        {
            if (!isActive || readbackPoints == null || readbackMetadata == null || readbackOccupancy == null)
            {
                return;
            }

            this.currentSegmentId = currentSegmentId;
            Array.Copy(points, readbackPoints, Math.Min(points.Length, readbackPoints.Length));
            Array.Copy(metadata, readbackMetadata, Math.Min(metadata.Length, readbackMetadata.Length));
            Array.Copy(occupancy, readbackOccupancy, Math.Min(occupancy.Length, readbackOccupancy.Length));
            ApplyReadback();
        }

        private void EnsureParticleSystem(Transform parent)
        {
            if (particleSystem != null)
            {
                return;
            }

            var particleObject = new GameObject("Live Depth Coverage Particles");
            particleObject.transform.SetParent(parent, false);
            particleSystem = particleObject.AddComponent<ParticleSystem>();
        }

        private void EnsureArrays(int size)
        {
            if (particles != null && particles.Length == size
                && readbackPoints != null && readbackPoints.Length == size
                && readbackMetadata != null && readbackMetadata.Length == size
                && readbackOccupancy != null && readbackOccupancy.Length == size)
            {
                return;
            }

            particles = new ParticleSystem.Particle[size];
            readbackPoints = new Vector4[size];
            readbackMetadata = new Vector2[size];
            readbackOccupancy = new int[size];
        }

        private void ConfigureParticleSystem()
        {
            if (particleSystem == null)
            {
                return;
            }

            var main = particleSystem.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.startLifetime = 999999.0f;
            main.startSpeed = 0.0f;
            main.startSize = pointSizeMeters;

            var emission = particleSystem.emission;
            emission.enabled = false;

            var shape = particleSystem.shape;
            shape.enabled = false;

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.alignment = ParticleSystemRenderSpace.View;
            var material = EnsureParticleMaterial();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private Material? EnsureParticleMaterial()
        {
            particleMaterial ??= DepthVisualizationMaterialFactory.CreateParticleMaterial();
            return particleMaterial;
        }

        private void OnPointsReadback(AsyncGPUReadbackRequest request)
        {
            if (!isActive || request.hasError || readbackPoints == null)
            {
                FinishReadback();
                return;
            }

            request.GetData<Vector4>().CopyTo(readbackPoints);
            hasPointsReadback = true;
            TryApplyReadback();
        }

        private void OnMetadataReadback(AsyncGPUReadbackRequest request)
        {
            if (!isActive || request.hasError || readbackMetadata == null)
            {
                FinishReadback();
                return;
            }

            request.GetData<Vector2>().CopyTo(readbackMetadata);
            hasMetadataReadback = true;
            TryApplyReadback();
        }

        private void OnOccupancyReadback(AsyncGPUReadbackRequest request)
        {
            if (!isActive || request.hasError || readbackOccupancy == null)
            {
                FinishReadback();
                return;
            }

            request.GetData<int>().CopyTo(readbackOccupancy);
            hasOccupancyReadback = true;
            TryApplyReadback();
        }

        private void TryApplyReadback()
        {
            if (!hasPointsReadback || !hasMetadataReadback || !hasOccupancyReadback)
            {
                return;
            }

            ApplyReadback();
            FinishReadback();
        }

        private void FinishReadback()
        {
            isReadbackPending = false;
            hasPointsReadback = false;
            hasMetadataReadback = false;
            hasOccupancyReadback = false;
        }

        private void ApplyReadback()
        {
            if (particleSystem == null || particles == null || readbackPoints == null || readbackMetadata == null || readbackOccupancy == null)
            {
                return;
            }

            var count = 0;
            for (var i = 0; i < readbackPoints.Length && count < particles.Length; i++)
            {
                if (readbackOccupancy[i] == 0)
                {
                    continue;
                }

                var point = readbackPoints[i];
                var metadata = readbackMetadata[i];
                particles[count] = new ParticleSystem.Particle
                {
                    position = new Vector3(point.x, point.y, point.z),
                    startSize = pointSizeMeters,
                    startLifetime = 999999.0f,
                    remainingLifetime = 999999.0f,
                    startColor = ParticleColor(Mathf.RoundToInt(metadata.x), metadata.y)
                };
                count++;
            }

            particleSystem.SetParticles(particles, count);
        }

        private Color32 ParticleColor(int segmentId, float depthMeters)
        {
            var normalizedDepth = Mathf.InverseLerp(minDepthMeters, maxDepthMeters, depthMeters);
            var color = Color.Lerp(new Color(1.0f, 0.36f, 0.08f), new Color(0.12f, 0.58f, 1.0f), normalizedDepth);
            var segmentAlpha = segmentId == currentSegmentId ? 1.0f : previousSegmentAlpha;
            var distanceAlpha = Mathf.Lerp(0.95f, 0.45f, normalizedDepth);
            color.a = segmentAlpha * distanceAlpha;
            return color;
        }
    }
}
