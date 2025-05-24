# nullable enable

using UnityEngine;

namespace RealityLog.Depth
{
    public struct DepthFrameDesc
    {
        public long timestampNs;
        public Vector3 createPoseLocation;
        public Quaternion createPoseRotation;
        public float fovLeftAngleTangent;
        public float fovRightAngleTangent;
        public float fovTopAngleTangent;
        public float fovDownAngleTangent;
        public float nearZ;
        public float farZ;
    }
}
