# Editor ToneMap Fixed Exposure For River
**Date**: 2026-06-20
**Session**: River RenderDoc exposure follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Diagnose why `C:\Users\Redwa\Desktop\debug1.rdc` with terrain makes river/bottom look much darker than `C:\Users\Redwa\Desktop\debug.rdc` without terrain.

**Success Criteria:**
- Identify whether blackening starts in river bottom, river surface, or later post-processing.
- Apply a scoped editor fix if the root cause is in local code.
- Preserve CK3-style scene light inputs for river bottom.

---

## Context & Background

Recent river work moved bottom/water resources to direct DDS loading, fixed surface alpha, and aligned scene lighting to CK3-style sun/environment intensity `20`. The new comparison showed that enabling terrain made the river and bottom appear almost black.

---

## What We Did

### 1. RenderDoc A/B Diagnosis
**Captures Compared:**
- `debug.rdc`: no terrain
- `debug1.rdc`: terrain visible

**Findings:**
- River draw sequence and shader hashes are unchanged.
- Terrain adds the expected terrain depth/main draws and compute dispatches.
- Matching river events:
  - no terrain: scene `41`, seed `66`, bottom `94/108/122`, surface `163/190/217`
  - with terrain: scene `223`, seed `248`, bottom `276/290/304`, surface `343/370/397`
- Same river-center pixel shows bottom and surface shader outputs are effectively the same in both captures:
  - bottom: about `[0.162, 0.123, 0.080]`
  - surface: about `[0.152, 0.238, 0.224]`
- Divergence happens in the final Stride ToneMap pass.

**Root Cause:**
- With terrain visible, HDR terrain raises `LuminanceAverageGlobal` from about `0.344` to `1.447`.
- Stride automatic exposure drops from about `0.263` to `0.133`.
- The same river input is then tone-mapped from about `[0.196, 0.282, 0.271]` down to `[0.106, 0.165, 0.153]` on the swapchain.

### 2. Fixed Editor ToneMap Exposure
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

Added editor compositor configuration for both asset-loaded and programmatic compositor paths:
- `ToneMap.AutoExposure = false`
- `ToneMap.AutoKeyValue = false`
- `ToneMap.TemporalAdaptation = false`
- `ToneMap.Exposure = -2.0f`

**Rationale:**
- River bottom intentionally reads scene sun/environment at CK3-style intensity `20`.
- Terrain also sees that scene light and can produce bright HDR values.
- The editor viewport should not let terrain visibility change river exposure while debugging river rendering.
- `EV -2.0` maps to linear exposure `0.25`, close to the no-terrain capture baseline `0.263`.

### 3. Regression Coverage
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

Added a text test that locks:
- fixed editor exposure constant
- tonemap configuration on both compositor creation paths
- disabled automatic exposure/key/temporal adaptation

---

## What Worked ✅

1. **Pixel history before shader edits**
   - Comparing bottom/surface shader output before final postprocess prevented another false river-shader fix.

2. **ToneMap trace**
   - Final pass disassembly and trace exposed `LuminanceAverageGlobal`, auto key, and linear exposure directly.

---

## Problems Encountered & Solutions

### Problem 1: River looked darker only when terrain was visible
**Symptom:** river/bottom looked nearly black in `debug1.rdc`.

**Root Cause:** terrain HDR drove Stride AutoExposure down; river draw output itself did not materially change.

**Solution:** fixed editor ToneMap exposure to `-2.0 EV`.

**Why This Works:** fixed exposure keeps the same river input in the same display range regardless of terrain visibility.

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decision
- This is not a new ADR-level decision. It is an editor viewport calibration: river lighting remains scene-driven, but editor final exposure is now stable.

---

## Testing

Ran:
```powershell
dotnet test Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug
```

Result:
- Passed with exit code `0`.
- Existing NuGet vulnerability warnings remain unrelated.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh `debug1.rdc`.
2. Confirm final ToneMap shader no longer has `TAutoExposure=true` behavior and uses fixed exposure around `0.25`.
3. Continue comparing current refraction source/timing against CK3, because pre-bottom/refraction source still differs from CK3's dark `JominiRefraction` payload.

---

## Session Statistics

**Files Changed:** 5
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Do not tune river bottom/surface gain when only final swapchain gets darker with terrain.
- Check river draw shader output first, then final ToneMap exposure.
- Editor fixed exposure lives in `EmbeddedStrideViewportGame.ConfigureEditorToneMap`.

**Gotchas for Next Session:**
- CK3 scene light intensity `20` is still intentionally used for river bottom scene-light inputs.
- Fixed Stride ToneMap exposure improves local editor stability, but does not add CK3 final mapface LUT / ColorCube behavior.

---

## Links & References

### Related Documentation
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

### Related Sessions
- `docs/log/2026/06/20/2026-06-20-water-color-direct-dds-srgb-view.md`
