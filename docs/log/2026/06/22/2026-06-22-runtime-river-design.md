# Runtime River Design
**Date**: 2026-06-22
**Session**: runtime river design
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Design how Runtime should display `game/map/rivers.png` through Stride scene assets.

**Success Criteria:**
- Keep `Terrain.Windows` independent from `Terrain.Editor`.
- Use `MainScene.sdscene` for `RiverSystem` instead of runtime code creation.
- Use `GraphicsCompositor.sdgfxcomp` for `RiverRenderFeature` instead of runtime code registration.
- Keep `TerrainRuntimeResourceBundle` as resource/config data only.

---

## Context & Background

**Previous Work:**
- `docs/superpowers/specs/2026-06-21-river-width-gradient-design.md`
- `docs/log/2026/06/21/2026-06-21-river-width-gradient-implementation.md`
- `docs/log/decisions/adr-014-river-rendering-architecture.md`

**Current State:**
- River mesh generation and rendering currently live in `Terrain.Editor`.
- Runtime `Terrain.Windows` references only `Terrain`.
- `Terrain/Assets/MainScene.sdscene` already owns the runtime `TerrainComponent`.
- `Terrain/Assets/GraphicsCompositor.sdgfxcomp` already owns `TerrainRenderFeature`.

---

## What We Did

### 1. Runtime River Architecture Spec
**Files Changed:** `docs/superpowers/specs/2026-06-22-runtime-river-design.md`

**Design:**
- Move shared River generation/rendering code into `Terrain`.
- Add `RiverSystem` with `RiverComponent` to `MainScene.sdscene`.
- Register `RiverRenderFeature` in `GraphicsCompositor.sdgfxcomp`.
- Keep `RiverComponent` as data container.
- Keep `RiverProcessor` responsible for runtime mesh generation and render object synchronization.
- Do not introduce `RiverRuntimeProcessor`.

---

## Decisions Made

### Decision 1: River is scene-asset driven
**Context:** Runtime should use Stride's scene system rather than creating the river entity in game startup code.

**Decision:** `RiverSystem` belongs in `Terrain/Assets/MainScene.sdscene`.

**Rationale:** Scene assets should define scene composition; processors should implement runtime behavior.

### Decision 2: River render feature is compositor-asset driven
**Context:** Runtime render features should not be patched into the compositor from `Terrain.Windows`.

**Decision:** `RiverRenderFeature` belongs in `Terrain/Assets/GraphicsCompositor.sdgfxcomp`.

**Rationale:** The graphics compositor asset already owns runtime render feature configuration.

### Decision 3: One processor name
**Context:** A separate `RiverRuntimeProcessor` would split behavior naming from Stride's component processor pattern.

**Decision:** Use one `RiverProcessor`.

**Rationale:** `RiverComponent -> RiverProcessor` should mirror `TerrainComponent -> TerrainProcessor`.

---

## Architecture Impact

### Documentation Updates Required
- [ ] Review and approve `docs/superpowers/specs/2026-06-22-runtime-river-design.md`.
- [ ] After implementation, update `docs/ARCHITECTURE_OVERVIEW.md`.
- [ ] After implementation, update `docs/CURRENT_FEATURES.md`.

### Architectural Decisions That Changed
- **From:** River rendering is an editor-owned subsystem.
- **To:** River core is a shared runtime subsystem in `Terrain`, while editor UI remains in `Terrain.Editor`.
- **Scope:** River code ownership, scene asset composition, graphics compositor asset configuration.

---

## Next Session

### Immediate Next Steps
1. User reviews `docs/superpowers/specs/2026-06-22-runtime-river-design.md`.
2. If approved, invoke `superpowers:writing-plans`.
3. Create an implementation plan for moving River core into `Terrain` and wiring scene/compositor assets.

### Docs to Read Before Next Session
- `docs/superpowers/specs/2026-06-22-runtime-river-design.md`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- latest log in `docs/log/2026/06/22/`

---

## Session Statistics

**Files Changed:** 2
**Commits:** 2

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Runtime River should use `MainScene.sdscene` for `RiverSystem`.
- Runtime River should use `GraphicsCompositor.sdgfxcomp` for `RiverRenderFeature`.
- `TerrainRuntimeResourceBundle` must not create or attach scene objects.
- `RiverComponent` is the asset hook and data container.
- `RiverProcessor` handles mesh generation and render synchronization.

**Gotchas for Next Session:**
- Do not make `Terrain.Windows` reference `Terrain.Editor`.
- Do not create `RiverSystem` from runtime code.
- Do not register `RiverRenderFeature` from runtime code.
- Follow the Stride shader asset workflow when moving River SDSL files.

---

## Links & References

### Related Documentation
- `docs/superpowers/specs/2026-06-22-runtime-river-design.md`
- `docs/log/decisions/adr-014-river-rendering-architecture.md`

---
