#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace RealityLog.Recording
{
    public sealed class TrackingEventMarkerRenderer : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float markerSizeMeters = 0.12f;
        [SerializeField, Min(1)] private int maxMarkers = 128;
        [SerializeField] private Material? markerMaterial = null;

        private readonly Queue<GameObject> markers = new();
        private Material? runtimeMarkerMaterial;
        private bool isRendering;

        public int MarkerCount => markers.Count;

        public void StartRendering()
        {
            ClearMarkers();
            isRendering = true;
        }

        public void StopRendering()
        {
            isRendering = false;
            ClearMarkers();
        }

        public void OnTrackingEventRaised(TrackingQualityEvent trackingEvent)
        {
            if (!isRendering)
            {
                return;
            }

            while (markers.Count >= maxMarkers)
            {
                DestroyMarker(markers.Dequeue());
            }

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"TrackingEvent_{trackingEvent.segmentId}_{trackingEvent.reason}";
            marker.transform.SetParent(transform, true);
            marker.transform.position = trackingEvent.position;
            marker.transform.localScale = Vector3.one * markerSizeMeters;

            var collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = ResolveMarkerMaterial();
            }

            markers.Enqueue(marker);
        }

        private Material? ResolveMarkerMaterial()
        {
            if (markerMaterial != null)
            {
                return markerMaterial;
            }

            if (runtimeMarkerMaterial != null)
            {
                return runtimeMarkerMaterial;
            }

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            runtimeMarkerMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (runtimeMarkerMaterial.HasProperty("_Color"))
            {
                runtimeMarkerMaterial.SetColor("_Color", Color.yellow);
            }

            return runtimeMarkerMaterial;
        }

        private void ClearMarkers()
        {
            while (markers.Count > 0)
            {
                DestroyMarker(markers.Dequeue());
            }
        }

        private static void DestroyMarker(GameObject marker)
        {
            if (Application.isPlaying)
            {
                Destroy(marker);
            }
            else
            {
                DestroyImmediate(marker);
            }
        }

        private void OnDestroy()
        {
            StopRendering();
            if (runtimeMarkerMaterial != null)
            {
                Destroy(runtimeMarkerMaterial);
                runtimeMarkerMaterial = null;
            }
        }
    }
}
