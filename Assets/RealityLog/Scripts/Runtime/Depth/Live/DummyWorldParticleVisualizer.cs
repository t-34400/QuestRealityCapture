#nullable enable

using UnityEngine;

namespace RealityLog.Depth
{
    public enum DummyWorldParticleShape
    {
        PlaneGrid = 0,
        CubeGrid = 1,
        Axes = 2
    }

    /// <summary>
    /// Displays fixed world-space particles without using depth data.
    /// This component isolates Unity/XR/ParticleSystem rendering from depth sampling and pose conversion.
    /// </summary>
    public sealed class DummyWorldParticleVisualizer : MonoBehaviour
    {
        [SerializeField] private bool startOnPlay = true;
        [SerializeField] private Transform? placementReference = null;
        [SerializeField] private Material? particleMaterial = null;
        [SerializeField] private DummyWorldParticleShape shape = DummyWorldParticleShape.CubeGrid;
        [SerializeField, Min(2)] private int resolution = 7;
        [SerializeField, Min(0.01f)] private float sizeMeters = 1.0f;
        [SerializeField, Min(0.01f)] private float distanceMeters = 2.0f;
        [SerializeField, Min(0.001f)] private float pointSizeMeters = 0.035f;
        [SerializeField] private Color pointColor = Color.cyan;

        private ParticleSystem? particleSystemComponent;
        private ParticleSystem.Particle[]? particles;

        private void Start()
        {
            if (startOnPlay)
            {
                Show();
            }
        }

        private void OnDestroy()
        {
            if (particleSystemComponent != null)
            {
                Destroy(particleSystemComponent.gameObject);
                particleSystemComponent = null;
            }
        }

        [ContextMenu("Show Dummy World Particles")]
        public void Show()
        {
            EnsureParticleSystem();
            ConfigureParticleSystem();

            var reference = ResolvePlacementReference();
            var origin = reference.position + reference.forward * distanceMeters;
            particles = shape switch
            {
                DummyWorldParticleShape.Axes => BuildAxes(origin),
                DummyWorldParticleShape.PlaneGrid => BuildPlaneGrid(origin, reference.right, reference.up),
                _ => BuildCubeGrid(origin, reference.right, reference.up, reference.forward),
            };

            particleSystemComponent!.SetParticles(particles, particles.Length);
            particleSystemComponent.Play(false);
            Debug.Log($"[{Constants.LOG_TAG}] Dummy world particles generated: shape={shape}, count={particles.Length}, origin={origin}.");
        }

        [ContextMenu("Clear Dummy World Particles")]
        public void Clear()
        {
            particleSystemComponent?.Clear(true);
        }

        private Transform ResolvePlacementReference()
        {
            if (placementReference != null)
            {
                return placementReference;
            }

            var mainCamera = UnityEngine.Camera.main;
            return mainCamera != null ? mainCamera.transform : transform;
        }

        private void EnsureParticleSystem()
        {
            if (particleSystemComponent != null)
            {
                return;
            }

            var particleObject = new GameObject("Dummy World Particles");
            particleObject.transform.SetParent(transform, false);
            particleSystemComponent = particleObject.AddComponent<ParticleSystem>();
        }

        private void ConfigureParticleSystem()
        {
            if (particleSystemComponent == null)
            {
                return;
            }

            var main = particleSystemComponent.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, EstimateParticleCount());
            main.startLifetime = 999999.0f;
            main.startSpeed = 0.0f;
            main.startSize = pointSizeMeters;

            var emission = particleSystemComponent.emission;
            emission.enabled = false;

            var shapeModule = particleSystemComponent.shape;
            shapeModule.enabled = false;

            var renderer = particleSystemComponent.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.None;
            if (particleMaterial != null)
            {
                renderer.material = particleMaterial;
            }
        }

        private int EstimateParticleCount()
        {
            var clampedResolution = Mathf.Max(2, resolution);
            return shape switch
            {
                DummyWorldParticleShape.Axes => clampedResolution * 3,
                DummyWorldParticleShape.PlaneGrid => clampedResolution * clampedResolution,
                _ => clampedResolution * clampedResolution * clampedResolution,
            };
        }

        private ParticleSystem.Particle[] BuildPlaneGrid(Vector3 origin, Vector3 right, Vector3 up)
        {
            var count = Mathf.Max(2, resolution);
            var result = new ParticleSystem.Particle[count * count];
            var spacing = sizeMeters / (count - 1);
            var halfSize = sizeMeters * 0.5f;
            var index = 0;
            for (var y = 0; y < count; y++)
            {
                for (var x = 0; x < count; x++)
                {
                    var position = origin
                        + right * (-halfSize + x * spacing)
                        + up * (-halfSize + y * spacing);
                    result[index++] = CreateParticle(position, pointColor);
                }
            }

            return result;
        }

        private ParticleSystem.Particle[] BuildCubeGrid(Vector3 origin, Vector3 right, Vector3 up, Vector3 forward)
        {
            var count = Mathf.Max(2, resolution);
            var result = new ParticleSystem.Particle[count * count * count];
            var spacing = sizeMeters / (count - 1);
            var halfSize = sizeMeters * 0.5f;
            var index = 0;
            for (var z = 0; z < count; z++)
            {
                for (var y = 0; y < count; y++)
                {
                    for (var x = 0; x < count; x++)
                    {
                        var position = origin
                            + right * (-halfSize + x * spacing)
                            + up * (-halfSize + y * spacing)
                            + forward * (-halfSize + z * spacing);
                        result[index++] = CreateParticle(position, pointColor);
                    }
                }
            }

            return result;
        }

        private ParticleSystem.Particle[] BuildAxes(Vector3 origin)
        {
            var count = Mathf.Max(2, resolution);
            var result = new ParticleSystem.Particle[count * 3];
            var index = 0;
            index = FillAxis(result, index, origin, Vector3.right, Color.red, count);
            index = FillAxis(result, index, origin, Vector3.up, Color.green, count);
            _ = FillAxis(result, index, origin, Vector3.forward, Color.blue, count);
            return result;
        }

        private int FillAxis(ParticleSystem.Particle[] target, int startIndex, Vector3 origin, Vector3 direction, Color color, int count)
        {
            var length = sizeMeters;
            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0.0f : i / (float)(count - 1);
                target[startIndex + i] = CreateParticle(origin + direction * (t * length), color);
            }

            return startIndex + count;
        }

        private ParticleSystem.Particle CreateParticle(Vector3 position, Color color)
        {
            return new ParticleSystem.Particle
            {
                position = position,
                startSize = pointSizeMeters,
                startLifetime = 999999.0f,
                remainingLifetime = 999999.0f,
                startColor = color
            };
        }
    }
}
