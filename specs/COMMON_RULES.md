# COMMON_RULES.md

## Purpose

This document defines project-wide principles, specification authority, architecture expectations, compatibility requirements, and change management rules for RealityLog.

RealityLog records Meta Quest passthrough camera frames and stores them for downstream processing.

The primary project goal is preserving data compatibility across implementation changes.

---

# Specification Authority

When information conflicts, use the following priority:

```text
COMMON_RULES.md
    >
Other specifications
    >
Tests
    >
Implementation
```

Implementation behavior must not override written specifications.

Tests validate specifications but do not replace them.

---

# Read Before Modify

Before making changes:

1. Identify affected behavior.
2. Identify relevant specifications.
3. Identify affected tests.
4. Identify compatibility implications.

Do not begin implementation until the relevant specifications have been reviewed.

---

# Single Source of Truth

Each rule should have exactly one authoritative location.

Avoid duplicating behavioral requirements across documents.

INDEX documents are navigation documents.

INDEX documents must not define behavioral requirements.

---

# Project-Specific Rules

## Project Purpose

RealityLog records Meta Quest passthrough camera frames for later processing and analysis. The project must preserve downstream compatibility while migrating from a Kotlin/Camera2 implementation to an Android NDK implementation.

## Architecture Constraints

Unity is responsible for permissions, UI, lifecycle management, and configuration.

The native plugin is responsible for camera access, frame acquisition, frame persistence, and recording FPS throttling.

## Compatibility Requirements

Recorded frame data must remain compatible with the existing Kotlin implementation.

Unless an explicit specification states otherwise, output must remain:

```text
Y plane raw bytes
+
U plane raw bytes
+
V plane raw bytes
```

The following are prohibited without specification updates:

* I420 conversion
* NV12 conversion
* NV21 conversion
* RGB conversion
* plane reordering
* stride removal
* padding removal

Compatibility takes precedence over optimization.

## Testing Requirements

Prefer targeted tests. Avoid full-suite execution unless necessary.

## Performance Requirements

Performance improvements must not change output format.
