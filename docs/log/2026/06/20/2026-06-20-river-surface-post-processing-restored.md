# River Surface Post Processing Restored
**Date**: 2026-06-20
**Session**: River surface deterministic wrapper restore
**Status**: ✅ Complete
**Priority**: High

> Superseded on 2026-06-20 by `2026-06-20-river-surface-debug4-wrapper-fully-restored.md`: the source no longer keeps procedural cloud disabled. It has been restored to the full `debug4.rdc` wrapper, including `GetCloudShadowMask`, `_InverseWorldSize`, `_HasCloudShadowEnabled`, and `cloudMask * 0.8f`.

---

## Session Goal

**Primary Objective:**
- Restore `ApplySurfacePostProcessing` after the wrapper-removal experiment made the water surface darker.
- Keep the procedural cloud shadow removal from the previous RenderDoc diagnosis.

**Success Criteria:**
- `RiverSurface` calls `ApplySurfacePostProcessing` after `CalcRiverAdvanced`.
- Terrain shadow tint and map distance fog are active again.
- `GetCloudShadowMask`, `_HasCloudShadowEnabled`, `_InverseWorldSize`, and `cloudMask * 0.8f` remain absent.
- Temporary `_WaterColorSurfaceLift` / `visibleWaterColor` source changes are removed.
- Stride shader keys/assets compile.

---

## Context & Background

`debug3.rdc` and `debug4.rdc` showed that procedural cloud shadow used `_GlobalTime` and caused repeated terrain visibility toggles to produce different river colors. The next isolation step removed the whole `ApplySurfacePostProcessing` wrapper. `debug5.rdc` then showed the direct `CalcRiverAdvanced` path could become fully black/darker than before, despite water-color sampling being present.

The current decision is to restore the deterministic wrapper pieces and remove only the time-varying procedural cloud path.

---

## What We Did

### 1. Restored The Deterministic Surface Wrapper
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- Restored `ApplySurfacePostProcessing`.
- Restored editor terrain height slice declarations and terrain-normal tint helpers.
- Restored `ShadowNoiseTexture`, `ShadowNoiseSampler`, and `TerrainHeightSampler`.
- Restored map distance fog without strategy-layer fog-of-war.
- Rechecked `C:\Users\Redwa\Desktop\debug4.rdc` and confirmed its `RiverSurface` PS was not the simplified wrapper:
  - surface draws: event `149` and `176`
  - pixel shader: `ResourceId::7793`
  - shader hash: `e0bd8d6f-17ae1378-c5f4c48c-762d934a`
  - it used `_MapSadowTintNoiseUVTiling`, `_MapSadowTintStrength`, `_MapSadowTintThresholdMin/Max`, `_TerrainSunnySunDir`, `ApplyOvercastContrast`, `_FogBegin2/_FogEnd2/_FogMax`, `_FogColor`, `_RelativeFogColor`, and relative height/noise fog modulation.
- Replaced the temporary simplified normal-y tint and `smoothstep(4500, 11000)` fog approximation with that debug4-style parameter chain.
- `PSMain` now computes:

```hlsl
float4 waterColor;
CalcRiverAdvanced(waterColor);
waterColor = ApplySurfacePostProcessing(waterColor, streams.PositionWS.xyz);
streams.ColorTarget = waterColor;
```

### 2. Kept Procedural Cloud Shadow Disabled

**Implementation:**
- `ApplySurfacePostProcessing` now uses `const float cloudMask = 0.0f`.
- The shader still rejects:
  - `GetCloudShadowMask`
  - `_HasCloudShadowEnabled`
  - `_InverseWorldSize`
- `cloudMask * 0.8f`
- The old `debug4.rdc` shader still had those procedural cloud symbols; current source intentionally does not.

### 3. Removed The Temporary Water-Color Lift

**Implementation:**
- Removed `_WaterColorSurfaceLift`.
- Removed `visibleWaterColor`.
- Removed the `max(waterColor, waterColorAndSpec.rgb * lift * waterFade)` floor.

`debug5.rdc` remains useful evidence that direct output was too dark, but that experiment should not stay in source once the wrapper is restored.

### 4. Restored Resource Documentation

**Files Changed:**
- `Terrain.Editor/Assets/River/README.md`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- Restored `game/map/water/shadow_color.dds` as a required local water resource.
- Updated current-status docs to say the wrapper is restored deterministically, not removed.
- Marked older wrapper-removal notes as superseded.

---

## Verification

Ran:

```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug /p:UseAppHost=false
```

Result:
- Shader key generation succeeded.
- Stride asset compile succeeded: `889 succeeded`, `0 failed`.
- `Terrain.Editor` build succeeded.
- River shader tests passed, including the deterministic post-wrapper test and the debug4-style shadow/fog parameter-chain assertions.
- Full test run still fails only on the existing repository hygiene assertion `repository does not track any game files`, because `git ls-files game` returns tracked `game/map/...` resources.

---

## Architecture Impact

- River surface again has a post-wrapper after `CalcWater`.
- The editor wrapper now matches the debug4-style terrain shadow tint and map distance fog chain more closely, but remains intentionally not full CK3 parity because procedural cloud shadow is disabled for deterministic capture comparison.
- Strategy-layer FOW remains absent from river surface.
- If editor terrain height slices are unavailable, `SliceCount=0` makes the wrapper fall back to an up normal rather than skipping the surface draw.

---

## Next Session

1. Capture a fresh frame and verify the restored wrapper output with `cloudMask=0`.
2. Decide whether to untrack existing `game/map/...` files or relax the repository hygiene test for this branch.
3. Continue comparing current refraction/source payloads against CK3 once surface wrapper behavior is stable.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Current source restores `ApplySurfacePostProcessing`.
- Do not reintroduce procedural cloud shadow into `RiverSurface.sdsl`.
- Do not reintroduce `_WaterColorSurfaceLift`; it was only a diagnostic experiment after removing the wrapper.
- `shadow_color.dds` is again required because the restored terrain shadow tint samples it.
