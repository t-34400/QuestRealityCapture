Shader "RealityLog/DepthCoveragePoints"
{
    Properties
    {
        _PointColor ("Point Color", Color) = (0.2, 0.8, 1.0, 0.45)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            StructuredBuffer<float4> _CoveragePoints;
            StructuredBuffer<int> _VoxelOccupancy;
            float _PointSizeMeters;
            float4 _PointColor;

            struct appdata
            {
                uint vertexID : SV_VertexID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float alpha : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                uint pointIndex = v.vertexID / 6u;
                uint cornerIndex = v.vertexID % 6u;

                float2 corner = float2(-1.0, -1.0);
                if (cornerIndex == 1u) corner = float2(1.0, -1.0);
                else if (cornerIndex == 2u) corner = float2(1.0, 1.0);
                else if (cornerIndex == 4u) corner = float2(1.0, 1.0);
                else if (cornerIndex == 5u) corner = float2(-1.0, 1.0);

                int occupied = _VoxelOccupancy[pointIndex];
                float4 point = _CoveragePoints[pointIndex];
                float3 cameraRight = normalize(float3(unity_CameraToWorld[0][0], unity_CameraToWorld[1][0], unity_CameraToWorld[2][0]));
                float3 cameraUp = normalize(float3(unity_CameraToWorld[0][1], unity_CameraToWorld[1][1], unity_CameraToWorld[2][1]));
                corner *= _PointSizeMeters;
                float3 world = point.xyz + cameraRight * corner.x + cameraUp * corner.y;

                o.pos = UnityWorldToClipPos(world);
                o.alpha = occupied == 1 ? point.w : 0.0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = _PointColor;
                color.a *= i.alpha;
                return color;
            }
            ENDCG
        }
    }
}
