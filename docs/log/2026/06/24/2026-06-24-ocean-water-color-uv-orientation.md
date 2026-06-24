# Ocean water_color UV orientation
**Date**: 2026-06-24
**Session**: Ocean CK3 parity follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix the updated `C:\Users\Redwa\Desktop\debug.rdc` issue where Ocean `water_color` was visibly reversed north/south.

**Success Criteria:**
- Verify the axis mismatch in RenderDoc before editing shader source.
- Keep the change Ocean-only: no River, shared refraction capture, global lighting, or tonemap changes.
- Add a shader text regression so CK3's source-level `1 - v` map UV is not mechanically reintroduced for Ocean.

---

## Context & Background

Earlier Ocean work moved closer to CK3's `CalcWater` path but copied the map-space UV convention too literally. CK3's shader uses `1.0 - WorldSpacePos.z / MapSize.y` for its own resource/world orientation, while the local Terrain `water_color.dds` already matches Terrain world-Z orientation.

The user updated `debug.rdc` and reported that the color was still far from CK3 and that `water_color` was reversed.

---

## What We Did

### RenderDoc Hot Replacement

Opened the updated capture:
- `C:\Users\Redwa\Desktop\debug.rdc`
- Ocean draw: `EID 280`
- Final output: `EID 1099`

Confirmed `EID 280` binds:
- `WaterColorTexture_id110` at `t2`
- `OceanTextureSampler_id119` at `s2`
- `_MapWorldSize = (18431, 9215)`

Hot-replaced the Ocean pixel shader with direct `WaterColorTexture` diagnostic outputs:
- Current source path, Y flipped: `tmp/renderdoc/debug-watercolor-flipy.png`
- Corrected path, no Y flip: `tmp/renderdoc/debug-watercolor-noflip.png`

The no-flip diagnostic restored the local north/south placement of shallow/deep water regions. The user clarified the mismatch was specifically north/south, so X-axis variants were not pursued.

### Shader Change

**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

`ComputeMapWorldUv` now returns:

```hlsl
return worldPosition.xz / max(_MapWorldSize, float2(1.0f, 1.0f));
```

The old Ocean-only `worldUv.y = 1.0f - worldUv.y;` has been removed. River surface remains unchanged and can still follow its own CK3-aligned water-color path.

### Regression Test

`OceanShaderTextTests` now locks:
- The comment documenting local `water_color` world-Z orientation.
- Direct `worldPosition.xz / _MapWorldSize` sampling.
- Absence of `worldUv.y = 1.0f - worldUv.y;` in `OceanSurface.sdsl`.

---

## Decisions Made

### Ocean Does Not Copy CK3's `1 - v` Water Map UV

**Context:** CK3's shader source flips map-space Y, but the local imported `water_color.dds` and Terrain world coordinates do not share CK3's exact resource orientation.

**Decision:** Ocean samples `water_color` in local Terrain orientation and does not invert Y.

**Rationale:** RenderDoc hot replacement showed the literal CK3 flip creates the reported north/south reversal in the updated `debug.rdc`.

**Trade-offs:** This is a resource-orientation adaptation, not a departure from the broader CK3 water model. Future CK3 source comparisons must separate algorithmic behavior from asset-coordinate conventions.

---

## What Worked ✅

1. **RenderDoc shader replacement before source edits**
   - Direct `WaterColorTexture` output isolated the issue from lighting, refraction, tone mapping, and display response.

2. **Minimal shader edit**
   - Fix is restricted to Ocean map UV generation and reused by both the main water-color sample and refraction-space water-color resample.

---

## Verification

Commands run:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
git diff --check
```

Results:
- Shader key generation passed.
- `StrideCleanAsset` passed.
- `StrideCompileAsset` passed.
- `Terrain.csproj` build passed with existing NuGet vulnerability warnings and existing nullable warning in `TerrainRenderFeature.cs`.
- `Terrain.Editor.Tests` passed.
- `git diff --check` reported no whitespace errors; only LF/CRLF normalization warnings.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh runtime frame after this source change to verify final Ocean color placement in the real shader, not only the direct `water_color` diagnostic.
2. Continue CK3 parity work from lighting/refraction/post-chain differences once regional water-color placement is correct.

### Gotchas
- Do not mechanically copy CK3's map UV flip into Ocean. Validate resource orientation per local asset path.
- The RenderDoc diagnostic PNGs under `tmp/renderdoc/` are local scratch artifacts and should not be committed.
