# River Mesh Bilinear Height Sampling
**Date**: 2026-06-21
**Session**: 5
**Status**: Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Fix visible river surface stair-stepping on rugged terrain after adaptive mesh density was accepted.

**Success Criteria:**
- Keep adaptive mesh density unchanged.
- Remove terrain-height quantization caused by nearest-neighbor height sampling.
- Add a regression test that fails on nearest-neighbor sampling and passes with smooth height interpolation.

---

## Context & Background

The previous session changed river centerlines to re-sample terrain height after smoothing and interpolation. That fixed stale Y values after XZ movement, but the underlying `SampleTerrainHeight` method used `Math.Round(wx/wz)` and sampled a single heightmap texel. On rugged terrain, dense river samples could therefore share one texel height and then jump to the next texel height, forming visible stair steps.

---

## What We Did

### 1. Reproduced The Height Quantization
**Files Changed:** `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`

- Updated the non-flat terrain regression test so expected river centerline height is bilinear at the final smoothed XZ coordinate.
- Verified RED: nearest-neighbor production sampling failed with max height error `8.680`.

### 2. Fixed Terrain Height Sampling
**Files Changed:** `Terrain.Editor/Services/RiverMeshService.cs`

- Replaced nearest-neighbor height sampling with bilinear interpolation over the cached ushort heightmap.
- Kept world coordinate mapping unchanged: world X/Z remain 1:1 with heightmap pixel coordinates.
- Kept adaptive Catmull-Rom sampling density unchanged.

**Rationale:**
- The stair-step artifact came from quantized height sampling, not mesh density.
- Bilinear sampling keeps the river on the terrain surface while avoiding abrupt per-pixel Y jumps.

---

## Verification

Command run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug
```

Result:
- Passed.
- Existing NuGet vulnerability warnings remain.
- Existing project/compiler warnings remain.

---

## Architecture Impact

- River mesh centerline heights now use bilinear terrain height sampling.
- `docs/ARCHITECTURE_OVERVIEW.md` and `docs/CURRENT_FEATURES.md` were updated.
- No ADR was created; this is a scoped sampling-quality fix inside existing river mesh generation.

---

## Quick Reference for Future Claude

**What Changed Since Last Doc Read:**
- `RiverMeshService.SampleTerrainHeight` no longer rounds to the nearest heightmap texel.
- The non-flat river centerline test now locks bilinear height sampling.

**Gotchas:**
- If a river still looks too bumpy after this, the next hypothesis is longitudinal river-profile smoothing or grade limiting, not more mesh density.
