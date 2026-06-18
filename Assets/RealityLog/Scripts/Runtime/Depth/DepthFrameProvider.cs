#nullable enable

using UnityEngine;
using UnityEngine.Android;

namespace RealityLog.Depth
{
    public sealed class DepthFrameProvider : MonoBehaviour
    {
        private const int FrameDescriptorCount = 2;

        private DepthDataExtractor? depthDataExtractor;
        private DepthFrameDesc[] latestFrameDescriptors = new DepthFrameDesc[FrameDescriptorCount];
        private RenderTexture? latestDepthTexture;
        private bool hasLatestFrame;
        private bool hasScenePermission;
        private bool depthExtractorEnabled;
        private int depthUsageCount;

        public bool IsDepthAvailable => hasScenePermission && depthDataExtractor is { IsSupported: true };
        public bool HasLatestFrame => hasLatestFrame;

        public void BeginDepthUsage()
        {
            depthUsageCount++;
            ApplyDepthEnabledState();
        }

        public void EndDepthUsage()
        {
            if (depthUsageCount > 0)
            {
                depthUsageCount--;
            }

            ApplyDepthEnabledState();
        }

        public bool TryGetLatestFrame(out RenderTexture depthTexture, out DepthFrameDesc[] frameDescriptors)
        {
            depthTexture = default!;
            frameDescriptors = latestFrameDescriptors;

            if (!hasLatestFrame || latestDepthTexture == null || !latestDepthTexture.IsCreated())
            {
                return false;
            }

            depthTexture = latestDepthTexture;
            return true;
        }

        private void Awake()
        {
            depthDataExtractor = new DepthDataExtractor();
            Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDestroy()
        {
            Application.onBeforeRender -= OnBeforeRender;
            depthUsageCount = 0;
            ApplyDepthEnabledState();
        }

        private void OnBeforeRender()
        {
            RefreshScenePermission();
            ApplyDepthEnabledState();

            if (!depthExtractorEnabled || depthDataExtractor == null)
            {
                return;
            }

            if (!depthDataExtractor.TryGetUpdatedDepthTexture(out var depthTexture, out var frameDescriptors))
            {
                return;
            }

            if (frameDescriptors.Length != FrameDescriptorCount)
            {
                Debug.LogError($"[{Constants.LOG_TAG}] Expected exactly two depth frame descriptors (left and right).");
                return;
            }

            latestDepthTexture = depthTexture;
            for (var i = 0; i < FrameDescriptorCount; ++i)
            {
                latestFrameDescriptors[i] = frameDescriptors[i];
            }

            hasLatestFrame = true;
        }

        private void RefreshScenePermission()
        {
            if (hasScenePermission)
            {
                return;
            }

            hasScenePermission = Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission);
        }

        private void ApplyDepthEnabledState()
        {
            var shouldEnable = depthUsageCount > 0 && hasScenePermission;
            if (depthExtractorEnabled == shouldEnable || depthDataExtractor == null)
            {
                return;
            }

            depthDataExtractor.SetDepthEnabled(shouldEnable);
            depthExtractorEnabled = shouldEnable;

            if (!shouldEnable)
            {
                hasLatestFrame = false;
                latestDepthTexture = null;
            }
        }
    }
}
