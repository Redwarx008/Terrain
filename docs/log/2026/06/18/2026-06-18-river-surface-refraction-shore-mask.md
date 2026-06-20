# River Surface Refraction Shore Mask
**Date**: 2026-06-18
**Session**: RenderDoc debug2 surface follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Diagnose why `C:\Users\Redwa\Desktop\debug2.rdc` still showed dark river surface/shore despite previous bottom lighting and waterFade fixes.

**Success Criteria:**
- Identify the pass and shader term directly producing the dark pixels.
- Validate the hypothesis with RenderDoc hot replacement before editing SDSL.
- Land the smallest CK3-semantics fix and verify shader asset compilation/tests/build.

---

## What We Did

### RenderDoc diagnosis
**Capture:** `C:\Users\Redwa\Desktop\debug2.rdc`

- Surface draw: EID `305`, output `ResourceId::4057`, refraction input `ResourceId::7769`.
- Refraction chain: EID `248` scene seed writes `7766`, copy to `7769`, EID `276` bottom writes `7769`, EID `305` surface samples `7769`.
- Representative bright-side pixels:
- `(1040,567)` final about `[0.601,0.526,0.421]`, `RiverUV.y≈0.899`.
- `(810,665)` final about `[0.482,0.417,0.330]`, `RiverUV.y≈0.889`.
- Representative dark-side pixels:
- `(1078,583)` final about `[0.016,0.020,0.021]`, `RiverUV.y≈0.184`.
- `(922,623)` final about `[0.017,0.022,0.021]`, `RiverUV.y≈0.182`.

### Hot replacement findings

- `waterFade/depth/foam` diagnostic showed `waterFade=0` on all representative points, so `waterDiffuse`, fresnel and reflection were not contributing.
- `refractionColor` diagnostic matched final output, proving the dark color came from `SampleRefractionSeeThrough`.
- `reflection=0` and `CK3 water colors + reflection=0` variants did not change representative pixels.
- Base vs distorted refraction split showed the direct root:
- Dark side base refraction was medium-bright, about `[0.28,0.25,0.20]`.
- Dark side distorted refraction was nearly black, about `[0.02,0.02,0.02]`.
- `useDistorted=1` selected that black offset sample.
- CK3 source `jomini_water_default.fxh` multiplies refraction offset by `RefractionShoreMask = 1 - saturate((_WaterRefractionShoreMaskDepth - Depth) * _WaterRefractionShoreMaskSharpness)`.
- In this capture the CK3 mask was `0` for representative shallow shore pixels, so CK3 would not offset the refraction sample there.
- Hot replacement with CK3 refraction shore mask raised dark side `(1078,583)` from about `[0.016,0.020,0.021]` to about `[0.235,0.213,0.177]`; bright-side pixels were essentially unchanged.

### Implementation
**Files changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`
- `Terrain.Editor/Rendering/River/RiverRenderObject.cs`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Changes:**
- Added `_WaterRefractionScale`, `_WaterRefractionShoreMaskDepth`, `_WaterRefractionShoreMaskSharpness`, `_WaterRefractionFade` to `RiverSurface`.
- Added `ComputeRefractionShoreMask(...)` and `ComputeRefractionOffset(...)`.
- Surface now samples the undistorted refraction payload first, computes `refractionShoreDepth = min(worldDepth, baseRefractionDepth)`, then multiplies distorted refraction offset by the CK3 shore mask.
- Exposed the new refraction parameters through `RiverRenderSettings -> RiverRenderObject -> RiverRenderFeature`.
- Regenerated `RiverSurface.sdsl.cs`.
- Added text tests to lock the CK3 refraction shore-mask semantics and parameter binding.

**2026-06-18 follow-up correction:** This implementation was only a partial CK3 alignment. The follow-up `debug.rdc` proved the compiled shader still used the old river-local `normalOffset * (0.0025 + depthFactor * 0.0035)` offset basis, which is not CK3-equivalent. The current code now supersedes that by binding `_ViewMatrix` and computing CK3 view-space normal / 1080p refraction offset directly.

---

## Decisions Made

### Superseded: Keep the current offset basis but gate it with CK3 shore mask
**Context:** CK3 computes offset from view-space normal and 1080p normalization. Current Stride shader already has a normalized flow-normal offset that had been tuned into the existing pipeline.

**Decision:** Preserve the existing offset magnitude path, expose `_WaterRefractionScale` as a CK3-compatible scale normalized by `500`, and apply CK3 `_WaterRefractionShoreMask*` / `_WaterRefractionFade` before distorted sampling.

**Rationale:**
- RenderDoc hot replacement proved the missing shore mask is the direct root of the dark pixels.
- Changing the full offset basis at the same time would mix a proven fix with an unverified larger formula change.

**Superseded by:** `RiverSurface` now follows CK3's offset basis: final `waterNormal` -> `_ViewMatrix` -> `float2(-1/1920, 1/1080)` -> `_WaterRefractionScale * RefractionShoreMask * _WaterRefractionFade`. The old normalized offset path and `/500` scale adapter must not be reintroduced.

---

## What Worked

1. **Packed diagnostic replacements**
- Outputting `waterFade`, `refraction`, `base/distorted RGB`, alpha/depth and `useDistorted` isolated the direct term without relying on visual guesses.

2. **Control replacement**
- A constant red PS confirmed RenderDoc shader replacement was actually active after an initially surprising no-op result from water-color/reflection experiments.

---

## What Didn't Work

1. **Water color / cubemap hypothesis**
- `reflection=0` and `CK3 water colors + reflection=0` were no-ops on representative pixels because `waterFade=0` had already zeroed diffuse/fresnel/reflection.
- Lesson: if surface final equals refraction, do not continue tuning water color constants.

---

## Verification

- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`
- `dotnet build Terrain.Editor\Terrain.Editor.csproj -c Debug`

All passed. Remaining warnings were existing NuGet advisory / WinForms DPI / shader loop warnings.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh frame after the CK3 view-space offset build and confirm `RiverSurface` cbuffer includes `_ViewMatrix` plus `_WaterRefractionScale/_WaterRefractionShoreMask*`.
2. Recheck the same dark-side screen region: shallow shore pixels should use zero or near-zero refraction offset, and deeper pixels should use CK3's view-space offset direction and 1080p-normalized magnitude.
3. If the river still differs from CK3, next likely area is the `DecompressWorldSpace` MaxHeight branch or remaining surface color/lighting differences, not the old river-local offset basis.

### Docs to Read Before Next Session
- `docs/log/2026/06/18/2026-06-18-river-surface-refraction-shore-mask.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- In `debug2.rdc`, dark surface pixels were caused by accepted distorted refraction sampling black texels.
- CK3 disables refraction distortion in shallow shore-mask pixels before sampling the offset texture.
- The corrected code exposes CK3 refraction parameters, binds `_ViewMatrix`, and computes the CK3 view-space refraction offset instead of multiplying the old local offset by the CK3 shore mask.

**Gotchas:**
- Do not re-attribute this symptom to `WaterColorShallow/Deep` or cubemap reflection unless a new capture proves `waterFade > 0` and final is no longer refraction-dominated.
- Do not reintroduce `normalOffset * (0.0025 + depthFactor * 0.0035)` or `_WaterRefractionScale / 500`; that was a partial fix and is not CK3-equivalent.
- RenderDoc `get_bindings` may omit resource IDs; use `get_resource_usage` to trace `7769`-style refraction resources.
