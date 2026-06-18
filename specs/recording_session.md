# recording_session.md

## Purpose

This specification defines Unity-side recording session orchestration, output path ownership, and JSON-based recording configuration for RealityLog.

---

# Authority

This document is authoritative for Unity recording session lifecycle, session directory ownership, recording module configuration, and JSON configuration behavior.

Camera-specific Unity/native boundaries are defined in `unity_camera_architecture.md` and `native_camera_plugin.md`.

Recorded YUV compatibility is defined in `yuv_storage_format.md`.

The legacy downstream-facing output format is defined in `legacy_recording_format.md` and is used by the Camera2-compatible backend.

MRUK camera output is defined in `mruk_recording_format.md` and is a distinct downstream-facing camera format.

---

# Recording Session Ownership

Recording start and stop should be coordinated by a single scene-facing session controller.

Recommended component:

```text
RecordingSessionController
```

The session controller owns:

* loading recording configuration
* creating a new session root directory
* constructing full output paths
* applying configuration to camera, depth, and pose modules
* starting recording modules in a deterministic order
* stopping recording modules in reverse order

Unity UI should call the session controller for recording on/off behavior instead of directly enabling individual camera, depth, or pose components.

Legacy camera recording controller components may remain as scene compatibility adapters, but when a session controller is assigned they must forward recording on/off calls to the session controller and must not override session-owned paths.

---

# Path Ownership

`Application.persistentDataPath` must be treated as a session path provider dependency, not as a dependency of individual recording modules.

Recommended component:

```text
RecordingSessionPathProvider
```

The path provider owns session root creation and output path construction.

Recording modules should receive full paths from the session controller or session paths object.

Legacy modules may keep directory-name setters temporarily for scene compatibility, but new native recording flow should not distribute session directory names through UnityEvent.

---

# Output Layout Compatibility

The session root must include a backend-identifying metadata file:

```text
<session>/
    session_info.json
```

`session_info.json` must describe the realized recording output, not merely the requested configuration. The minimum required fields are:

```json
{
  "sessionFormatVersion": 2,
  "captureBackend": "MRUK"
}
```

`captureBackend` must be one of:

```text
MRUK
NativeCamera2
```

`appVersion` must not be required for postprocess compatibility decisions. Downstream tools should branch on `sessionFormatVersion` and `captureBackend`.

The Camera2-compatible output layout must preserve the original QuestRealityCapture layout documented in `legacy_recording_format.md`:

```text
<session>/
    hmd_poses.csv
    left_controller_poses.csv
    right_controller_poses.csv

    left_camera_raw/
    right_camera_raw/

    left_camera_image_format.json
    right_camera_image_format.json

    left_camera_characteristics.json
    right_camera_characteristics.json

    left_depth/
    right_depth/

    left_depth_descriptors.csv
    right_depth_descriptors.csv
```

Changing Camera2-compatible directory names, file names, CSV columns, JSON keys, raw depth byte layout, or YUV byte layout requires downstream compatibility review.

The MRUK camera backend must use the separate output layout defined in `mruk_recording_format.md` and must not write RGBA frames into legacy YUV paths.

---

# Recording Lifecycle

The default start sequence is:

```text
load config
create session paths
apply config to modules
start configured camera backend recorders
start depth exporter
start optional live feedback overlays
start pose loggers
```

The default stop sequence is:

```text
stop pose loggers
stop optional live feedback overlays
stop depth exporter
stop configured camera backend recorders
close cameras when configured
```

If any enabled recording module fails to start, the session controller must stop already-started modules and close any camera recorder whose native lifecycle may have been partially opened.

When camera recording is enabled with `camera.backend = "NativeCamera2"` in non-stereo mode, each enabled camera side in the active configuration must have a matching native Camera2 recorder assigned before the session starts.

When `camera.backend = "NativeCamera2"` and `camera.stereoMode` is enabled with both camera sides enabled, the session controller must start a native stereo Camera2 recorder instead of the independent left/right recorders. If no stereo recorder is assigned or discoverable, startup must fail with a clear error rather than silently falling back to independent recording.

When `camera.backend = "MRUK"`, the session controller must route camera recording through MRUK recorder components and the MRUK output layout. MRUK recording must not use legacy Camera2 YUV paths unless the implementation intentionally produces valid legacy-compatible YUV output and the relevant specifications are updated.

Depth and pose modules must report start success or failure to the session controller so that session startup does not silently continue after output initialization fails.

Depth frame acquisition should be isolated from depth persistence so that recording diagnostics or live feedback can share the current GPU depth texture without adding another GPU-to-CPU readback path. A depth exporter may depend on a scene-facing depth frame provider, but the exporter remains the owner of raw depth file output and descriptor CSV output.

Live feedback overlays, including coverage and diagnostics, are optional operator aids. Startup failures in these overlays should be logged as warnings and must not fail or stop the recording session.

The session controller should expose a simple scene-facing method:

```text
SetRecordingEnabled(bool enabled)
```

Scene-facing recording toggles should support a configurable cooldown so that rapid repeated UI input cannot immediately start and stop native recording sessions. The cooldown applies to accepted state changes through `SetRecordingEnabled(bool enabled)`. Repeating the already-active state is treated as a no-op and should not consume the cooldown.

---

# JSON Configuration

Recording configuration may be loaded from an external JSON file path or a Unity `TextAsset`. External JSON has priority over `TextAsset` configuration so that device-local configuration can override packaged defaults.

Relative external paths are resolved under `Application.persistentDataPath`. The default external configuration path is:

```text
recording_config.json
```

On Android devices, this allows configuration override through adb by placing `recording_config.json` in the app persistent files directory. The exact package-specific absolute path is determined by Unity at runtime through `Application.persistentDataPath`.

A missing external configuration file must not be treated as a startup error. Missing configuration should fall back to the assigned `TextAsset`, then to built-in defaults.

The built-in default camera backend is MRUK. Camera2-compatible output remains available by explicitly setting `camera.backend` to `NativeCamera2`.

Default values are authoritative. Camera2-compatible output values must match `legacy_recording_format.md`; MRUK defaults intentionally select the MRUK backend and MRUK camera output layout:

```text
sessionNameFormat = yyyyMMdd_HHmmss
camera.enabled = true
camera.backend = MRUK
camera.targetSaveFps = 10
camera.preferOpenByCameraId = true
camera.allowJavaMetadataFallback = false
camera.stereoMode = false
camera.stereoMaxTimeDeltaSeconds = 0.02
camera.stereoPairFileName = stereo_pairs.csv
camera.mrukStereoPairFileName = mruk_stereo_pairs.csv
camera.left.enabled = true
camera.left.imageDirectoryName = left_camera_raw
camera.left.metadataFileName = left_camera_characteristics.json
camera.left.formatInfoFileName = left_camera_image_format.json
camera.left.mrukImageDirectoryName = left_camera_mruk_rgba
camera.left.mrukIntrinsicsFileName = left_camera_mruk_intrinsics.json
camera.left.mrukFrameMetadataFileName = left_camera_mruk_frame_metadata.csv
camera.right.enabled = true
camera.right.imageDirectoryName = right_camera_raw
camera.right.metadataFileName = right_camera_characteristics.json
camera.right.formatInfoFileName = right_camera_image_format.json
camera.right.mrukImageDirectoryName = right_camera_mruk_rgba
camera.right.mrukIntrinsicsFileName = right_camera_mruk_intrinsics.json
camera.right.mrukFrameMetadataFileName = right_camera_mruk_frame_metadata.csv
depth.enabled = true
depth.targetSaveFps = 10
depth.leftDirectoryName = left_depth
depth.rightDirectoryName = right_depth
depth.leftDescriptorFileName = left_depth_descriptors.csv
depth.rightDescriptorFileName = right_depth_descriptors.csv
pose.enabled = true
pose.targetSaveFps = 30
pose.hmdFileName = hmd_poses.csv
pose.leftControllerFileName = left_controller_poses.csv
pose.rightControllerFileName = right_controller_poses.csv
liveFeedback.enabled = true
liveFeedback.coverage.enabled = true
liveFeedback.coverage.targetUpdateFps = 3
liveFeedback.coverage.samplingStep = 24
liveFeedback.coverage.voxelSizeMeters = 0.15
liveFeedback.coverage.maxVoxels = 30000
liveFeedback.coverage.minDepthMeters = 0.3
liveFeedback.coverage.maxDepthMeters = 5.0
liveFeedback.coverage.eye = left
liveFeedback.coverage.showSampleFrustums = false
liveFeedback.coverage.frustumSampleIntervalSeconds = 1.0
liveFeedback.coverage.maxFrustumSamples = 24
liveFeedback.coverage.logPoseDiagnostics = false
liveFeedback.coverage.poseDiagnosticIntervalSeconds = 1.0
liveFeedback.coverage.flipVerticalProjection = true
liveFeedback.diagnostics.enabled = true
liveFeedback.diagnostics.showHud = false
liveFeedback.diagnostics.showTrajectory = true
liveFeedback.diagnostics.showTrackingEvents = true
liveFeedback.diagnostics.positionJumpMeters = 0.3
liveFeedback.diagnostics.rotationJumpDegrees = 30.0
```

Packaged example/default JSON should remain available at:

```text
Assets/RealityLog/Configs/recording_config.default.json
```

The default JSON file is intended as the source template for adb overrides.

Supported configuration fields include:

```json
{
  "sessionNameFormat": "yyyyMMdd_HHmmss",
  "camera": {
    "enabled": true,
    "backend": "MRUK",
    "targetSaveFps": 10,
    "preferOpenByCameraId": true,
    "allowJavaMetadataFallback": false,
    "stereoMode": false,
    "stereoMaxTimeDeltaSeconds": 0.02,
    "stereoPairFileName": "stereo_pairs.csv",
    "mrukStereoPairFileName": "mruk_stereo_pairs.csv",
    "left": {
      "enabled": true,
      "imageDirectoryName": "left_camera_raw",
      "metadataFileName": "left_camera_characteristics.json",
      "formatInfoFileName": "left_camera_image_format.json",
      "mrukImageDirectoryName": "left_camera_mruk_rgba",
      "mrukIntrinsicsFileName": "left_camera_mruk_intrinsics.json",
      "mrukFrameMetadataFileName": "left_camera_mruk_frame_metadata.csv"
    },
    "right": {
      "enabled": true,
      "imageDirectoryName": "right_camera_raw",
      "metadataFileName": "right_camera_characteristics.json",
      "formatInfoFileName": "right_camera_image_format.json",
      "mrukImageDirectoryName": "right_camera_mruk_rgba",
      "mrukIntrinsicsFileName": "right_camera_mruk_intrinsics.json",
      "mrukFrameMetadataFileName": "right_camera_mruk_frame_metadata.csv"
    }
  },
  "depth": {
    "enabled": true,
    "targetSaveFps": 10,
    "leftDirectoryName": "left_depth",
    "rightDirectoryName": "right_depth",
    "leftDescriptorFileName": "left_depth_descriptors.csv",
    "rightDescriptorFileName": "right_depth_descriptors.csv"
  },
  "pose": {
    "enabled": true,
    "targetSaveFps": 30,
    "hmdFileName": "hmd_poses.csv",
    "leftControllerFileName": "left_controller_poses.csv",
    "rightControllerFileName": "right_controller_poses.csv"
  },
  "liveFeedback": {
    "enabled": true,
    "coverage": {
      "enabled": true,
      "targetUpdateFps": 3,
      "samplingStep": 24,
      "voxelSizeMeters": 0.15,
      "maxVoxels": 30000,
      "minDepthMeters": 0.3,
      "maxDepthMeters": 5.0,
      "eye": "left",
      "showSampleFrustums": false,
      "frustumSampleIntervalSeconds": 1.0,
      "maxFrustumSamples": 24,
      "logPoseDiagnostics": false,
      "poseDiagnosticIntervalSeconds": 1.0,
      "flipVerticalProjection": true
    },
    "diagnostics": {
      "enabled": true,
      "showHud": false,
      "showTrajectory": true,
      "showTrackingEvents": true,
      "positionJumpMeters": 0.3,
      "rotationJumpDegrees": 30.0
    }
  }
}
```


Live feedback configuration is defined in `live_recording_feedback.md`. The recording session configuration owns deserialization and defaulting of the `liveFeedback` block, but live feedback remains optional and must not change the recording output layout.

`targetSaveFps` values should be interpreted as save-rate throttles. They should not imply that the underlying camera, depth, or tracking systems must change their capture/update rate. In native stereo mode, `camera.targetSaveFps` is applied after timestamp pairing and limits saved stereo pairs.

A `targetSaveFps` value of zero means that every eligible update may be saved.

Negative FPS values are invalid and should be avoided by configuration authors.

---

# Live Coverage Session Hook

Recording session orchestration may start optional live coverage feedback after depth export startup and before pose logging startup.

Live coverage feedback is not a persistence module. Failure to start live coverage must not fail the recording session, and live coverage must not change session directory creation or recorded file layouts.

On stop, live coverage feedback should be stopped before depth export so shared depth provider usage is released before the depth exporter is stopped.

---

# Native Stereo Camera Session Configuration

Recording configuration may enable native stereo camera recording with:

```json
{
  "camera": {
    "stereoMode": true,
    "stereoMaxTimeDeltaSeconds": 0.02,
    "stereoPairFileName": "stereo_pairs.csv"
  }
}
```

When `camera.stereoMode` is true and both left and right camera sides are enabled, Unity routes camera recording through a single native stereo recorder component instead of two independent native camera recorder components.

`camera.targetSaveFps` applies to saved stereo pairs in stereo mode. `camera.stereoMaxTimeDeltaSeconds` defines the maximum allowed native timestamp difference between paired left and right frames. `camera.stereoPairFileName` names the CSV written at the session root that associates left and right `.yuv` files.

When `camera.backend` is `NativeCamera2` and stereo mode is false, existing independent left/right native camera recorder behavior is preserved. The packaged default configuration keeps stereo mode disabled; Camera2 workflows remain opt-in through `camera.backend = "NativeCamera2"`.


---

# Camera Backend Selection

Camera recording is selected by `camera.backend`.

Supported values are:

```text
MRUK
NativeCamera2
```

`MRUK` is the default backend for new configuration because it provides frame-timestamped camera pose through the MRUK camera API. Missing `camera.backend` values in older configuration files should be normalized to `MRUK` unless a compatibility migration explicitly chooses otherwise.

`NativeCamera2` preserves the existing native Camera2 recorder behavior and writes legacy-compatible YUV output as defined in `legacy_recording_format.md` and `yuv_storage_format.md`.

`MRUK` writes the MRUK-specific output layout defined in `mruk_recording_format.md`. The existing `camera.left.imageDirectoryName`, `camera.left.metadataFileName`, `camera.left.formatInfoFileName`, and matching right-side fields remain Camera2-compatible fields and must not be reused for MRUK RGBA output.

The session controller must write `session_info.json` after backend normalization so postprocess sees the realized backend:

```json
{
  "sessionFormatVersion": 2,
  "captureBackend": "MRUK"
}
```

The requested backend in configuration and the realized backend in `session_info.json` should match. If startup falls back to another backend, the fallback must be explicit, logged, and reflected in `session_info.json`.
