# River Surface Debug4 Wrapper Fully Restored
**Date**: 2026-06-20
**Session**: Restore river surface to `debug4.rdc`
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Restore `RiverSurface` post processing to the shader behavior captured in `C:\Users\Redwa\Desktop\debug4.rdc`.

**Success Criteria:**
- `ApplySurfacePostProcessing` keeps the debug4 terrain shadow tint, procedural cloud shadow, cloud tint, alpha fades, and map distance fog.
- `_InverseWorldSize` and `_HasCloudShadowEnabled` exist in generated shader keys and are bound from `RiverRenderFeature`.
- Stride shader key generation, asset compile, C# build, and river shader tests pass.

---

## Context & Background

Earlier on 2026-06-20, `debug3.rdc` and `debug4.rdc` showed that procedural cloud shadow can make the same river area appear darker or lighter as `_GlobalTime` advances. We temporarily removed cloud shadow and then temporarily removed the entire wrapper. `debug5.rdc` showed direct `CalcRiverAdvanced` output was darker, so the wrapper had to remain.

The user then asked to fully restore to `debug4.rdc`, not the deterministic `cloudMask=0` variant.

RenderDoc MCP was available this session. Reopening `debug4.rdc` confirmed:
- API: D3D11
- Draws/events: `62` / `72`
- River surface PS at event `149`: `ResourceId::7793`
- Shader hash: `e0bd8d6f-17ae1378-c5f4c48c-762d934a`
- The PS contains `GetCloudShadowMask`, `_HasCloudShadowEnabled`, `_InverseWorldSize`, `_GlobalTime * 0.01f`, and `cloudMask * 0.8f`.

---

## What We Did

### 1. Restored Debug4 Cloud Shadow
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- Reintroduced `_InverseWorldSize` and `_HasCloudShadowEnabled`.
- Reintroduced `Levels`, `Overlay`, `GetCloud`, and `GetCloudShadowMask`.
- Changed post processing from `const float cloudMask = 0.0f` to:

```hlsl
float cloudMask = GetCloudShadowMask(worldPosition.xz);
color.rgb = ApplyTerrainShadowTintWithClouds(color.rgb, worldPosition.xz, cloudMask, 1.0f);
color.rgb = lerp(color.rgb, float3(0.0f, 0.01f, 0.02f), cloudMask * 0.8f);
```

`_WaterColorSurfaceLift` and `visibleWaterColor` remain absent.

### 2. Bound Cloud Parameters From C#
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- `ApplySurfaceParameters` binds `_InverseWorldSize` from `riverObject.MapWorldSize` as a fallback and explicitly enables `_HasCloudShadowEnabled`.
- `TryBindEditorTerrainInputs` overrides `_InverseWorldSize` with the terrain heightmap world size when editor terrain slices are available.

### 3. Updated Tests And Generated Keys
**Files Changed:**
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `Terrain.Editor/Effects/RiverSurface.sdsl.cs`

**Implementation:**
- Replaced the deterministic wrapper test with `river surface shader applies debug4 post wrapper`.
- Assertions now require `GetCloudShadowMask`, `_InverseWorldSize`, `_HasCloudShadowEnabled`, `_GlobalTime * 0.01f`, and `cloudMask * 0.8f`.
- `StrideAssetUpdateGeneratedFiles` regenerated `RiverSurface.sdsl.cs`; it now contains both new keys.

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
- `Terrain.Editor` build succeeded with existing warnings only.
- River shader/effect compiler tests passed, including `river surface shader applies debug4 post wrapper`.
- Full test run still fails only on the existing repository hygiene assertion `repository does not track any game files`, because `git ls-files game` returns tracked `game/map/...` resources.

---

## Architecture Impact

- River surface is now intentionally time-phase sensitive again, matching `debug4.rdc`.
- Strategy-layer fog-of-war remains absent from river surface.
- Future A/B captures that toggle terrain visibility must freeze or record `_GlobalTime`, otherwise procedural cloud phase can masquerade as a terrain visibility regression.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Current source is no longer the deterministic `cloudMask=0` wrapper.
- Full debug4 wrapper is restored, including procedural cloud shadow and cloud tint.
- Do not remove `ApplySurfacePostProcessing`; `debug5.rdc` showed that direct `CalcRiverAdvanced` output is too dark.
- Do not reintroduce `_WaterColorSurfaceLift`; it was only a diagnostic hot-edit idea.

**Gotchas:**
- If the user compares two captures taken at different times, cloud shadow may differ even when terrain inputs are unchanged.
- The known unrelated test failure is repository hygiene for tracked `game/map/...` files.
