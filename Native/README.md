# qrc_camera_native

NDK Camera2 native plugin template for Unity Android and Meta Quest.

## Design Principles

- No runtime permission requests are performed. Unity is expected to obtain `CAMERA` and `horizonos.permission.HEADSET_CAMERA` beforehand.
- No JPEG output or YUV-to-RGB conversion is performed.
- Each `AImage` plane is read using `AImage_getPlaneData()` and written exactly as received. Plane 0, 1, and 2 are concatenated and stored as a `.yuv` file to preserve compatibility with the Kotlin implementation.
- Camera FPS is not constrained. Frame skipping is applied only when saving frames.

## Generated Files

```text
include/qrc_camera_native.h
src/qrc_camera_native.cpp
CMakeLists.txt
```

## C API

```c
QrcCamera_CreateSession(&handle);
QrcCamera_InitializeSession(handle, width, height, frameDirectory, formatInfoFilePath);
QrcCamera_SetSessionSaveFrameRate(handle, fps);
QrcCamera_OpenSession(handle, position);
QrcCamera_OpenSessionById(handle, cameraId);
QrcCamera_StartSessionRecording(handle);
QrcCamera_StopSessionRecording(handle);
QrcCamera_CloseSession(handle);
QrcCamera_GetSessionStats(handle, &stats);
QrcCamera_GetSessionLastError(handle);
QrcCamera_DestroySession(handle);
QrcCamera_GetCameraIdListJson();
QrcCamera_GetCameraMetadataJson(position);
```

## Storage Format

Each frame is stored by concatenating buffers in the following order:

```text
plane[0].buffer
plane[1].buffer
plane[2].buffer
```

No I420/NV12/NV21 conversion, stride removal, padding removal, or plane reordering is performed.

File naming follows the same convention as the Kotlin implementation.

```text
<computed_unix_time_ms>.yuv
```

Timestamp conversion:

```text
unix_ms = baseUnixTimeMs + (imageTimestampNs - baseMonoTimeNs) / 1_000_000
```

## formatInfo.json

The first saved frame generates metadata similar to:

```json
{
  "width": 1280,
  "height": 960,
  "format": "YUV_420_888",
  "planes": [
    { "rowStride": 1280, "pixelStride": 1, "bufferSize": 1228800 }
  ],
  "baseTime": {
    "baseMonoTimeNs": 123,
    "baseUnixTimeMs": 456
  }
}
```

## Native Metadata

`QrcCamera_GetCameraMetadataJson(position)` returns standard NDK Camera2 characteristics in a JSON format compatible with Unity `CameraMetadata`.

When `ACameraMetadata_getTagFromName()` is available, the plugin resolves the following Meta Quest vendor metadata keys:

```text
com.meta.extra_metadata.position
com.meta.extra_metadata.camera_source
```

If vendor key resolution is unavailable, `nativeMetadata.questVendorKeysResolved=false` is reported and raw vendor tags are exported. In that case, left/right camera selection falls back to camera ID ordering and should be verified on the target device.
