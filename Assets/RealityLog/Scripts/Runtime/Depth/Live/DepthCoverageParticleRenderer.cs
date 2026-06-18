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
        private int[]? readbackOccupancy;
        private bool isReadbackPending;
        private bool hasPointsReadback;
        private bool hasOccupancyReadback;
        private float nextRefreshRealtime;
        private int maxParticles;
        private float pointSizeMeters;
        private float previousSegmentAlpha;
        private int currentSegmentId;
        private bool isActive;

        public void Configure(
            Transform parent,
            int maxVoxels,
            float pointSizeMeters,
            float previousSegmentAlpha)
        {
            maxParticles = Mathf.Max(1, maxVoxels);
            this.pointSizeMeters = Mathf.Max(0.001f, pointSizeMeters);
            this.previousSegmentAlpha = Mathf.Clamp01(previousSegmentAlpha);
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

            particles = null;
            readbackPoints = null;
            readbackOccupancy = null;
            FinishReadback();
        }

        public void RefreshIfNeeded(
            ComputeBuffer coveragePointsBuffer,
            ComputeBuffer voxelOccupancyBuffer,
            int currentSegmentId,
            float refreshIntervalSeconds)
        {
            if (!isActive
                || particles == null || readbackPoints == null || readbackOccupancy == null
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
                voxelOccupancyBuffer.GetData(readbackOccupancy);
                ApplyReadback();
                return;
            }

            isReadbackPending = true;
            hasPointsReadback = false;
            hasOccupancyReadback = false;
            AsyncGPUReadback.Request(coveragePointsBuffer, OnPointsReadback);
            AsyncGPUReadback.Request(voxelOccupancyBuffer, OnOccupancyReadback);
        }

        public void ApplyImmediate(Vector4[] points, int[] occupancy, int currentSegmentId)
        {
            if (!isActive || readbackPoints == null || readbackOccupancy == null)
            {
                return;
            }

            this.currentSegmentId = currentSegmentId;
            Array.Copy(points, readbackPoints, Math.Min(points.Length, readbackPoints.Length));
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
                && readbackOccupancy != null && readbackOccupancy.Length == size)
            {
                return;
            }

            particles = new ParticleSystem.Particle[size];
            readbackPoints = new Vector4[size];
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
            if (!hasPointsReadback || !hasOccupancyReadback)
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
            hasOccupancyReadback = false;
        }

        private void ApplyReadback()
        {
            if (particleSystem == null || particles == null || readbackPoints == null || readbackOccupancy == null)
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
                particles[count] = new ParticleSystem.Particle
                {
                    position = new Vector3(point.x, point.y, point.z),
                    startSize = pointSizeMeters,
                    startLifetime = 999999.0f,
                    remainingLifetime = 999999.0f,
                    startColor = ParticleColorForSegment(Mathf.RoundToInt(point.w))
                };
                count++;
            }

            particleSystem.SetParticles(particles, count);
        }

        private Color32 ParticleColorForSegment(int segmentId)
        {
            var alpha = segmentId == currentSegmentId ? byte.MaxValue : (byte)Mathf.RoundToInt(previousSegmentAlpha * byte.MaxValue);
            return new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, alpha);
        }
    }
}
