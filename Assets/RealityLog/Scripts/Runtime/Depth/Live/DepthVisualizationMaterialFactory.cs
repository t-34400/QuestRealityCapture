#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace RealityLog.Depth
{
    internal static class DepthVisualizationMaterialFactory
    {
        private static readonly string[] ParticleShaderNames =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Unlit",
            "Particles/Standard Unlit",
            "Sprites/Default"
        };

        private static readonly string[] LineShaderNames =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Unlit",
            "Sprites/Default",
            "Unlit/Color"
        };

        public static Material? CreateParticleMaterial()
        {
            return CreateMaterial(ParticleShaderNames, "live depth coverage particles");
        }

        public static Material? CreateLineMaterial()
        {
            return CreateMaterial(LineShaderNames, "live depth frustums");
        }

        private static Material? CreateMaterial(string[] shaderNames, string usage)
        {
            foreach (var shaderName in shaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null || !shader.isSupported)
                {
                    continue;
                }

                var material = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                ConfigureTransparentWhite(material);
                return material;
            }

            Debug.LogWarning($"[{Constants.LOG_TAG}] Could not create a supported material for {usage}; Unity may render the overlay magenta.");
            return null;
        }

        private static void ConfigureTransparentWhite(Material material)
        {
            SetColorIfPresent(material, "_BaseColor", Color.white);
            SetColorIfPresent(material, "_Color", Color.white);

            SetFloatIfPresent(material, "_Surface", 1.0f);
            SetFloatIfPresent(material, "_Blend", 0.0f);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_ZWrite", 0.0f);
            SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
    }
}
