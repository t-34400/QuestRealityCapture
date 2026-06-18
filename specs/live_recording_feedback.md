# live_recording_feedback.md

## Purpose

This specification defines optional Quest-side live recording feedback for RealityLog recording sessions.

Live feedback helps an operator understand observed capture coverage and tracking diagnostics while recording is still active.

---

# Authority

This document is authoritative for live recording feedback configuration, runtime ownership, and compatibility constraints.

Recording session lifecycle and output path ownership are defined in `recording_session.md`.

Depth persistence and descriptor output remain governed by `recording_session.md` and `legacy_recording_format.md`.

---

# Compatibility Requirements

Live feedback must not change the default recording output layout.

Live feedback must not change recorded file names, CSV columns, raw depth byte layout, or YUV byte layout.

Live feedback must be optional and is enabled in the bundled default recording configuration so operators see coverage and diagnostics without creating an external override. A user configuration may explicitly disable it.

When live feedback is disabled, runtime components must avoid allocating feedback-only buffers, dispatching feedback-only compute work, or drawing feedback-only overlays.

---

# Depth Readback Ownership

Saving depth frames to disk remains the responsibility of the depth exporter.

Live coverage visualization must not add a second GPU-to-CPU readback path for the same depth frame stream.

A live coverage implementation may read the current GPU depth texture through a shared depth frame provider and should keep coverage processing GPU-resident.

The shared depth frame provider may be used by both persistence and live feedback components, but raw depth file output and descriptor CSV output remain owned by the depth exporter.

---

# Live Coverage Semantics

Live coverage is a coarse operator aid, not a dense reconstruction product.

Coverage should represent approximate observed regions during the active recording session.

Coverage implementations should prefer low update rates, coarse depth sampling, and coarse voxel aggregation suitable for Quest runtime performance.

Recommended initial defaults are:

```text
targetUpdateFps = 3
samplingStep = 24
voxelSizeMeters = 0.15
maxVoxels = 30000
minDepthMeters = 0.3
maxDepthMeters = 5.0
eye = left
```

Coverage implementations may keep a persistent coarse voxel map for the full recording session.

---

# Diagnostics Semantics

Recording diagnostics are warning overlays and must not stop recording by default.

Tracking warnings should help the operator decide whether to continue, rescan, or restart manually.

A tracking discontinuity may create a new diagnostics or coverage segment so that observations before and after the event can be distinguished visually.

Diagnostics should support HUD warnings, trajectory display, and event markers when enabled by configuration.

---

# JSON Configuration

Live feedback configuration is nested under `liveFeedback` in the recording configuration JSON.

Supported fields include:

```json
{
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

Older external configuration JSON files that omit `liveFeedback` must continue to load and must behave as if live feedback is disabled. The bundled default JSON may explicitly set `liveFeedback.enabled` to true.


---

# Initial Live Coverage Implementation

The initial live coverage implementation uses a GPU-resident coarse voxel map.

When `liveFeedback.enabled` and `liveFeedback.coverage.enabled` are both true, `RecordingSessionController` may start a `LiveDepthCoverageVisualizer` after depth export startup and before pose logging startup.

Live coverage startup failure must be logged as a warning and must not fail or stop the recording session.

The initial implementation samples one configured depth eye, defaults to the left eye, updates at the configured low rate, converts raw depth buffer samples to linear meter depth using the corresponding `DepthFrameDesc.nearZ` and `DepthFrameDesc.farZ`, converts those depths into Quest world-space points using the corresponding `DepthFrameDesc` pose and Meta Depth API FOV conventions, inserts those points into a fixed-size GPU hash voxel map, and draws occupied voxels through a Unity Particle System renderer by default. A procedural billboard renderer may remain available as a diagnostic comparison path, but it must not be required for normal Quest runtime coverage display. Infinite or invalid far clipping values must use the infinite-far conversion branch. `DepthFrameDesc` FOV tangents must follow Meta SDK semantics and store absolute tangent magnitudes (`tan(abs(angle))`); pixel-to-camera reconstruction must explicitly project horizontal samples over `[-left, +right]` and vertical samples over `[-down, +top]`. The depth camera transform must mirror Meta SDK depth camera matrix construction by applying a `(1, 1, -1)` local scale to the `DepthFrameDesc` pose and by applying the optional tracking-space transform before writing Unity world-space coverage points.

The visualizer must obtain depth textures through `DepthFrameProvider` and must not issue a second `AsyncGPUReadback` request for the raw depth frame stream. It may asynchronously read back the derived coarse coverage voxel buffers at a low rate when using the Particle System renderer, because those buffers are feedback-only visualization products and not depth persistence data.

When coverage visualization stops, feedback-only compute buffers must be released and the visualizer must end its depth provider usage.

---

# Initial Recording Diagnostics Implementation

When `liveFeedback.enabled` and `liveFeedback.diagnostics.enabled` are both true, `RecordingSessionController` may start a recording diagnostics controller after live coverage startup and before pose logging startup.

Recording diagnostics startup failure must be logged as a warning and must not fail or stop the recording session.

The initial diagnostics implementation samples the HMD pose during recording, draws an optional world-space trajectory, places optional event markers at detected discontinuities, and shows an optional operator HUD.

Tracking discontinuity detection is warning-only. It must not stop recording by default.

The initial discontinuity detector may use pose deltas rather than device-native tracking confidence. A new event should be emitted when either the position delta exceeds `positionJumpMeters` or the rotation delta exceeds `rotationJumpDegrees` between monitored HMD samples.

Diagnostics overlays must not add new recording output files and must not change existing camera, depth, or pose persistence formats.


---

# Coverage Segmentation

Live coverage may be segmented by diagnostics tracking events.

When diagnostics reports a tracking discontinuity while live coverage is active, coverage visualization should advance to the event segment identifier.

Coverage samples captured after the event should be stored under the new segment so that repeated observations of the same coarse voxel after a tracking discontinuity do not overwrite the previous segment's observations.

The initial segmented coverage renderer may draw the current segment at full opacity and previous segments at reduced opacity.

Coverage segmentation is a visualization aid only. It must not stop recording, change output files, or modify persisted pose, depth, or camera data.

---


# Runtime Particle Rendering Diagnostic

A standalone runtime dummy particle visualizer may be provided to isolate Unity, XR origin, and Particle System rendering behavior from depth sampling and depth pose conversion.

The diagnostic visualizer must not read depth textures, must not allocate live coverage compute buffers, must not change recording output files, and must not be controlled by recording JSON configuration. It may place a fixed world-space particle grid or axes in front of a selected reference transform once at startup or through an inspector context action.

The diagnostic particles must use `ParticleSystemSimulationSpace.World` so operators can verify whether particles remain fixed when the HMD or XR origin moves. If dummy particles drift, the problem is in Unity/XR/ParticleSystem rendering or world-origin handling. If dummy particles remain fixed while live depth coverage drifts, the problem is in depth sampling, pose conversion, frame synchronization, or coverage accumulation.

# Editor-Only Debug Coverage Source

The live coverage visualizer may provide Editor-only debug point sources to isolate rendering problems from Quest depth sampling problems.

Editor debug sources must be exposed only through Unity Editor inspector fields or Editor-only context menu actions guarded by `UNITY_EDITOR`.

Editor debug source selection must not be part of the runtime recording JSON configuration and must not affect Quest runtime behavior.

Editor debug coverage may bypass `DepthFrameProvider` and compute depth sampling, but it should use the same coverage point material, buffers, and draw path as runtime live coverage so that rendering and world-space placement can be tested independently.
