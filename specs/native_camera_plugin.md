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

Current exported functions are:

```c
QrcCamera_Initialize(width, height, frameDirectory, formatInfoFilePath);
QrcCamera_SetSaveFrameRate(fps);
QrcCamera_Open(position);
QrcCamera_OpenById(cameraId);
QrcCamera_StartRecording();
QrcCamera_StopRecording();
QrcCamera_Close();
QrcCamera_GetStats(&stats);
QrcCamera_GetLastError();
QrcCamera_GetLastOpenedCameraId();
QrcCamera_GetCameraIdListJson();
QrcCamera_GetCameraMetadataJson(position);
```

Changes to exported signatures require Unity bridge review and compatibility review.

---

# Lifecycle

The intended lifecycle is:

```text
Initialize
    ->
SetSaveFrameRate
    ->
Open or OpenById
    ->
StartRecording
    ->
StopRecording
    ->
Close
```

`Close` should release native camera/session/image-reader resources and stop background writing.

Calling code should treat native result codes as authoritative operation results.

---

# Camera Selection

`QrcCamera_OpenById(cameraId)` is preferred when exact Quest passthrough camera IDs are known.

`QrcCamera_Open(position)` may be used only as a fallback unless stable left/right mapping is specified.

Left/right camera selection should be based on Meta camera metadata when available.

The native plugin owns native camera metadata lookup for the native recorder path. `QrcCamera_GetCameraMetadataJson(position)` should return a Unity-compatible `CameraMetadata` JSON object for the requested left/right position. It should read standard NDK camera characteristics directly and should resolve Quest vendor keys by name when the runtime exposes native vendor tag lookup. The Quest vendor keys are:

```text
com.meta.extra_metadata.position
com.meta.extra_metadata.camera_source
```

When named vendor tag lookup is not available at build/runtime, the native plugin may include raw vendor tag diagnostics and fall back to camera-id order for position selection, but this fallback must remain visible in metadata diagnostics.

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

When an operation fails, Unity should be able to retrieve a human-readable reason through `QrcCamera_GetLastError()`.

Failures must not be silently ignored by Unity integration code.

---

# Build Scope

The native plugin is built as an Android shared library for Meta Quest / Unity Android deployment.

The initial migration may keep the native implementation in a small number of files while compatibility is being validated.

Split the native implementation when responsibilities become difficult to navigate.
