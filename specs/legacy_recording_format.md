# Legacy Recording Format

Authoritative description of the original QuestRealityCapture output format that downstream tooling may depend on.

## Session Layout

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

## Pose CSV

Header:

```csv
unix_time,ovr_timestamp,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,rot_w
```

Values are written directly from OVR node poses without axis conversion.

## Camera YUV

File name:

```text
<unixTimeMs>.yuv
```

Content is plane[0], plane[1], plane[2] byte data concatenated in order.

## Depth

Raw depth files:

```text
<unixTimeMs>.raw
```

Content is float32 depth data.

Descriptor header:

```csv
timestamp_ms,ovr_timestamp,create_pose_location_x,create_pose_location_y,create_pose_location_z,create_pose_rotation_x,create_pose_rotation_y,create_pose_rotation_z,create_pose_rotation_w,fov_left_angle_tangent,fov_right_angle_tangent,fov_top_angle_tangent,fov_down_angle_tangent,near_z,far_z,width,height
```
