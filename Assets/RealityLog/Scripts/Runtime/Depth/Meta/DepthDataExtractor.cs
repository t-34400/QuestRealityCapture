/*
 * Based on the Meta Quest SDK (DepthProviderOpenXR) implementation:
 *
 * This file includes derivative work inspired by Meta Platforms' SDK.
 * Portions of the implementation logic, API usage, and structure follow the Meta OpenXR Occlusion feature 
 * from the Oculus Integration and OpenXR SDK.
 *
 * Copyright (c) 2024-2025. This derivative work is subject to the Oculus SDK License Agreement.
 *
 * You may obtain a copy of the License at:
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the SDK and this derivative work
 * are distributed under the License on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */

# nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.API;

namespace RealityLog.Depth
{
    public class DepthDataExtractor
    {
        private XRDisplaySubsystem? displaySubsystem;
        private XROcclusionSubsystem? occlusionSubsystem;
        private Dictionary<IntPtr, (uint textureId, RenderTexture? renderTexture)>? depthTextures;
        private IntPtr? prevNativeTexture;
        private DepthFrameDesc[] depthFrameDescs;

        public DepthDataExtractor()
        {
            depthFrameDescs = new DepthFrameDesc[2]; // left, right

            var loader = XRGeneralSettings.Instance.Manager.activeLoader;
            var displaySubsystem = loader.GetLoadedSubsystem<XRDisplaySubsystem>();

            if (loader is not OpenXRLoader)
            {
                return;
            }

            this.displaySubsystem = displaySubsystem;
            occlusionSubsystem = loader.GetLoadedSubsystem<XROcclusionSubsystem>();

            if (occlusionSubsystem == null)
            {
                Debug.LogError("XROcclusionSubsystem not found. Enable Meta Quest: Occlusion in Project Settings.");
            }
        }

        public bool IsSupported => displaySubsystem != null && occlusionSubsystem != null;

        public void SetDepthEnabled(bool isEnabled)
        {
            if (occlusionSubsystem == null) return;

            if (isEnabled)
            {
                occlusionSubsystem.Start();
            }
            else
            {
                occlusionSubsystem.Stop();
                if (depthTextures != null)
                {
                    foreach (var entry in depthTextures.Values)
                    {
                        if (entry.renderTexture != null)
                        {
                            UnityEngine.Object.Destroy(entry.renderTexture);
                        }
                    }
                    depthTextures = null;
                }
            }
        }

        public bool TryGetUpdatedDepthTexture(out RenderTexture depthTexture, out DepthFrameDesc[] frameDescriptors)
        {
            depthTexture = default!;
            frameDescriptors = depthFrameDescs;

            if (!IsSupported
                || occlusionSubsystem == null || !occlusionSubsystem.running
                || displaySubsystem == null || !displaySubsystem.running)
            {
                return false;
            }

            if (depthTextures == null)
            {
                if (!occlusionSubsystem.TryGetSwapchainTextureDescriptors(out var swapchains))
                {
                    Debug.LogError("Failed to get swapchain descriptors.");
                    return false;
                }

                depthTextures = new Dictionary<IntPtr, (uint, RenderTexture?)>();

                foreach (var descriptors in swapchains)
                {
                    if (descriptors.Length == 0) continue;
                    var desc = descriptors[0];

                    if (desc.nativeTexture == IntPtr.Zero) continue;

                    if (!UnityXRDisplay.CreateTexture(ToUnityXRRenderTextureDesc(desc), out var textureId))
                    {
                        Debug.LogError("Failed to create texture.");
                        continue;
                    }

                    depthTextures[desc.nativeTexture] = (textureId, null);
                }
            }

            if (!occlusionSubsystem.TryGetFrame(Allocator.Temp, out var frame)
                || !frame.TryGetTimestamp(out var timestampNs)
                || !frame.TryGetFovs(out var fovs)
                || !frame.TryGetPoses(out var poses)
                || !frame.TryGetNearFarPlanes(out var planes))
            {
                return false;
            }

            var textures = occlusionSubsystem.GetTextureDescriptors(Allocator.Temp);
            if (textures.Length == 0) return false;

            var nativeTexture = textures[0].nativeTexture;
            if (nativeTexture == prevNativeTexture) return false;
            prevNativeTexture = nativeTexture;

            if (!depthTextures.TryGetValue(nativeTexture, out var textureData))
            {
                Debug.LogError("Unknown native texture received.");
                return false;
            }

            var _depthTexture = textureData.renderTexture;

            if (_depthTexture == null)
            {
                _depthTexture = displaySubsystem.GetRenderTexture(textureData.textureId);
                if (_depthTexture == null)
                {
                    Debug.Log("GetRenderTexture failed.");
                    return false;
                }
                depthTextures[nativeTexture] = (textureData.textureId, _depthTexture);
            }

            depthTexture = _depthTexture;

            for (int i = 0; i < depthFrameDescs.Length; i++)
            {
                depthFrameDescs[i] = new DepthFrameDesc
                {
                    timestampNs = timestampNs,
                    createPoseLocation = poses[i].position,
                    createPoseRotation = poses[i].rotation,
                    fovLeftAngleTangent = Mathf.Tan(Mathf.Abs(fovs[i].angleLeft)),
                    fovRightAngleTangent = Mathf.Tan(Mathf.Abs(fovs[i].angleRight)),
                    fovTopAngleTangent = Mathf.Tan(Mathf.Abs(fovs[i].angleUp)),
                    fovDownAngleTangent = Mathf.Tan(Mathf.Abs(fovs[i].angleDown)),
                    nearZ = planes.nearZ,
                    farZ = planes.farZ
                };
            }

            return true;
        }

        private static UnityXRRenderTextureDesc ToUnityXRRenderTextureDesc(XRTextureDescriptor desc)
        {
            return new UnityXRRenderTextureDesc
            {
                shadingRateFormat = UnityXRShadingRateFormat.kUnityXRShadingRateFormatNone,
                shadingRate = new UnityXRTextureData(),
                width = (uint)desc.width,
                height = (uint)desc.height,
                textureArrayLength = (uint)desc.depth,
                flags = 0,
                colorFormat = UnityXRRenderTextureFormat.kUnityXRRenderTextureFormatNone,
                depthFormat = ToUnityXRDepthTextureFormat(desc.format),
                depth = new UnityXRTextureData { nativePtr = desc.nativeTexture }
            };
        }

        private static UnityXRDepthTextureFormat ToUnityXRDepthTextureFormat(TextureFormat format)
        {
            return format switch
            {
                TextureFormat.RFloat => UnityXRDepthTextureFormat.kUnityXRDepthTextureFormat24bitOrGreater,
                TextureFormat.R16 or TextureFormat.RHalf => UnityXRDepthTextureFormat.kUnityXRDepthTextureFormat16bit,
                _ => throw new NotSupportedException($"Unsupported texture format: {format}")
            };
        }
    }
}