# River Bottom Blend State Clarification
**Date**: 2026-06-21
**Session**: river bottom blend-state question
**Status**: ✅ Complete
**Priority**: Low

---

## Session Goal

**Primary Objective:**
- Verify why the local river bottom blend state looks different from CK3 and correct the mismatch.

**Success Criteria:**
- Identify the active bottom blend state in `RiverRenderFeature`.
- Check CK3 source/capture evidence for the target bottom blend factors.
- Update current implementation if it differs.

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-ck3-shader-pass-equivalence-analysis.md](../20/../19/2026-06-19-river-ck3-shader-pass-equivalence-analysis.md)
- See: [2026-06-17-river-review-fixes-mapworldsize-cubemap-and-payload-alpha.md](../17/2026-06-17-river-review-fixes-mapworldsize-cubemap-and-payload-alpha.md)
- Related: [stride-river-rendering-patterns.md](../../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- CK3 bottom uses dual-source blending for both RT0 color and RT0 alpha.
- Current code had matched only RGB while keeping RT0 alpha as direct write.

---

## What We Did

### 1. Checked Current Bottom Blend State Against CK3
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- Reviewed `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`.
- Reviewed `Terrain.Editor/Effects/RiverBottom.sdsl`.
- Reviewed CK3 `jomini/gfx/FX/jomini/jomini_river_bottom.fxh`.
- Checked `ck3-river.rdc` and current `debug2.rdc` river draw events with RenderDoc CLI/MCP.

**Rationale:**
- CK3 source declares `SourceBlend = src1_alpha`, `DestBlend = inv_src1_alpha`, `SourceAlpha = src1_alpha`, and `DestAlpha = inv_src1_alpha`.
- Current code used `One/Zero` for RT0 alpha, so the user's observation was correct.

### 2. Matched Bottom Alpha Blend Factors
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- Changed bottom `AlphaSourceBlend` to `Blend.SecondarySourceAlpha`.
- Changed bottom `AlphaDestinationBlend` to `Blend.InverseSecondarySourceAlpha`.

**Rationale:**
- This matches the CK3 bottom blend state exactly at the factor level.

---

## Decisions Made

No new architecture or implementation decisions.

---

## Code Quality Notes

### Testing
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug /p:UseAppHost=false /p:OutDir="artifacts/verify-blend-test/"`
  - Passed with existing warnings only.
- `dotnet Terrain.Editor.Tests/artifacts/verify-blend-test/Terrain.Editor.Tests.dll`
  - River/blend-related tests passed, including `river dual-source blend state matches target color and alpha factors`.
  - Final exit code remained `1` due to known unrelated isolated `OutDir` source-path failures looking for `E:\Terrain.Editor\...`.

### Technical Debt
- Created: none.
- Paid down: none.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `CreateDualSourceBlendState()` now uses `SecondarySourceAlpha / InverseSecondarySourceAlpha` for both RT0 RGB and RT0 alpha.
- The previous `One / Zero` alpha direct-write path was a local divergence from CK3.
- `RiverBottom.sdsl` writes color/depth payload to `streams.ColorTarget` and coverage alpha to `streams.ColorTarget1`.

**Gotchas for Next Session:**
- If edge payload artifacts return, investigate the pre-bottom/refraction payload writer instead of silently diverging the bottom blend state from CK3 again.
- When comparing RenderDoc states, check both RGB and alpha blend factors.

---

## Code References

- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`
