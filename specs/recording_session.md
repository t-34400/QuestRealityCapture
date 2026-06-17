# recording_session.md

## Purpose

This specification defines Unity-side recording session orchestration, output path ownership, and JSON-based recording configuration for RealityLog.

---

# Authority

This document is authoritative for Unity recording session lifecycle, session directory ownership, recording module configuration, and JSON configuration behavior.

Camera-specific Unity/native boundaries are defined in `unity_camera_architecture.md` and `native_camera_plugin.md`.

Recorded YUV compatibility is defined in `yuv_storage_format.md`.

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

The default output layout remains:

```text
<session>/
    left_camera/
    right_camera/
    left_camera_characteristics.json
    right_camera_characteristics.json
    left_camera_image_format.json
    right_camera_image_format.json
    left_depth/
    right_depth/
    left_depth_descriptors.csv
    right_depth_descriptors.csv
    poses.csv
```

Changing default directory names or file names requires downstream compatibility review.

---

# Recording Lifecycle

The default start sequence is:

```text
load config
create session paths
apply config to modules
start camera recorders
start depth exporter
start pose logger
```

The default stop sequence is:

```text
stop pose logger
stop depth exporter
stop camera recorders
close cameras when configured
```

If any enabled recording module fails to start, the session controller must stop already-started modules and close any camera recorder whose native lifecycle may have been partially opened.

When camera recording is enabled, each enabled camera side in the active configuration must have a matching native camera recorder assigned before the session starts.

Depth and pose modules must report start success or failure to the session controller so that session startup does not silently continue after output initialization fails.

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

A missing external configuration file must not be treated as a startup error. Missing configuration should fall back to the assigned `TextAsset`, then to defaults that preserve the existing output layout.

Default values are authoritative and must preserve the existing output layout:

```text
sessionNameFormat = yyyyMMdd_HHmmss
camera.enabled = true
camera.targetSaveFps = 10
camera.preferOpenByCameraId = true
camera.allowJavaMetadataFallback = false
camera.left.enabled = true
camera.left.imageDirectoryName = left_camera
camera.left.metadataFileName = left_camera_characteristics.json
camera.left.formatInfoFileName = left_camera_image_format.json
camera.right.enabled = true
camera.right.imageDirectoryName = right_camera
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
pose.fileName = poses.csv
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
      "imageDirectoryName": "left_camera",
      "metadataFileName": "left_camera_characteristics.json",
      "formatInfoFileName": "left_camera_image_format.json"
    },
    "right": {
      "enabled": true,
      "imageDirectoryName": "right_camera",
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
    "fileName": "poses.csv"
  }
}
```

`targetSaveFps` values should be interpreted as save-rate throttles. They should not imply that the underlying camera, depth, or tracking systems must change their capture/update rate.

A `targetSaveFps` value of zero means that every eligible update may be saved.

Negative FPS values are invalid and should be avoided by configuration authors.
