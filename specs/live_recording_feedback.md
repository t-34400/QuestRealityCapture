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

Live feedback must be optional and must be disabled by default unless a user configuration explicitly enables it.

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

Older configuration JSON files that omit `liveFeedback` must continue to load and must behave as if live feedback is disabled.

