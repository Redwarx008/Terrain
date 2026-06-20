# Remove ApplySurfacePostProcessing
**Date**: 2026-06-20
**Session**: remove wrapper
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Delete `ApplySurfacePostProcessing` from river surface rendering.

**Success Criteria:**
- `RiverSurface` no longer declares or calls the post wrapper.
- C# render binding and resource loading no longer reference wrapper-only textures or parameters.
- Generated shader keys and tests reflect the new boundary.

---

## What We Did

### 1. Removed River Surface Post Wrapper
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl.cs`

Removed `ApplySurfacePostProcessing` plus wrapper-only helpers and parameters:
cloud shadow, terrain shadow tint, map distance fog, editor height slice sampling, `_InverseWorldSize`, `_HasCloudShadowEnabled`, and `_MapSadowTint*`.

`PSMain` now calls `CalcRiverAdvanced(waterColor)` and writes `streams.ColorTarget = waterColor` directly.

### 2. Cleaned Runtime Bindings
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`

Removed river surface editor terrain height-slice binding, shadow tint sampler binding, `_InverseWorldSize` / `_HasCloudShadowEnabled` setup, and `shadow_color.dds` loading.

### 3. Updated Tests and Docs
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/decisions/2026-06-20-remove-river-surface-post-wrapper.md`

Changed the surface wrapper text test from requiring wrapper parity to forbidding wrapper dependencies from returning.

---

## Decisions Made

### Decision 1: Remove Wrapper Instead of Keeping Debug4 Parity
**Context:** The earlier debug4 wrapper restore was semantically closer to CK3, but the `debug4.rdc` hot replacement showed negligible exported image impact for the inspected river draws.

**Decision:** Delete the wrapper and its exclusive resource path.

**Trade-offs:** We lose CK3 cloud/terrain/fog post semantics, but reduce shader complexity, binding complexity, required texture IO, and `_GlobalTime` cloud phase sensitivity.

---

## Testing

- ✅ `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- ✅ `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- ✅ `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- ✅ `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`
- ⚠️ `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug /p:UseAppHost=false`

The test run passed the river shader compile and all river text tests. It still fails at the existing repository hygiene assertion because tracked files remain under `game/`, including `game/map/water/shadow_color.dds`.

---

## Next Session

### Immediate Next Steps
1. If visual parity still looks wrong after wrapper removal, inspect refraction source/timing rather than re-adding `ApplySurfacePostProcessing`.
2. Resolve or relax the existing repository hygiene test for tracked `game/` resources if full test-suite green is required.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `ApplySurfacePostProcessing` is intentionally removed.
- `RiverSurface` now directly writes `CalcRiverAdvanced` output.
- Do not re-add editor height slice bindings or `shadow_color.dds` just to chase debug4 wrapper parity.

**What Changed Since Last Doc Read:**
- Architecture: river surface no longer includes CK3-style cloud/terrain/fog post wrapper.
- Implementation: wrapper-only shader keys were regenerated out of `RiverSurface.sdsl.cs`.
- Constraints: `debug4.rdc` showed the wrapper deletion was visually negligible in the inspected frame, but this is not full CK3 semantic parity.
