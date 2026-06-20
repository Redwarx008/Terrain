# River Z Fighting RenderDoc Analysis
**Date**: 2026-06-20
**Session**: river z fighting CK3 comparison
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Diagnose why `C:\Users\Redwa\Desktop\debug.rdc` shows blocky terrain exposure/z-fighting-like artifacts on a continuous river segment.
- Re-check how `C:\Users\Redwa\Desktop\ck3-river.rdc` avoids the same terrain interaction.

**Success Criteria:**
- Identify whether CK3 avoids the artifact through terrain carving, no depth write, shader depth, mesh height, or render-state bias.
- Keep the fix aligned with the capture evidence.

---

## Context & Background

**Previous Work:**
- Earlier in the day, the symptom was incorrectly narrowed to mesh height placement and a temporary `RiverMeshService` terrain clamp was added.
- User clarified the location is one continuous river segment and the artifact disappears when terrain is not drawn, so segment junction/miter and whole-mesh absence were ruled out.

**Current State Before Fix:**
- River rendering uses `bottom -> refraction -> surface`.
- `RiverSurface` outputs only `SV_Target`; it does not write `SV_Depth`.
- `RiverRenderFeature` uses `DepthStencilStates.DepthRead`, so river surface tests against scene depth but does not write it.
- Bottom and surface shared a small rasterizer bias: `DepthBias=-1`, `SlopeScaleDepthBias=-1`.

---

## What We Did

### 1. Compared Current Surface Against Terrain

Current `debug.rdc` draw IDs:
- terrain: `204`
- sky/background after terrain: `223`
- river bottom: `276/290`
- river surface: `319/337`

Pixel history/debug evidence:
- Failed pixel `(1180,640)`:
  - terrain depth: `0.99582636`
  - river surface `SV_Position.z`: `0.99584526`
  - result: surface shader produced water color, but depth test failed because water was about `1.89e-5` farther than terrain.
- Passing pixel `(800,620)`:
  - terrain depth: `0.99582261`
  - river surface `SV_Position.z`: `0.99581963`
  - result: surface passed because water was about `2.98e-6` closer than terrain.

This proves the local artifact is a near-coplanar depth race between terrain and the river surface, not a missing segment.

### 2. Compared CK3 Surface

CK3 draw IDs:
- bottom river: `332/334/336/338`
- terrain: `366`
- surface river: `460/462/464/466`

CK3 surface VS:
- transforms input world position directly with `ViewProjectionMatrix`.
- passes UV, transparency/distance, width, tangent, normal, and world position through.
- does not sample height or terrain in VS.

CK3 surface PS:
- output signature is only `SV_Target`; no `SV_Depth`.
- still samples terrain height/lookup textures for later surface wrapper effects, but not to rewrite hardware depth.

CK3 pixel/debug evidence:
- At `(1050,930)`, terrain history depth was `0.80338705`; surface `SV_Position.z` was `0.80038857`.
- At `(980,930)`, terrain depth was `0.80048406`; surface `SV_Position.z` was `0.79751605`.
- At `(1100,945)`, terrain depth was `0.80404258`; surface `SV_Position.z` was `0.80106091`.
- Surface passed, and depth history after the surface still showed the terrain depth value, consistent with depth read/no depth write.

CK3 therefore keeps the surface stably in front of terrain by about `0.003` in depth for these sampled pixels while preserving the terrain depth buffer.

---

## Findings

### Finding 1: CK3 Does Not Solve This By Carving Terrain

The target capture draws terrain normally before the surface pass. The surface then draws over it.

### Finding 2: CK3 Does Not Use Surface `SV_Depth`

The surface PS has only `SV_Target`. The depth relationship comes from geometry/projection/rasterizer state, not from a custom pixel depth output.

### Finding 3: Current Surface Was Too Close To Terrain Depth

Current river surface and terrain differ by only `1e-5` scale in the problematic area, so tiny interpolation/slope differences decide whether each fragment passes. That creates hard blocky terrain exposure.

### Finding 4: Mesh Terrain Clamp Was The Wrong Direction

Clamping generated river vertices to sampled terrain height was a plausible first probe but not CK3-equivalent. It also mutates geometry and can disturb the sloped ribbon basis. The clamp and its regression test were removed.

---

## Decisions Made

### Decision 1: Keep Surface DepthRead / No Depth Write

**Decision:** Preserve `DepthStencilStates.DepthRead` for the surface pass.

**Rationale:** CK3 surface overlays terrain while leaving terrain depth in place. Disabling depth entirely would let rivers draw through unrelated foreground geometry.

### Decision 2: Split Bottom And Surface Rasterizer Bias

**Decision:** Bottom keeps the old small bias; surface gets an independent stronger negative rasterizer depth bias:
- bottom: `DepthBias=-1`, `SlopeScaleDepthBias=-1`
- surface: `DepthBias=-512`, `SlopeScaleDepthBias=-4`

**Rationale:** The artifact only needs the surface pass to be stably in front of terrain. Bottom renders to the river/refraction target and should not inherit a larger scene-depth-oriented bias.

### Cleanup: Remove Stale Surface `_BankFade` Binding

**Decision:** Keep `_BankFade` bound only for `RiverBottom`. Surface alpha uses CK3-style depth-based alpha and no longer declares `_BankFade`.

---

## Files Changed

- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
  - Added separate bottom/surface rasterizer bias constants.
  - Split surface rasterizer state from bottom rasterizer state.
  - Surface still uses `DepthStencilStates.DepthRead`.
- `Terrain.Editor/Services/RiverMeshService.cs`
  - Removed the temporary `LiftAboveTerrain` clamp from `BuildRiverMesh`.
- `Terrain.Editor.Tests/Program.cs`
  - Removed the temporary terrain-clamp regression test and reflection helpers.
- `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`
  - Added a regression test proving surface rasterizer bias is stronger than bottom.
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

---

## Verification

- `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`
  - Passed.
  - Warnings only: existing NuGet vulnerability warnings, unused fields/events, and WinForms DPI manifest warning.
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug /p:UseAppHost=false`
  - New/changed river tests passed, including `river surface rasterizer uses stronger depth bias than bottom`.
  - Final failure remains the pre-existing repository hygiene test: tracked files still exist under `game/map/...`.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh frame after the surface depth-bias change.
2. Re-check the previously failing pixel `(1180,640)` or the same visual location.
3. Confirm river surface draw no longer reports `depthTestFailed` while terrain depth remains preserved after the surface pass.

### What To Avoid
- Do not reintroduce per-vertex terrain clamping as the primary fix for this issue.
- Do not disable surface depth testing globally unless a later capture proves foreground occlusion is handled elsewhere.

