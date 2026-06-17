# COMMON_RULES.md

## Purpose

This document defines project-wide principles, specification authority, architecture expectations, compatibility requirements, and change management rules for QuestRealityCapture.

QuestRealityCapture is a Meta Quest data capture application.

The RealityLog Unity module records passthrough camera frames, poses, depth data, and related metadata for downstream processing.

The primary project goal is preserving recording data compatibility and maintaining a stable capture pipeline across supported Quest devices.

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
