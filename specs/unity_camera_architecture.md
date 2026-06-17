# unity_camera_architecture.md

## Purpose

This specification defines Unity-side camera recording architecture for RealityLog.

It exists to keep Unity responsibilities separate from native camera recording responsibilities during the migration from Kotlin/Camera2 to Android NDK.

---

# Authority

This document is authoritative for Unity camera architecture, Unity/native responsibility boundaries, and Unity-side recorder lifecycle expectations.

Native plugin behavior is defined in `native_camera_plugin.md`.

Recorded YUV format behavior is defined in `yuv_storage_format.md`.

---

# Unity Responsibilities

Unity is responsible for:

* requesting Android camera permissions
* selecting or providing camera IDs
* selecting save directories
* passing recording configuration to the recorder implementation
* driving recorder lifecycle from scene/UI behavior
* surfacing native errors to developers or users
* preserving a clear boundary between legacy Java/Kotlin recording and native recording

Unity must not duplicate native frame persistence logic when using the native recorder.

---

# Native Bridge

Unity should call the native plugin through a focused bridge component.

The bridge should be responsible for C API declarations and low-level marshaling only.

Scene logic and UI should not call `DllImport` functions directly.

Recommended component:

```text
NativeCameraBridge
```

---

# Recorder Abstraction

Unity should expose recording operations through a small recorder-facing interface or equivalent focused component.

The scene-facing lifecycle should remain close to:

```text
Initialize
OpenCamera
StartRecording
StopRecording
Close
```

This mirrors the native plugin lifecycle and avoids coupling recording behavior to `MonoBehaviour` enable/disable events.

---

# Permission Service

Permission acquisition should be isolated from camera session and recorder implementation details.

A permission component may use Java/Kotlin support code when needed, but should not own recording lifecycle or frame persistence.

Recommended component:

```text
CameraPermissionService
```

---

# Camera Metadata

Unity may use Java/Kotlin support code to read Meta camera metadata, including camera source and camera position.

Metadata retrieval should be separate from permission acquisition when practical.

Recommended component:

```text
CameraMetadataProvider
```

Metadata-derived camera IDs should be passed to the native plugin through `QrcCamera_OpenById()` when available.

---

# Legacy Java/Kotlin Recorder

Existing Java/Kotlin Camera2 recording code should be treated as a legacy implementation during migration.

Legacy code may remain for comparison and fallback, but it should be separated from the native recorder path.

Recommended direction:

```text
ICameraRecorder
    -> JavaCameraRecorder or LegacyCameraRecorder
    -> NativeCameraRecorder
```

The exact names may differ, but the ownership boundary should remain explicit.

---

# Scene Coupling

Recording should not implicitly start only because a GameObject is enabled unless the scene component explicitly represents that policy.

Prefer explicit calls for:

* permission request completion
* camera open
* recording start
* recording stop
* camera close

This separation is required to support native lifecycle error handling and compatibility validation.

---

# Save Paths

Unity should decide the recording directory and metadata file paths before native initialization.

The native plugin should receive explicit paths and should not infer Unity project or scene state.

---

# Error Handling

Unity integration must inspect native result codes.

On native failure, Unity should retrieve `QrcCamera_GetLastError()` and report it through an appropriate Unity logging or UI path.

Silent native failures are not acceptable for recording operations.

---

# Initial Migration Strategy

The initial native integration should prioritize compatibility validation over a broad Unity refactor.

Recommended sequence:

1. Add a focused native bridge.
2. Add a native recorder wrapper.
3. Keep legacy Java/Kotlin recorder code available for comparison.
4. Route a minimal scene path through the native recorder.
5. Validate `.yuv` compatibility with downstream processing.
6. Refactor scene/UI organization after native compatibility is confirmed.
