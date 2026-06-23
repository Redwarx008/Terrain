# Ocean Render Component Skeleton
**Date**: 2026-06-24
**Session**: task-5-ocean-render-component
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add the Ocean ECS/render object skeleton and full-map horizontal quad buffer generation without adding an Ocean shader, render feature, scene entity, or compositor asset.

**Success Criteria:**
- `OceanComponent` uses `OceanProcessor`, exposes visibility/material/runtime input, and does not expose public `SeaLevel`.
- `OceanRenderObject` builds a full-map quad from `OceanRuntimeInput` and keeps CPU-side state testable without a real `GraphicsDevice`.
- `MapSurfaceProcessor` applies `OceanRuntimeInput(resources.SeaLevel, mapWorldSize)` only after terrain context is ready.
- Requested tests and `git diff --check` pass.

---

## Context & Background

**Previous Work:**
- `2026-06-24-river-sea-level-runtime-binding.md` bound runtime sea level into river render settings while intentionally leaving Ocean unimplemented.

**Current State:**
- Sea level remains authoritative in map settings / `TerrainRuntimeResourceBundle`.
- `MapSurfaceComponent` and `OceanComponent` do not expose public `SeaLevel`.

---

## What We Did

### 1. Ocean ECS and Render Object Skeleton
**Files Changed:** `Terrain/Rendering/Ocean/*`

**Implementation:**
- Added `OceanComponent`, `OceanMaterialSettings`, `OceanRuntimeInput`, `OceanVertex`, `OceanRenderObject`, `OceanProcessor`, and `OceanRenderGroups`.
- `OceanRenderObject.BuildQuad` creates 4 vertices and 6 indices covering `(0,0)` to `MapWorldSize` at `Y=SeaLevel`.
- `Rebuild` reuses the CPU quad data and uploads vertex/index buffers when a `GraphicsDevice` is available.

**Rationale:**
- CPU-side quad construction keeps tests deterministic and avoids brittle GPU-device setup in `Terrain.Editor.Tests`.

### 2. MapSurface Runtime Input Propagation
**Files Changed:** `Terrain/MapSurface/MapSurfaceProcessor.cs`

**Implementation:**
- After terrain initialization and map size validation, `MapSurfaceProcessor` applies `OceanRuntimeInput(resources.SeaLevel, mapWorldSize)` to an existing `OceanComponent`.
- Missing ocean remains silent and does not affect terrain or river.

---

## Decisions Made

### Decision 1: Keep Ocean Rendering as a Buffer Skeleton Only
**Context:** Task 5 explicitly excludes shader/render feature/scene/compositor assets.

**Decision:** Add ECS/processor/render-object types and GPU buffer generation only.

**Rationale:** This provides the runtime data and geometry boundary for later Ocean rendering work without committing to shader or compositor design.

---

## Code Quality Notes

### Testing
- Added `Terrain.Editor.Tests/OceanRenderingTests.cs`.
- Tests cover renderer attribute, lack of public `SeaLevel`, vertex layout, MapSurface source guard, and CPU-side quad state.

### Technical Debt
- No Ocean draw path exists yet; `OceanRenderFeature` and scene/compositor integration remain future tasks.

---

## Next Session

### Immediate Next Steps
1. Add Ocean shader/render feature only in the dedicated follow-up task.
2. Add runtime scene/compositor assets only when the draw path exists.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `OceanRenderObject.BuildQuad` is the deterministic CPU-side geometry path used by tests.
- `SeaLevel` must continue to come from `TerrainRuntimeResourceBundle`, not component public properties.
- No Ocean shader, render feature, resource loader, scene entity, or compositor asset was added in this session.
