# QuestEgoCapture

<p align="center">
  <img src="docs/overview.png" alt="QuestEgoCapture" width="320"/>
</p>

**Capture egocentric data for robot learning on Meta Quest 3 or 3s, including body/hand poses, HMD/controller poses, stereo passthrough images, camera metadata, and depth maps.**

> **Note:** This is a fork of [QuestRealityCapture](https://github.com/t-34400/QuestRealityCapture) extended for **human egocentric robot training data collection**. We added full body and hand skeleton tracking to capture human demonstrations for imitation learning and behavioral cloning.

---

## 📖 Overview

`QuestEgoCapture` is a Unity-based data logging app for Meta Quest 3. It captures and stores synchronized real-world information such as headset and controller poses, images from both passthrough cameras, camera characteristics, and depth data, organized per session.

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
* Captures **YUV passthrough images** from **both left and right cameras**
* Logs **Camera2 API characteristics** and image format information
* Saves **depth maps** and **depth descriptors** from both cameras
* Automatically organizes logs into timestamped folders on internal storage

### Added in this Fork

* **Full body skeleton tracking** (85 joints via `OVRBody`) - captures torso, arms, legs, and hands
* **Hand skeleton tracking** (24 bones per hand via `OVRSkeleton`) - world-space hand poses
* **Pinch/grab detection** - continuous pinch strength + binary grab flag per hand

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

Example structure:

```
/sdcard/Android/data/com.t34400.QuestRealityCapture/files
└── YYYYMMDD_hhmmss/
    ├── hmd_poses.csv
    ├── left_controller_poses.csv
    ├── right_controller_poses.csv
    │
    ├── body_pose.csv              # [NEW] Full body skeleton (85 joints)
    ├── left_hand_pose.csv         # [NEW] Left hand skeleton + pinch/grab
    ├── right_hand_pose.csv        # [NEW] Right hand skeleton + pinch/grab
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

### Body Pose CSV (New)

* File: `body_pose.csv`
* Logs 85 body joints from `OVRBody` at each frame
* Format:

  ```
  UnixTime,UnityTime,BodyJoint0_Px,BodyJoint0_Py,BodyJoint0_Pz,BodyJoint0_Rx,BodyJoint0_Ry,BodyJoint0_Rz,BodyJoint0_Rw,...,BodyJoint84_Rw
  ```

* Joint indices include: root, spine, chest, neck, head, shoulders, arms, hands/fingers (indices 18-43 left hand, 44-69 right hand), legs, and feet
* Positions are body-relative; invalid joints are logged as `0,0,0,0,0,0,0`

### Hand Pose CSV (New)

* Files: `left_hand_pose.csv`, `right_hand_pose.csv`
* Logs 24 hand bones from `OVRSkeleton` in world space, plus pinch/grab state
* Format:

  ```
  UnixTime,UnityTime,PinchStrength,IsGrabbing,HandBone0_Px,HandBone0_Py,HandBone0_Pz,HandBone0_Rx,HandBone0_Ry,HandBone0_Rz,HandBone0_Rw,...,HandBone23_Rw
  ```

* `PinchStrength`: 0.0-1.0 continuous value from index finger pinch
* `IsGrabbing`: 1 if `PinchStrength > 0.8`, else 0

### Camera Characteristics (JSON)

* Obtained via Android Camera2 API
* Includes pose, intrinsics (fx, fy, cx, cy), sensor info, etc.

### Image Format (JSON)

* Includes resolution, format (e.g., `YUV_420_888`), per-plane buffer info
* Contains baseMonoTimeNs and baseUnixTimeMs for timestamp alignment

### Passthrough Camera (Raw YUV)
- Raw YUV frames are stored as `.yuv` files under `left_camera_raw/` and `right_camera_raw/`.
- Image format and buffer information are provided in the accompanying `*_camera_image_format.json` files.

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

## 🚀 Installation & Usage

1. Download the APK from [GitHub Releases](https://github.com/t-34400/QuestRealityCapture/releases)
2. Install with ADB:

   ```bash
   adb install QuestRealityCapture.apk
   ```
3. Launch the app on **Meta Quest 3 or 3s** (firmware **v74+** required)
4. When the green instruction panel appears, press the **menu button on the left controller** to dismiss it and start logging
5. Data will be saved under the session folder as described above

Required permissions (camera/scene access) are requested automatically at runtime.

---

## 🛠 Environment

* Unity **6000.0.30f1**
* Meta OpenXR SDK
* Meta XR SDK v81 (for body/hand tracking)
* Device: Meta Quest 3 or 3s only
* Approx. recording frame rate: \~25 FPS (camera & depth), body/hand poses logged every frame

---

## 📝 License

This project is licensed under the **[MIT License](LICENSE)**.

This project uses Meta’s OpenXR SDK — please ensure compliance with its license when redistributing.

---

## 📌 TODO
