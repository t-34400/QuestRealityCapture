# INDEX.md

## Purpose

This document is the navigation index for QuestRealityCapture specifications.

This document must not define behavioral requirements.

---

# Required Reading

Read `COMMON_RULES.md` before reading any other specification.

---

# Specifications

## Project-Wide Rules

* `COMMON_RULES.md`
  * Defines project-wide authority, change-management rules, architecture principles, compatibility policy, and project-specific global constraints.

## Recording Data Format

* `yuv_storage_format.md`
  * Defines recorded YUV frame storage compatibility requirements.
  * Read before modifying camera frame persistence, YUV handling, format metadata, file naming, or downstream-facing data layout.

## Native Camera Plugin

* `native_camera_plugin.md`
  * Defines Android NDK native plugin responsibilities, C API lifecycle, permission assumptions, camera opening behavior, FPS throttling, and native error reporting expectations.
  * Read before modifying `Native/`, native build files, native exported APIs, or native recording behavior.

## Unity Camera Architecture

* `unity_camera_architecture.md`
  * Defines Unity-side responsibilities, native bridge expectations, permission ownership, Unity/native responsibility boundaries, and scene-facing recorder lifecycle expectations.
  * Read before modifying Unity camera scripts or introducing Unity-to-native integration code.
## Recording Session

* `recording_session.md`
  * Defines Unity-side recording session orchestration, session directory ownership, output path construction, recording on/off behavior, and JSON-based camera/depth/pose configuration.
  * Read before modifying recording session controllers, save directory management, recording on/off scene behavior, or config JSON handling.


* `legacy_recording_format.md`
  * Defines the canonical QuestRealityCapture recording output layout and compatibility requirements.
