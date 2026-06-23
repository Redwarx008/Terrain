# Map Surface Coordinator Review Fix
**Date**: 2026-06-24
**Session**: map-surface-coordinator-review-fix
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Address Task 3 code review feedback for the MapSurface coordinator skeleton.

**Success Criteria:**
- Runtime `MainScene.sdscene` contains a `MapSurfaceComponent` coordinator entity.
- `MapSurfaceProcessor` does not throw or retry every frame when bootstrap loading fails.
- Tests cover scene wiring and resource failure gating.
- Continue to avoid `SeaLevel` on `MapSurfaceComponent` and avoid future Ocean/River runtime API references.

---

## What We Did

### 1. Runtime Scene Wiring
**Files Changed:** `Terrain/Assets/MainScene.sdscene`

**Implementation:**
- Added root `MapSurface` entity.
- Added `MapSurfaceComponent` referencing the existing Terrain entity `1cfe1131-cc2f-4a41-947d-3102b1f351dd`.
- Added `RiverEntity` reference to existing RiverSystem entity `c8f8f226-3477-45ec-84d8-d8e8de365e1b`.
- Left `OceanEntity` as `null`.

**Rationale:**
- Without a scene component instance, Stride never registers `MapSurfaceProcessor`, so the coordinator cannot drive terrain bundle injection.

### 2. Resource Load Failure Gate
**Files Changed:** `Terrain/MapSurface/MapSurfaceProcessor.cs`, `Terrain/MapSurface/MapSurfaceRuntimeState.cs`

**Implementation:**
- Added `ResourceLoadFailed` and `ResourceLoadFailureDiagnostic` to `MapSurfaceRuntimeState`.
- Replaced throwing `EnsureResources` with `TryEnsureResources`.
- First bootstrap failure records the diagnostic and returns `false`; later calls return `false` without invoking the loader.

**Rationale:**
- Coordinator bootstrap failures should not crash the update loop or produce per-frame log spam. Terrain fallback remains intact because `TerrainProcessor` still loads resources itself when no coordinator bundle was applied.

### 3. Tests
**Files Changed:** `Terrain.Editor.Tests/MapSurfaceCoordinatorTests.cs`

**Implementation:**
- Added a text regression test for `MainScene.sdscene` coordinator presence and existing entity references.
- Added a unit test proving resource-load failure is latched and does not retry the loader.
- Made the repository-root helper work from git worktrees where `.git` is a file.

---

## Decisions Made

### Decision 1: Latch Coordinator Bootstrap Failure
**Context:** A failed `GameRuntimeResourceBootstrap.Load()` previously escaped from `Update` and would be retried every frame.

**Decision:** Store the failure in `MapSurfaceRuntimeState` and stop retrying until the component/state is recreated.

**Rationale:** This matches the existing runtime terrain failure-gate intent and avoids update-loop instability while preserving terrain's existing fallback path.

**Trade-offs:** A runtime resource fix on disk will require recreating/reloading the coordinator state before it retries.

---

## Architecture Impact

### Documentation Updates Required
- [x] Updated `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Updated `docs/CURRENT_FEATURES.md`

---

## Code Quality Notes

### Testing
- **Tests Written:** 2 additional focused tests.
- **Verification:** `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`; `git diff --check`.
- **Status:** Passed. Existing NuGet vulnerability warnings remain.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Runtime scene now includes a root `MapSurface` entity.
- `MapSurfaceProcessor.TryEnsureResources` is the failure-gated resource load entry point.
- `OceanEntity` is intentionally null until ocean runtime types exist.

**Gotchas for Next Session:**
- Keep `SeaLevel` authority in map settings / `TerrainRuntimeResourceBundle`.
- Do not add future Ocean/River runtime API calls while working on the Task 3 skeleton.
