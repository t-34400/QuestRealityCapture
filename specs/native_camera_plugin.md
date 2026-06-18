# native_camera_plugin.md

## Purpose

This specification defines the Android NDK native camera plugin responsibilities and Unity-facing C API expectations.

---

# Authority

This document is authoritative for native plugin lifecycle, exported API behavior, native recording responsibilities, and native integration assumptions.

YUV byte layout requirements are defined in `yuv_storage_format.md`.

---

# Native Plugin Scope

The native plugin is responsible for:

* opening Android camera devices
* acquiring frames through Android NDK camera APIs
* writing captured frames to storage
* recording format metadata
* throttling saved frames to the configured target save FPS
* reporting native errors through result codes and last-error text

The native plugin is not responsible for:

* Unity UI
* Android runtime permission requests
* user-facing recording orchestration
* YUV-to-RGB conversion
* JPEG generation
* downstream processing

---

# Permission Assumption

The native plugin must assume required permissions have already been granted before initialization or camera opening.

Permission acquisition belongs to Unity.

Required permissions include:

```text
CAMERA
horizonos.permission.HEADSET_CAMERA
```

The native plugin may report permission-related failures, but must not request permissions itself.

---

# Exported C API

The native plugin exposes a C ABI for Unity interop.

The exported functions are session-handle based:

```c
QrcCamera_CreateSession(&handle);
QrcCamera_DestroySession(handle);
QrcCamera_InitializeSession(handle, width, height, frameDirectory, formatInfoFilePath);
QrcCamera_SetSessionSaveFrameRate(handle, fps);
QrcCamera_OpenSession(handle, position);
QrcCamera_OpenSessionById(handle, cameraId);
QrcCamera_StartSessionRecording(handle);
QrcCamera_StopSessionRecording(handle);
QrcCamera_CloseSession(handle);
QrcCamera_GetSessionStats(handle, &stats);
QrcCamera_GetSessionLastError(handle);
QrcCamera_GetSessionLastOpenedCameraId(handle);
QrcCamera_GetCameraIdListJson();
QrcCamera_GetCameraMetadataJson(position);
```

Every concurrently recorded camera must use its own `QrcCameraSessionHandle`. The native recording path must not share a singleton camera session between left and right camera recorders. Destroying a session releases all native resources owned by that recorder.

Changes to exported signatures require Unity bridge review and compatibility review.

---

# Lifecycle

The intended lifecycle for each camera is:

```text
CreateSession
    ->
InitializeSession
    ->
SetSessionSaveFrameRate
    ->
OpenSession or OpenSessionById
    ->
StartSessionRecording
    ->
StopSessionRecording
    ->
CloseSession
    ->
DestroySession
```

`CloseSession` should release native camera/device/image-reader resources and stop background writing. `DestroySession` should release the session object itself.

Calling code should treat native result codes as authoritative operation results.

---

# Camera Selection

`QrcCamera_OpenSessionById(handle, cameraId)` is preferred when exact Quest passthrough camera IDs are known.

`QrcCamera_OpenSession(handle, position)` may be used only as a fallback unless stable left/right mapping is specified.

Left/right camera selection should be based on Meta camera metadata when available.

The native plugin owns native camera metadata lookup for the native recorder path. `QrcCamera_GetCameraMetadataJson(position)` should return a Unity-compatible `CameraMetadata` JSON object for the requested left/right position. It should read standard NDK camera characteristics directly and should resolve Quest vendor keys by descriptor first, then by name when the runtime exposes native vendor tag lookup. The Quest vendor keys are:

```text
com.meta.extra_metadata.camera_source = 0x80004d00
com.meta.extra_metadata.position = 0x80004d01
```

Native left/right selection must use passthrough camera metadata:

```text
camera_source == 0
position == 0 for left
position == 1 for right
```

The native recorder path must not silently fall back to camera-id order for passthrough camera selection. If the Quest passthrough vendor metadata cannot be resolved for the requested side, native metadata lookup and `QrcCamera_OpenSession(position)` should fail with diagnostics rather than selecting a non-passthrough camera.

---

# Frame Persistence

The native plugin must persist YUV frames according to `yuv_storage_format.md`.

The native plugin must not introduce image conversion, stride removal, padding removal, or plane reordering.

---

# Save FPS Throttling

The native plugin should not change camera capture FPS to satisfy recording FPS requirements.

Target recording FPS is implemented by dropping frames at save time.

When `targetSaveFps` is greater than zero, the plugin should save a frame only when enough timestamp interval has elapsed since the last saved frame.

When `targetSaveFps` is zero, all received frames are eligible for saving.

Negative FPS values are invalid.

---

# Statistics

The native plugin should expose recording statistics sufficient for Unity-side diagnostics.

Current stats include:

* received frame count
* saved frame count
* dropped frame count
* I/O error count
* last image timestamp
* last saved timestamp

Stats should be treated as diagnostic information, not as the primary recording data source.

---

# Error Reporting

Native functions return `QrcCameraResult` values.

When an operation fails, Unity should be able to retrieve a human-readable reason through `QrcCamera_GetSessionLastError(handle)`.

Failures must not be silently ignored by Unity integration code.

---

# Build Scope

The native plugin is built as an Android shared library for Meta Quest / Unity Android deployment.

The initial migration may keep the native implementation in a small number of files while compatibility is being validated.

Split the native implementation when responsibilities become difficult to navigate.

---

# Native Stereo Recording Mode

The native plugin may expose a stereo session API in addition to the existing single-camera session API.

The stereo API must keep the single-camera API behavior unchanged. Existing `QrcCamera_*` exported functions remain the compatibility path for independent left/right camera recorders.

The stereo API owns both the left and right native camera devices in one session and must match frames by native image timestamp before persisting them. A stereo frame pair is eligible for saving only when the absolute timestamp difference is less than or equal to the configured maximum delta.

Stereo sessions must preserve raw YUV persistence rules from `yuv_storage_format.md` for each saved left and right frame. Stereo mode must not convert, repack, reorder, or crop image planes.

The stereo API should write a pair index file named by Unity integration, conventionally:

```text
stereo_pairs.csv
```

The CSV must include enough information to associate saved left/right `.yuv` files with their native timestamps and timestamp delta. The expected columns are:

```text
pair_index,left_timestamp_ns,right_timestamp_ns,delta_ns,left_unix_ms,right_unix_ms,left_file,right_file
```

Stereo target save FPS is applied to accepted pairs, not independently to each camera stream.
