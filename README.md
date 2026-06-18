# QuestRealityCapture

<p align="center">
  <img src="docs/overview.png" alt="QuestRealityCapture" width="320"/>
</p>

**Capture and store real-world data on Meta Quest 3 or 3s, including HMD/controller poses, MRUK or Camera2 passthrough images, camera metadata, and depth maps.**

---

## 📖 Overview

`QuestRealityCapture` is a Unity-based data logging app for Meta Quest 3. It captures and stores synchronized real-world information such as headset and controller poses, images from both passthrough cameras, camera characteristics, and depth data, organized per session.

For **data parsing, visualization, and reconstruction**, refer to the companion project:
**[Meta Quest 3D Reconstruction](https://github.com/t-34400/metaquest-3d-reconstrucion)**

This includes:

* Scripts for **loading and decoding** camera poses, intrinsics, and depth descriptors
* Conversions of **raw YUV images** and **depth maps** to usable formats (RGB, point clouds)
* Utilities for **reconstructing 3D scenes** using [Open3D](http://www.open3d.org/)
* Export pipelines to prepare data for **SfM/SLAM tools** like **COLMAP**

---

## ✅ Features

* Records HMD and controller poses (in Unity coordinate system)
* Captures passthrough images from both left and right cameras through the default **MRUK** backend or the legacy **NativeCamera2** backend
* Stores MRUK frames as raw RGBA32 with frame-timestamped MRUK camera poses
* Preserves the legacy Camera2-compatible raw YUV output path when `camera.backend` is set to `NativeCamera2`
* Logs backend-specific camera intrinsics and image format information
* Saves **depth maps** and **depth descriptors** from both cameras
* Provides optional live recording feedback for depth coverage and diagnostics
* Supports JSON-based runtime configuration for camera, depth, pose, and live feedback settings
* Automatically organizes logs into timestamped folders on internal storage

---

## 📢 NOTICE (v1.1.0)

Starting with version **1.1.0**, the **camera pose values** stored in `left_camera_characteristics.json` and `right_camera_characteristics.json` are now saved as **raw pose values directly obtained from the Android Camera2 API**.

### Migration Guide for Older Logs (v1.0.x and earlier)

In versions **prior to 1.1.0**, the camera poses were preprocessed into Unity coordinate space. To convert these older poses to match the new raw format convention, apply the following transformation:

* **Translation (position)**:

  ```
  (x, y, z) → (x, y, -z)
  ```

* **Rotation (quaternion)**:

  ```
  (x, y, z, w) → (-x, -y, z, w)
  ```

This conversion aligns the preprocessed Unity pose with the raw Android pose representation now used in version 1.1.0 and later.

---

## 🧾 Data Structure

Each time you start recording, a new folder is created under:

```
/sdcard/Android/data/com.t34400.QuestRealityCapture/files
```

Every session contains `session_info.json`. Downstream tools should read this file first and branch on `captureBackend`.

```json
{
  "sessionFormatVersion": 2,
  "captureBackend": "MRUK"
}
```

### MRUK backend layout (default)

The default backend is `MRUK`. It records MRUK `PassthroughCameraAccess` frames as raw RGBA32 files and writes MRUK camera pose/intrinsics metadata separately from the legacy Camera2 files.

```
/sdcard/Android/data/com.t34400.QuestRealityCapture/files
└── YYYYMMDD_hhmmss/
    ├── session_info.json
    ├── hmd_poses.csv
    ├── left_controller_poses.csv
    ├── right_controller_poses.csv
    │
    ├── left_camera_mruk_rgba/
    │   ├── <timestamp_us_realtime>.rgba
    │   └── ...
    ├── right_camera_mruk_rgba/
    │   ├── <timestamp_us_realtime>.rgba
    │   └── ...
    │
    ├── left_camera_mruk_intrinsics.json
    ├── right_camera_mruk_intrinsics.json
    ├── left_camera_mruk_frame_metadata.csv
    ├── right_camera_mruk_frame_metadata.csv
    ├── mruk_stereo_pairs.csv
    │
    ├── left_depth/
    │   ├── <unixtimeMs>.raw
    │   └── ...
    ├── right_depth/
    │   ├── <unixtimeMs>.raw
    │   └── ...
    │
    ├── left_depth_descriptors.csv
    └── right_depth_descriptors.csv
```

### NativeCamera2 backend layout

Set `camera.backend` to `NativeCamera2` to use the legacy Camera2-compatible output layout. This backend preserves the raw `YUV_420_888` plane byte layout consumed by existing downstream tools.

```
/sdcard/Android/data/com.t34400.QuestRealityCapture/files
└── YYYYMMDD_hhmmss/
    ├── session_info.json
    ├── hmd_poses.csv
    ├── left_controller_poses.csv
    ├── right_controller_poses.csv
    ├── stereo_pairs.csv              # present when native stereo mode is enabled
    │
    ├── left_camera_raw/
    │   ├── <unixtimeMs>.yuv
    │   └── ...
    ├── right_camera_raw/
    │   ├── <unixtimeMs>.yuv
    │   └── ...
    │
    ├── left_camera_image_format.json
    ├── right_camera_image_format.json
    ├── left_camera_characteristics.json
    ├── right_camera_characteristics.json
    │
    ├── left_depth/
    │   ├── <unixtimeMs>.raw
    │   └── ...
    ├── right_depth/
    │   ├── <unixtimeMs>.raw
    │   └── ...
    │
    ├── left_depth_descriptors.csv
    └── right_depth_descriptors.csv
```

---

## 📄 Data Format Details

### Pose CSV

* Files: `hmd_poses.csv`, `left_controller_poses.csv`, `right_controller_poses.csv`
* Format:

  ```
  unix_time,ovr_timestamp,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,rot_w
  ```

### Camera Backends

#### MRUK (default)

* Uses Meta XR MRUK `PassthroughCameraAccess`.
* Saves camera frames as raw `.rgba` files under `left_camera_mruk_rgba/` and `right_camera_mruk_rgba/`.
* Each `.rgba` file contains RGBA32 byte data. For 1280 x 1280 frames, the expected file size is `1280 * 1280 * 4 = 6553600` bytes.
* Frame file names use the MRUK Unix-epoch microsecond timestamp: `<timestamp_us_realtime>.rgba`.
* Intrinsics are written to `left_camera_mruk_intrinsics.json` and `right_camera_mruk_intrinsics.json`.
* Per-frame metadata is written to `left_camera_mruk_frame_metadata.csv` and `right_camera_mruk_frame_metadata.csv`. It includes the saved file name, timestamp, resolution, RGBA byte count, MRUK camera pose, and API success flags.
* Stereo associations are written to `mruk_stereo_pairs.csv`. Pairs are generated by nearest timestamp matching, not by assuming directory order or equal frame indices.

MRUK is preferred when frame-synchronized camera pose is more important than preserving the legacy Camera2 YUV byte format. The pose stored in the MRUK frame metadata comes from `PassthroughCameraAccess.GetCameraPose()` for the camera frame.

#### NativeCamera2

* Uses the Android Camera2 NDK backend.
* Saves raw `YUV_420_888` frames as `.yuv` files under `left_camera_raw/` and `right_camera_raw/`.
* Image format and buffer information are provided in the accompanying `*_camera_image_format.json` files.
* Camera2 characteristics are written to `left_camera_characteristics.json` and `right_camera_characteristics.json`.
* When native stereo mode is enabled, left/right frames are matched by native image timestamp before saving. The session root includes `stereo_pairs.csv` with:

  ```text
  pair_index,left_timestamp_ns,right_timestamp_ns,delta_ns,left_unix_ms,right_unix_ms,left_file,right_file
  ```

To convert passthrough YUV (YUV_420_888) images to RGB for visualization or reconstruction, see: [Meta Quest 3D Reconstruction](https://github.com/t-34400/metaquest-3d-reconstrucion)

### Depth Map Descriptor CSV

* Format:

  ```
  timestamp_ms,ovr_timestamp,create_pose_location_x, ..., create_pose_rotation_w,
  fov_left_angle_tangent,fov_right_angle_tangent,fov_top_angle_tangent,fov_down_angle_tangent,
  near_z,far_z,width,height
  ```

### Depth Map

* Raw `.float32` depth images (1D float per pixel)

To convert raw depth maps into linear or 3D form, refer to the companion project: [Meta Quest 3D Reconstruction](https://github.com/t-34400/metaquest-3d-reconstrucion)

---
### Live Feedback

Live feedback can display recording aids inside the headset while recording is active. It is intended for operator feedback only and does not change camera, depth, pose, or metadata output files.

Available live feedback features include:

* Depth coverage visualization
* Recording trajectory visualization
* Tracking discontinuity markers
* Optional operator HUD diagnostics

The bundled default configuration enables live coverage and diagnostics, but keeps the HUD text overlay disabled by default.

---

## ⚙️ Configuration

The application can be configured using a JSON configuration file.

Default configuration ([recording_config.default.json](Assets/RealityLog/Configs/recording_config.default.json)):

```text
Assets/RealityLog/Configs/recording_config.default.json
```

Runtime override:

```text
/sdcard/Android/data/com.t34400.QuestRealityCapture/files/recording_config.json
```

When `recording_config.json` is present, values in that file override the default configuration.

Minimal MRUK configuration:

```json
{
  "camera": {
    "enabled": true,
    "backend": "MRUK",
    "targetSaveFps": 10,
    "left": {
      "enabled": true
    },
    "right": {
      "enabled": true
    }
  },
  "pose": {
    "enabled": true,
    "targetSaveFps": 30
  },
  "depth": {
    "enabled": true,
    "targetSaveFps": 10
  }
}
```

Full default configuration excerpt:

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

`targetSaveFps` controls the save rate. It does not force the underlying camera, depth, or tracking system update rate. A value of `0` saves every eligible update.

Camera backend settings:

* `camera.backend`: selects the camera backend. Supported values are `MRUK` and `NativeCamera2`. The default is `MRUK`.
* `camera.targetSaveFps`: limits saved camera frames or stereo pairs. For MRUK, it limits saved timestamped left/right pairs. For NativeCamera2 stereo mode, it is applied after native timestamp pairing.
* `camera.mrukStereoPairFileName`: session-root CSV for MRUK nearest-timestamp stereo pairs.
* `camera.left.mrukImageDirectoryName` / `camera.right.mrukImageDirectoryName`: MRUK RGBA output directories.
* `camera.left.mrukIntrinsicsFileName` / `camera.right.mrukIntrinsicsFileName`: MRUK intrinsics JSON files.
* `camera.left.mrukFrameMetadataFileName` / `camera.right.mrukFrameMetadataFileName`: MRUK per-frame metadata CSV files.

NativeCamera2 stereo settings:

* `camera.stereoMode`: when `true` with `camera.backend` set to `NativeCamera2`, the Unity session routes left/right camera capture through the native stereo recorder.
* `camera.stereoMaxTimeDeltaSeconds`: maximum allowed timestamp difference for a saved Camera2 stereo pair.
* `camera.stereoPairFileName`: session-root CSV that maps saved left/right `.yuv` files to native timestamps.

Example deployment:

```bash
adb push recording_config.json /sdcard/Android/data/com.t34400.QuestRealityCapture/files/recording_config.json
```

---

## 🚀 Installation & Usage

1. Download the APK from [GitHub Releases](https://github.com/t-34400/QuestRealityCapture/releases)
2. Install with ADB:

   ```bash
   adb install QuestRealityCapture.apk
   ```
3. Launch the app on **Meta Quest 3 or 3s** (firmware **v74+** required)
4. When the green instruction panel appears, press the **menu button on the left controller** or use the configured left-hand menu gesture to dismiss it and start logging
5. Data will be saved under the session folder as described above

Required permissions (camera/scene access) are requested automatically at runtime.

---

## 🛠 Environment

* Unity **6000.4.5f1**
* Meta OpenXR SDK
* Device: Meta Quest 3 or 3s only
* Camera, depth, and pose save rates are configurable through `recording_config.json`

---

## 📝 License

This project is licensed under the **[MIT License](LICENSE)**.

This project uses Meta’s OpenXR SDK — please ensure compliance with its license when redistributing.

---

## Known Issues

* On some Quest runtime / Unity / Meta XR SDK combinations, exiting the application after MRUK camera use may trigger a native Unity crash during teardown. Recorded session files are written before this shutdown crash. This is tracked as an MRUK/Meta XR lifecycle compatibility TODO and does not change the on-disk MRUK format.

---

## 📌 TODO
