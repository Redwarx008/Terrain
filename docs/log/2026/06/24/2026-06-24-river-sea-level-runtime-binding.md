# River Sea Level Runtime Binding
**Date**: 2026-06-24
**Session**: task-4-ocean-system-river-sea-level
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Drive River bottom/surface `_WaterHeight` from map settings `sea_level` through the runtime resource bundle and river render settings.

**Success Criteria:**
- Runtime `RiverProcessor` copies `TerrainRuntimeResourceBundle.SeaLevel` into river render settings.
- `RiverRenderFeature` binds `_WaterHeight` for both bottom and surface from river settings, not a hard-coded `3.0f`.
- `MapSurfaceComponent` remains free of `SeaLevel`.
- No Ocean shader, component, or runtime input type is introduced.
- Requested tests and `git diff --check` pass.

---

## Context & Background

**Previous Work:**
- Task 2 committed editor settings persistence for `ShowOcean` and `SeaLevel`: `abe5df3 feat: expose sea level editor setting`.
- Runtime bundle already carried `TerrainRuntimeResourceBundle.SeaLevel`.

**Current State:**
- River runtime settings now include `SeaLevel = 3.8f`.
- SDSL `_WaterHeight` defaults remain `3.0f` only as shader fallback; runtime binding overrides them.

**Why Now:**
- Task 4 required river water/ocean fade height to follow map settings without implementing Ocean rendering.

---

## What We Did

### 1. Runtime Settings Propagation
**Files Changed:** `Terrain/Rendering/River/RiverRenderSettings.cs`, `Terrain/Rendering/River/RiverProcessor.cs`

**Implementation:**
- Added `RiverRenderSettings.SeaLevel`.
- After loading the runtime bundle, `RiverProcessor` writes `bundle.SeaLevel` into `component.Settings.SeaLevel`.
- Existing `RiverMaxVisibleCameraHeight` propagation remains and is still written from the same bundle path.

**Rationale:**
- `RiverRenderSettings` is already the pass-wide source resolved through `RiverRenderObject.Source`, matching the existing shared parameter model.

### 2. River Shader Parameter Binding
**Files Changed:** `Terrain/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- `ApplyBottomParameters` binds `RiverBottomKeys._WaterHeight` from `settings.SeaLevel`.
- `ApplySurfaceParameters` binds `RiverSurfaceKeys._WaterHeight` from `settings.SeaLevel`.
- Removed the static `bottomEffect.Parameters.Set(RiverBottomKeys._WaterHeight, 3.0f)` from static resource binding.
- Added `SeaLevel` to the debug shared-parameter invariant for multi-river pass reuse.

**Rationale:**
- The render feature binds non-frame river parameters once during `Prepare`; sea level is one of those shared pass-wide parameters.

### 3. Tests and Documentation
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Testing:**
- Text tests now assert runtime no longer binds `_WaterHeight` to `3.0f`.
- Text tests assert bottom and surface `_WaterHeight` are both bound from `settings.SeaLevel`.
- Text tests assert `RiverProcessor` copies `bundle.SeaLevel` into river settings.

---

## Decisions Made

### Decision 1: Store Sea Level in `RiverRenderSettings`
**Context:** Sea level is pass-wide and comes from map settings, not per mesh geometry.

**Decision:** Use `RiverRenderSettings.SeaLevel` instead of copying sea level into `RiverRenderObject` or `RiverMeshData`.

**Rationale:** This follows the existing `RiverRenderFeature.GetRiverSettings` pattern and avoids duplicating pass-wide state per render object.

**Trade-offs:** Multiple drawable river objects in the same pass must share sea level, enforced by the debug invariant.

---

## Code Quality Notes

### Testing
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed.
- `git diff --check` passed with CRLF working-copy warnings only.

### Shader Workflow
- SDSL files were not changed.
- Generated `.sdsl.cs` key files were not changed.
- `StrideAssetUpdateGeneratedFiles`, `StrideCleanAsset`, and `StrideCompileAsset` were not run because no shader parameter/default changed.

---

## Next Session

### Immediate Next Steps
1. Implement actual Ocean shader/component only in the dedicated Ocean task.
2. If sea level becomes live-editable in runtime/editor preview, wire the existing `Settings.SeaLevel` dirty/update path to the concrete preview service then.

---

## Session Statistics

**Files Changed:** 7
**Commits:** 1 planned

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Runtime sea level source of truth is `TerrainRuntimeResourceBundle.SeaLevel`.
- River consumes it through `RiverRenderSettings.SeaLevel`.
- `MapSurfaceComponent` intentionally still does not expose sea level.
- Ocean rendering is still not implemented by this task.

**Gotchas for Next Session:**
- Do not reintroduce `bottomEffect.Parameters.Set(RiverBottomKeys._WaterHeight, 3.0f)`.
- SDSL `_WaterHeight = 3.0f` is currently only a fallback default.
