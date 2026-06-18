# recording_session.md

## Purpose

This specification defines Unity-side recording session orchestration, output path ownership, and JSON-based recording configuration for RealityLog.

---

# Authority

This document is authoritative for Unity recording session lifecycle, session directory ownership, recording module configuration, and JSON configuration behavior.

Camera-specific Unity/native boundaries are defined in `unity_camera_architecture.md` and `native_camera_plugin.md`.

Recorded YUV compatibility is defined in `yuv_storage_format.md`.

The legacy downstream-facing output format is defined in `legacy_recording_format.md` and is the default recording layout unless a user configuration explicitly overrides it.

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

The default output layout must preserve the original QuestRealityCapture layout documented in `legacy_recording_format.md`:

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

Changing default directory names, file names, CSV columns, JSON keys, raw depth byte layout, or YUV byte layout requires downstream compatibility review.

---

# Recording Lifecycle

The default start sequence is:

```text
load config
create session paths
apply config to modules
start camera recorders
start depth exporter
start pose loggers
```

The default stop sequence is:

```text
stop pose loggers
stop depth exporter
stop camera recorders
close cameras when configured
```

If any enabled recording module fails to start, the session controller must stop already-started modules and close any camera recorder whose native lifecycle may have been partially opened.

When camera recording is enabled, each enabled camera side in the active configuration must have a matching native camera recorder assigned before the session starts.

Depth and pose modules must report start success or failure to the session controller so that session startup does not silently continue after output initialization fails.

Depth frame acquisition should be isolated from depth persistence so that recording diagnostics or live feedback can share the current GPU depth texture without adding another GPU-to-CPU readback path. A depth exporter may depend on a scene-facing depth frame provider, but the exporter remains the owner of raw depth file output and descriptor CSV output.

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

A missing external configuration file must not be treated as a startup error. Missing configuration should fall back to the assigned `TextAsset`, then to defaults that preserve the legacy output layout.

Default values are authoritative and must match `legacy_recording_format.md` unless explicitly changed through compatibility review:

```text
sessionNameFormat = yyyyMMdd_HHmmss
camera.enabled = true
camera.targetSaveFps = 10
camera.preferOpenByCameraId = true
camera.allowJavaMetadataFallback = false
camera.left.enabled = true
camera.left.imageDirectoryName = left_camera_raw
camera.left.metadataFileName = left_camera_characteristics.json
camera.left.formatInfoFileName = left_camera_image_format.json
camera.right.enabled = true
camera.right.imageDirectoryName = right_camera_raw
camera.right.metadataFileName = right_camera_characteristics.json
camera.right.formatInfoFileName = right_camera_image_format.json
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
liveFeedback.enabled = false
liveFeedback.coverage.enabled = true
liveFeedback.coverage.targetUpdateFps = 3
liveFeedback.coverage.samplingStep = 24
liveFeedback.coverage.voxelSizeMeters = 0.15
liveFeedback.coverage.maxVoxels = 30000
liveFeedback.coverage.minDepthMeters = 0.3
liveFeedback.coverage.maxDepthMeters = 5.0
liveFeedback.coverage.eye = left
liveFeedback.diagnostics.enabled = true
liveFeedback.diagnostics.showHud = true
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
    "targetSaveFps": 10,
    "preferOpenByCameraId": true,
    "allowJavaMetadataFallback": false,
    "left": {
      "enabled": true,
      "imageDirectoryName": "left_camera_raw",
      "metadataFileName": "left_camera_characteristics.json",
      "formatInfoFileName": "left_camera_image_format.json"
    },
    "right": {
      "enabled": true,
      "imageDirectoryName": "right_camera_raw",
      "metadataFileName": "right_camera_characteristics.json",
      "formatInfoFileName": "right_camera_image_format.json"
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
    "enabled": false,
    "coverage": {
      "enabled": true,
      "targetUpdateFps": 3,
      "samplingStep": 24,
      "voxelSizeMeters": 0.15,
      "maxVoxels": 30000,
      "minDepthMeters": 0.3,
      "maxDepthMeters": 5.0,
      "eye": "left"
    },
    "diagnostics": {
      "enabled": true,
      "showHud": true,
      "showTrajectory": true,
      "showTrackingEvents": true,
      "positionJumpMeters": 0.3,
      "rotationJumpDegrees": 30.0
    }
  }
}
```


Live feedback configuration is defined in `live_recording_feedback.md`. The recording session configuration owns deserialization and defaulting of the `liveFeedback` block, but live feedback remains optional and must not change the recording output layout.

`targetSaveFps` values should be interpreted as save-rate throttles. They should not imply that the underlying camera, depth, or tracking systems must change their capture/update rate.

A `targetSaveFps` value of zero means that every eligible update may be saved.

Negative FPS values are invalid and should be avoided by configuration authors.
