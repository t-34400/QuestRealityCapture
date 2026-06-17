# COMMON_RULES.md

## Purpose

This document defines project-wide principles, specification authority, architecture expectations, compatibility requirements, and change management rules for RealityLog.

RealityLog records Meta Quest passthrough camera frames and stores them for downstream processing.

The primary project goal is preserving recorded data compatibility while migrating camera recording from the existing Kotlin/Camera2 implementation to an Android NDK native plugin.

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

# Specification-Driven Development

Implementation follows specifications.

When intentionally changing behavior:

1. Update specifications.
2. Update tests.
3. Update implementation.

Keep these changes in the same change set whenever practical.

---

# Explicit Behavior

Important behavior must be documented.

Do not rely on:

* developer assumptions
* undocumented conventions
* historical implementation quirks
* prior discussions

If behavior matters, specify it.

---

# Traceability

Important requirements should remain traceable.

Preferred chain:

```text
Requirement
    ->
Specification
    ->
Test
    ->
Implementation
```

Changes should preserve this relationship whenever practical.

---

# Specification Organization

Specifications must remain maintainable.

Prefer:

* small focused documents
* one responsibility per document
* explicit references between documents
* topic-oriented organization

Avoid:

* monolithic specifications
* duplicated requirements
* mixed responsibilities
* large unstructured documents

When a specification becomes difficult to navigate, split it into smaller specifications.

---

# Code Organization

Source code must remain maintainable.

Prefer:

* small focused modules
* explicit interfaces
* clear ownership boundaries
* responsibility-oriented structure

Avoid:

* god objects
* multi-purpose modules
* hidden coupling
* excessive file growth

Guidelines:

* keep source files near 300 lines or less when practical
* split files before 500 lines unless strongly justified
* keep functions near 50 lines or less when practical

These are guidelines, not hard limits.

---

# Complexity Management

When adding functionality:

1. Extend an existing specification if the responsibility matches.
2. Create a new specification if a new responsibility is introduced.
3. Avoid expanding unrelated specifications.

When adding code:

1. Extend an existing module if the responsibility matches.
2. Create a new module if a new responsibility is introduced.
3. Avoid accumulating unrelated responsibilities in a single file.

Prefer introducing new focused components over growing existing multi-purpose components.

---

# Architecture Principles

Unless a more specific specification states otherwise:

* prefer modular design
* prefer explicit interfaces
* prefer deterministic behavior
* prefer testable components
* separate responsibilities clearly
* avoid hidden coupling
* avoid unnecessary complexity

---

# Testing Principles

Tests verify observable behavior.

Tests should focus on:

* expected outputs
* public interfaces
* documented requirements
* regression protection

Avoid testing implementation details unless necessary.

Tests are validation artifacts, not primary specifications.

---

# Documentation Principles

Documentation should describe:

* behavior
* interfaces
* assumptions
* constraints

Avoid duplicating information maintained elsewhere.

Keep documentation synchronized with behavior changes.

---

# Compatibility Policy

RealityLog uses strict recorded-data compatibility.

Compatibility requirements are defined by focused project specifications.

Behavior that affects recorded output, file layout, timing semantics, or downstream consumption must not change unless explicitly approved and documented.

Compatibility with existing downstream processing takes precedence over performance optimization.

---

# Decision Records

Long-term architectural decisions should be documented separately.

Decision records should explain:

* context
* decision
* consequences

Avoid embedding architectural rationale directly into behavioral specifications.

---

# Project-Specific Rules

## Project Purpose

RealityLog records Meta Quest passthrough camera frames for later processing and analysis.

The project must preserve downstream compatibility while migrating from a Kotlin/Camera2 implementation to an Android NDK implementation.

---

## Domain Concepts

### Camera Frame

A single image frame captured from a Meta Quest passthrough camera.

### Passthrough Camera

A Meta Quest headset camera exposed through Android camera APIs and identified by Meta-specific camera metadata.

### Recording

Persisting captured camera frames and related metadata to storage.

### Downstream Processing

External processing pipelines that consume RealityLog recordings.

### Compatibility

The ability of downstream systems to consume newly generated recordings without modification.

---

## Architecture Constraints

Unity is responsible for:

* permission acquisition
* user interaction
* recorder lifecycle orchestration
* recording configuration
* save destination selection

The native plugin is responsible for:

* camera access
* frame acquisition
* frame persistence
* recording frame-rate throttling
* native error reporting

Unity and native responsibilities must remain explicit.

---

## Coding Rules

Prefer clear and maintainable implementations over premature optimization.

Code comments should only explain non-trivial logic.

Avoid introducing abstraction layers that are not justified by actual project needs.

---

## Compatibility Requirements

Recorded frame data compatibility is the highest-priority project requirement.

Detailed YUV storage compatibility requirements are defined in `yuv_storage_format.md`.

The output format must not be modified implicitly through refactoring, cleanup, or optimization work.

---

## Testing Requirements

Changes affecting recording behavior must verify compatibility requirements.

Prefer targeted tests.

Do not run the full test suite by default.

When broader validation is required, run multiple relevant test subsets sequentially with short timeouts.

Validation should focus on:

* recorded output structure
* metadata correctness
* native plugin behavior
* Unity/native integration boundaries
* compatibility guarantees

---

## Performance Requirements

Performance improvements must not change output format.

Recording correctness and compatibility are more important than throughput optimization.

Frame dropping for target FPS control is permitted only when specified by the recording specifications.

---

## Safety Requirements

Recording failures must not silently corrupt output.

Errors should be detectable and reportable.

Partial recordings should remain distinguishable from successful recordings whenever practical.

---

## Data Rules

Recorded YUV output is treated as a compatibility-sensitive format.

Output format changes require:

1. specification update
2. compatibility impact review
3. validation update
4. implementation update

Generated build outputs must not be committed or included in source-oriented handoff archives unless explicitly required.
