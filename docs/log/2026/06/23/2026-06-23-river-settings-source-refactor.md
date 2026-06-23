# River Settings Source Refactor
**Date**: 2026-06-23
**Session**: River render CPU flame graph cleanup
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Remove the per-frame, per-river-object `RiverRenderObject.ApplySettings` hotspot shown in CPU flame graphs.

**Success Criteria:**
- `RiverRenderObject` no longer caches pass-wide `RiverRenderSettings`.
- `RiverProcessor.Draw` no longer copies settings into every render object each frame.
- `RiverRenderFeature` still binds the same shader parameters and cutoff values from the owning `RiverComponent.Settings`.

---

## Context & Background

**Previous Work:**
- `2026-06-23-river-camera-height-visibility.md`

**Why Now:**
- Runtime can generate thousands of `RiverRenderObject` instances. Copying identical settings into every object each frame creates avoidable CPU overhead.

---

## What We Did

### 1. Removed duplicated settings state from render objects
**Files Changed:** `Terrain/Rendering/River/RiverRenderObject.cs`, `Terrain/Rendering/River/RiverProcessor.cs`

**Implementation:**
- Deleted `RiverRenderObject.ApplySettings`.
- Removed all pass-wide settings fields from `RiverRenderObject`.
- Kept per-object/per-mesh fields: buffers, mesh draw, bounds, map extent, map world size, refraction clamp, world matrix.
- `RiverProcessor.Draw` now only updates enabled state, render group, and world transform per object.

**Rationale:**
- Settings are component-wide/pass-wide, not segment-local. Keeping copies on every segment object made the ownership unclear and created the flame graph hotspot.

### 2. Bound pass parameters from the component settings source
**Files Changed:** `Terrain/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- Added `GetRiverSettings(RiverRenderObject)` using the render object's `Source` `RiverComponent`.
- `Prepare` binds bottom/surface non-frame parameters from `RiverComponent.Settings`.
- `ResolveRiverMaxVisibleCameraHeight` scans draw-range objects and reads each object's source settings, preserving the max-cutoff fallback.
- Debug invariant now compares per-object mesh parameters plus source settings instead of comparing duplicated render-object fields.

**Rationale:**
- `RiverRenderFeature` already treats these values as shared pass parameters. Reading them from `RiverComponent.Settings` makes that ownership explicit.

### 3. Updated regression text checks
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- Tests now reject `ApplySettings` and `settings.` references in `RiverRenderObject`.
- Tests assert `RiverRenderFeature` reads settings from `RiverComponent` and binds shader parameters from `settings`.

---

## Problems Encountered & Solutions

### Problem 1: Parallel build locked `Terrain.dll`
**Symptom:** `dotnet build` failed with `Terrain\obj\Debug\net10.0-windows\Terrain.dll` locked by `VBCSCompiler` / Defender.

**Root Cause:** Two builds were launched in parallel and both wrote the same intermediate assembly.

**Solution:**
- Ran `dotnet build-server shutdown`.
- Re-ran builds sequentially.

---

## Code Quality Notes

### Performance
- Removed O(river object count) settings field copies from `RiverProcessor.Draw`.
- Remaining per-object work is the expected transform/enabled state sync.

### Testing
- `dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-build` passed.
- `dotnet build Terrain.Windows\Terrain.Windows.csproj --no-restore` passed.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverRenderObject` should stay mesh/object-only.
- Shared river shader settings live on `RiverComponent.Settings`.
- Do not reintroduce `RiverRenderObject.ApplySettings`; it was a measured CPU hotspot.

