# Map Surface Coordinator Skeleton
**Date**: 2026-06-23
**Session**: map-surface-coordinator-skeleton
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Implement ocean-system Task 3: a thin `MapSurfaceComponent` / `MapSurfaceProcessor` skeleton that coordinates terrain runtime resource initialization.

**Success Criteria:**
- `MapSurfaceComponent` references Terrain/River/Ocean entities and does not expose `SeaLevel`.
- `MapSurfaceProcessor` loads `GameRuntimeResourceBootstrap` once per component state, injects the runtime bundle into terrain, waits for terrain readiness, and stores a runtime context.
- No Ocean/River future runtime types are referenced.
- Focused tests pass and the change is committed.

---

## Context & Background

**Previous Work:**
- Latest commit before this session: `2874f27 fix: preserve sea level api compatibility`.
- Related plan: `docs/superpowers/plans/2026-06-23-ocean-system.md` Task 3.

**Current State:**
- Task 1/2 already added `[settings].sea_level`, runtime bundle propagation, and Settings UI sea level / show ocean controls.

**Why Now:**
- The project needs a single surface initialization entry point before adding actual ocean/river shared sea-level runtime input.

---

## What We Did

### 1. Added MapSurface Runtime Skeleton
**Files Changed:** `Terrain/MapSurface/*`

**Implementation:**
- Added `MapSurfaceComponent` with `TerrainEntity`, `RiverEntity`, and `OceanEntity` references.
- Added `MapSurfaceRuntimeState` with resource load, context, and missing-reference log state.
- Added `MapSurfaceRuntimeContext` carrying resources, terrain, map world size, and sea level.
- Added `MapSurfaceProcessor` registered via `DefaultEntityComponentProcessorAttribute`.

**Rationale:**
- The coordinator establishes the initialization ordering boundary without coupling to future `OceanComponent` or river runtime input types.

### 2. Terrain Accepts Coordinator Bundle
**Files Changed:** `Terrain/Core/TerrainComponent.cs`, `Terrain/Core/TerrainProcessor.cs`

**Implementation:**
- `TerrainComponent` now exposes `RuntimeResourceBundle` with a private setter and `ApplyRuntimeResourceBundle`.
- `TerrainProcessor.TryLoadTerrainData` prefers `component.RuntimeResourceBundle ?? LoadRuntimeResourceBundle()`.

**Rationale:**
- Terrain keeps its existing fallback behavior when no coordinator is present, while MapSurface can provide a shared bundle loaded from map settings.

### 3. Focused Tests
**Files Changed:** `Terrain.Editor.Tests/MapSurfaceCoordinatorTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- Added tests for processor registration, absence of `SeaLevel` on `MapSurfaceComponent`, and terrain bundle injection.
- Watched the tests fail first on missing `Terrain.MapSurface` types, then implemented the skeleton.

---

## Decisions Made

### Decision 1: Coordinator Stores Context Only
**Context:** Task 3 must not implement Ocean/River runtime or reference future types.

**Decision:** `MapSurfaceProcessor` only applies the bundle to terrain and stores `MapSurfaceRuntimeContext` once terrain is initialized.

**Rationale:** This preserves the intended initialization order and keeps Task 3 independent from later Ocean/River runtime APIs.

**Trade-offs:** River and ocean are not driven by the coordinator yet; later tasks must consume the saved context or extend the processor after the relevant runtime APIs exist.

---

## What Worked ✅

1. **Focused reflection/API tests**
   - They caught the missing component/API surface before implementation and stayed independent of GPU/editor runtime.

2. **Existing terrain fallback path**
   - The `RuntimeResourceBundle ?? LoadRuntimeResourceBundle()` change preserved current startup behavior for scenes without MapSurface.

---

## What Didn't Work ❌

1. **Nullable inference through `bool` helper**
   - A helper returning `bool` with `out TerrainComponent?` still produced a new CS8602 warning.
   - The processor now resolves terrain inline with pattern matching to avoid adding nullable noise.

---

## Architecture Impact

### Documentation Updates Required
- [x] Updated `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Updated `docs/CURRENT_FEATURES.md`

### New Pattern
**Thin coordinator skeleton**
- Coordinators may load shared resource/settings state and store a context before downstream runtime APIs exist, but must not invent placeholder future component contracts.

---

## Code Quality Notes

### Testing
- **Tests Written:** 3 focused tests in `MapSurfaceCoordinatorTests`.
- **Verification:** `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`; `git diff --check`.
- **Status:** Passed. Existing NuGet vulnerability and pre-existing compiler warnings remain.

### Technical Debt
- No new runtime river/ocean behavior was added by design.

---

## Next Session

### Immediate Next Steps
1. Add explicit river runtime input once its API is introduced.
2. Add ocean runtime/rendering components in the later ocean-system tasks.
3. Wire `MapSurfaceComponent` into the runtime/editor scene roots when the ocean entity exists.

---

## Session Statistics

**Files Changed:** 11 content files
**Commits:** 1

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `MapSurfaceComponent` is intentionally a thin entity-reference coordinator and must not expose `SeaLevel`.
- `MapSurfaceProcessor` currently only drives terrain and saves context; do not call future Ocean/River APIs until those types exist.
- `TerrainComponent.ApplyRuntimeResourceBundle` is the handoff point from coordinator to terrain initialization.

**Gotchas for Next Session:**
- Do not move sea-level authority onto `MapSurfaceComponent`.
- Missing `TerrainEntity` / `TerrainComponent` logging is one-shot via `MissingReferencesLogged` to avoid per-frame spam.
