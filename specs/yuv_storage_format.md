# yuv_storage_format.md

## Purpose

This specification defines the recorded YUV frame storage format for RealityLog.

The storage format is compatibility-sensitive because existing downstream processing already consumes files produced by the Kotlin/Camera2 implementation.

---

# Authority

This document is the authoritative specification for recorded YUV frame byte layout and related format metadata.

`COMMON_RULES.md` remains authoritative for project-wide compatibility policy.

---

# Compatibility Goal

The Android NDK implementation must preserve recorded frame compatibility with the existing Kotlin/Camera2 implementation.

Downstream processing must be able to consume newly recorded `.yuv` files without format migration.

---

# Frame Byte Layout

Each saved frame file must contain exactly the camera image plane buffers in camera-provided order:

```text
plane[0] raw bytes
+
plane[1] raw bytes
+
plane[2] raw bytes
```

For Android `YUV_420_888`, this corresponds to:

```text
Y plane raw bytes
+
U plane raw bytes
+
V plane raw bytes
```

The implementation must write the complete byte range returned by the platform for each plane.

---

# Prohibited Transformations

The following transformations are prohibited unless this specification is intentionally updated:

* I420 conversion
* NV12 conversion
* NV21 conversion
* RGB conversion
* YUV to JPEG conversion
* plane reordering
* stride removal
* padding removal
* chroma repacking
* pixel normalization
* row-by-row reconstruction

The recorded bytes must represent the raw plane buffers as exposed by the capture implementation.

---

# Kotlin Compatibility Rule

The Kotlin/Camera2 implementation stores:

```kotlin
plane[0].buffer
plane[1].buffer
plane[2].buffer
```

The NDK implementation must preserve the same semantic behavior by writing data returned from `AImage_getPlaneData()` for each plane in ascending plane index order.

---

# File Extension

Saved frame files must use the `.yuv` extension.

---

# File Naming

Frame file names should be based on the computed Unix timestamp in milliseconds when available.

The current native implementation uses:

```text
<computed_unix_time_ms>.yuv
```

Timestamp naming must not be changed without reviewing downstream compatibility.

---

# Timestamp Mapping

When native camera timestamps are monotonic or sensor-based, implementations may map image timestamps to Unix time using a captured base time pair.

The current native formula is:

```text
unix_ms = baseUnixTimeMs + (imageTimestampNs - baseMonoTimeNs) / 1_000_000
```

Changes to timestamp mapping require compatibility review because downstream processing may depend on file names or metadata timing.

---

# Format Metadata

Recording implementations should emit format metadata that allows downstream tools to interpret raw frame bytes.

Metadata should include at least:

* width
* height
* image format name
* per-plane row stride
* per-plane pixel stride
* per-plane buffer size
* base time information when timestamp mapping is used

Metadata must describe the actual recorded raw byte layout.

---

# Validation Requirements

Changes affecting YUV persistence must validate that:

* frames are written in plane index order
* no conversion or stride removal is introduced
* the saved byte count matches the sum of recorded plane buffer sizes
* metadata reflects the written frame structure

Prefer targeted validation over broad test execution.
