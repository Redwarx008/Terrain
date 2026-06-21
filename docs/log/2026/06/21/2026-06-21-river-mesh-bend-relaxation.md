# River Mesh Bend Relaxation
**Date**: 2026-06-21
**Session**: 3
**Status**: Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Diagnose and reduce overly sharp river mesh bends visible in `C:\Users\Redwa\Desktop\river-mesh.rdc`.

**Success Criteria:**
- Confirm whether the hard turns are already present in GPU mesh data.
- Compare the current mesh style against CK3 river captures.
- Add a regression test and implement a scoped geometry fix.

---

## Context & Background

Recent sessions focused on river lighting, refraction, alpha, depth bias, and segment direction. This session started from a visual mesh issue: some river turns looked too angular even after the rendering/shader path had been brought closer to CK3.

Related docs:
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/decisions/adr-014-river-rendering-architecture.md`
- `docs/design/terrain-editor-design-phase-6.md`

---

## What We Did

### 1. RenderDoc Mesh Diagnosis
**Files Changed:** none

- Opened `C:\Users\Redwa\Desktop\river-mesh.rdc` with RenderDoc MCP.
- Capture summary: D3D11, 80 events, 67 draws, no HIGH severity debug messages.
- Identified river draw pairs:
  - `276/323`: 548 vertices, 546 faces.
  - `290/343`: 632 vertices, 630 faces.
- Exported post-transform mesh JSON and reconstructed centerline samples by left/right vertex pairs.

**Finding:**
- Current GPU centerlines contain repeated internal single-point turns around `60°~77°`.
- The same centerline shape appears in both bottom and surface draw pairs, so the issue originates in CPU mesh generation, not shader composition.

### 2. CK3 Mesh Comparison
**Files Changed:** none

- Opened `C:\Users\Redwa\Desktop\ck3-river.rdc`.
- Capture summary: D3D11, 441 events, 424 draws, no HIGH severity debug messages.
- Exported known CK3 river draws: `332/334` bottom and `460/462` surface.
- Reconstructed centerline samples using the same analysis script.

**Finding:**
- Ignoring endpoint/degenerate samples, CK3 river centerline turns mostly sit around `12°~26°`.
- This confirms the target style is denser and more gradually relaxed than the current generated mesh.

### 3. TDD Fix
**Files Changed:**
- `Terrain.Editor.Tests/Program.cs`
- `Terrain.Editor/Services/RiverMeshService.cs`

**Red Test:**
- Added `centerline smoothing limits repeated river bend angles`.
- The test uses a repeated low-resolution bend pattern and asserts the smoothed centerline stays at or below `30°` max horizontal internal turn.
- Verified red: current implementation failed with `actual max angle 33.69`.

**Implementation:**
- Added `BendRelaxationWeight = 0.25f`.
- `SmoothCenterline` still performs its existing Chaikin pass and preserves the old one-iteration behavior.
- For `iterations >= 2`, it now runs `RelaxRepeatedBends` for `iterations + 1` passes.
- `RelaxRepeatedBends` preserves endpoints and applies a simple neighbor-weighted relaxation to internal points.

**Rationale:**
- This addresses the centerline shape before ribbon generation, so both bottom and surface pass geometry improve together.
- It does not change segment extraction, miter logic, ribbon indices, vertex layout, shader inputs, or render passes.

### 4. Follow-up Tightening
**Files Changed:**
- `Terrain.Editor.Tests/Program.cs`
- `Terrain.Editor/Services/RiverMeshService.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

After visual review, the `30°` regression target was still too loose. The test threshold was tightened to `15°`, which made the previous implementation fail at `26.34°` and then `16.19°` after the first stronger pass. The final follow-up increased relaxation weight to `0.4` and runs `iterations + 3` relaxation passes, keeping endpoint preservation and existing ribbon topology.

### 5. Full Generation Chain Boundary Test
**Files Changed:**
- `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`
- `Terrain.Editor/Services/RiverMeshService.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

Additional visual feedback showed that even the tightened centerline smoothing was still not enough. A new integration-style regression test, `curved river map publishes smooth mesh boundaries`, now drives the real path:

```text
RiverCell[,] -> RiverMeshGenerator -> RiverMapService.ExtractSegments
             -> RiverMeshService.BuildCenterlines -> BuildRiverMesh
```

The first version of the fixture accidentally contained diagonal gaps and produced two segments; after fixing it to a strict 4-neighbor river path, the real failure was visible:

- `CurveSampleSpacing=1.0`: final mesh boundary max angle was about `33°`.
- `CurveSampleSpacing=0.5`: final mesh boundary max angle dropped to `17.44°`, still above target.
- `CurveSampleSpacing=0.25`: final mesh boundary max angle passed the `<=12°` regression target.

This shows the remaining hard corner was not the miter join alone, and not just centerline relaxation. The final Catmull-Rom resampling was too sparse for the low-resolution `rivers.png` stair-step input.

---

## Problems Encountered & Solutions

### Problem 1: Hard Turns Were Not A Shader Artifact
**Symptom:** River turns looked angular in the provided screenshot.
**Root Cause:** Exported GPU mesh centerlines already contained large internal turn angles.
**Solution:** Fix centerline smoothing in `RiverMeshService`, not river shaders.

### Problem 2: CK3 Has Some Large Angles At Degenerate Samples
**Symptom:** Raw CK3 export showed `78°~93°` samples near starts/ends.
**Root Cause:** Those came from near-zero segment lengths and endpoint/cap degenerates.
**Solution:** Compare meaningful non-degenerate internal samples; those mostly stayed around `12°~26°`.

---

## Architecture Impact

- River mesh generation now has an additional post-Chaikin bend relaxation step.
- Follow-up visual feedback tightened the regression target from `30°` to `15°`.
- Full generation-chain testing tightened the final mesh boundary target to `12°` and reduced Catmull-Rom sampling spacing to `0.25`.
- `docs/ARCHITECTURE_OVERVIEW.md` and `docs/CURRENT_FEATURES.md` were updated.
- No new ADR was created because this is a scoped implementation refinement, not a new architecture decision.
- No new learning note was created; the RenderDoc mesh-export workflow is already covered by existing river debugging learnings.

---

## Verification

Command run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug
```

Result:
- Passed.
- Existing NuGet vulnerability warnings and known compiler warnings remain.

---

## Next Session

1. Capture a new river frame after regenerating meshes and compare exported centerline turn-angle stats against this session's `river-mesh.rdc`.
2. Visually inspect whether the new centerline relaxation is enough for long rivers from real `rivers.png`.
3. If hard bends remain, consider adaptive curve resampling before ribbon generation rather than shader-side masking.

---

## Quick Reference for Future Claude

**What Changed Since Last Doc Read:**
- `RiverMeshService.SmoothCenterline` now applies endpoint-preserving bend relaxation when called with two or more iterations.
- A regression test locks repeated bends to `<= 15°` after smoothing.
- `RiverMeshService` now uses `CurveSampleSpacing=0.25` so final generated ribbon boundaries stay below `12°` on a curved river-map fixture.

**Gotchas:**
- Do not diagnose this class of hard river bend as bottom/surface shader lighting first; export mesh and inspect centerline angles.
- CK3 exported meshes include degenerate endpoint samples, so ignore near-zero segment lengths when comparing turn angles.
