# Ocean CK3 Flow and Refraction Fixes
**Date**: 2026-06-24
**Session**: Ocean CK3 shader parity follow-up
**Status**: Complete for source/compile changes; RenderDoc visual recheck pending
**Priority**: High

---

## Session Goal

Use CK3 shader source evidence to decide the shared refraction RGB protocol, port Ocean flow normal interpolation closer to CK3, audit Ocean water DDS color-space loading, and make a conservative Ocean lighting adjustment without integrating CK3 province/border/FOW/flatmap strategy-layer code.

---

## Context & Background

Previous RenderDoc comparison against `C:\Users\Redwa\Desktop\save.rdc` EID 1061 showed the current Ocean refraction input and final output were far brighter than CK3:
- CK3 refraction max RGB around `0.43`, final output max RGB around `0.59`.
- Current refraction max RGB around `1.25`, final Ocean output max RGB above `5`.

Related session:
- `docs/log/2026/06/24/2026-06-24-ocean-ck3-renderdoc-compare.md`

---

## What We Did

### 1. Refraction Capture RGB Protocol

CK3 `jomini_water_default.fxh` samples refraction RGB directly and only uses alpha as the compressed camera-relative world-space distance payload. CK3 source did not contain a matching inverse or companion for `color / (1 + color) * 1.5`.

Implemented:
- Removed `_RefractionCaptureExposure`.
- Removed `_RefractionCaptureColorScale`.
- Removed `CompressRefractionCaptureColor`.
- `WaterRefractionCapture.sdsl` now writes `Texture0.Sample(LinearSampler, uv).rgb` directly and keeps alpha as `RiverCompressWorldSpace(...)`.
- Removed the obsolete key bindings from `WaterRefractionCapturePass`.

Decision:
- Do not keep `scene color -> color / (1 + color) * 1.5` compression for shared water refraction RGB.

### 2. Ocean Flow Normal Interpolation

Ported Ocean flow normal sampling toward CK3 `CalcFlow` / `SampleFlowTexture`:
- FlowMap sampled with `SampleLevel`.
- Flow direction normalized from FlowMap RG.
- Flow normal UV rotated by the CK3 flow inverse matrix.
- FlowNormal sampled with `SampleGrad`; gradients intentionally match CK3 source by being computed from `NormalCoord` before applying the flow inverse rotation.
- Normal Y scaled by `1 / max(0.01, flowMap.b)`.
- Normal XZ rotated back into flow space.
- Four CK3 flow phases are sampled at `floor(flowCoord)`, `+0.5x`, `+0.5y`, `+0.5xy`.
- Blend factor uses CK3 cubic formula `0.5 - 4 * x^3`.
- Removed extra `_WaterFlowNormalSpeed`; `_WaterFlowTime` is used directly.

### 3. Ocean DDS Color-Space Loading

Adjusted only Ocean resource loading:
- sRGB: `water_color.dds`, `foam.dds`, `foam_ramp.dds`.
- linear: `ambient_normal.dds`, `flowmap.dds`, `flow_normal.dds`, `foam_map.dds`, `foam_noise.dds`.

River loading was intentionally left unchanged in this session to avoid widening the visual blast radius beyond Ocean.

### 4. Ocean Lighting Parameters

Made a conservative shader-level lighting convergence:
- Water diffuse lighting now uses `lerp(DeepColor.rgb, ShallowColor.rgb, facing) * _WaterDiffuseMultiplier`.
- It no longer multiplies the lit diffuse color by the sampled water-color texture.
- Gloss now derives from water color/spec alpha through `_WaterGlossScale`, while `_WaterGlossBase` and `_OceanRoughness` remain active material controls.
- `RiverStrideLighting` remains the scene lighting bridge; CK3 province/border/FOW/flatmap and fixed strategy sun inputs are still intentionally not connected.

---

## Files Changed

- `Terrain/Effects/Water/WaterRefractionCapture.sdsl`
- `Terrain/Rendering/Water/WaterRefractionCapturePass.cs`
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Rendering/Ocean/OceanResourceLoader.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `Terrain.Editor.Tests/OceanResourceTextTests.cs`
- `Terrain.Editor.Tests/WaterRenderingTextTests.cs`
- Generated shader keys:
  - `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
  - `Terrain/Effects/Water/WaterRefractionCapture.sdsl.cs`
- Documentation:
  - `docs/ARCHITECTURE_OVERVIEW.md`
  - `docs/CURRENT_FEATURES.md`

---

## Validation

Commands run:
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain.sln --no-restore`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
- `git diff --check`

Result:
- Shader generated files refreshed successfully.
- Stride asset compile passed.
- Solution build passed.
- Editor test suite passed, including OceanSurface and WaterRefractionCapture effect compiler tests.
- `git diff --check` reported no whitespace errors; it only printed existing CRLF normalization warnings.

Scope note:
- This validation proves the source, generated keys, asset compiler, solution build, and text/effect-compiler regressions. A fresh RenderDoc capture is still required before claiming visual parity with CK3 EID 1061.

Known warnings:
- Existing NuGet vulnerability warnings for `Microsoft.Build.Tasks.Core`, `NuGet.Packaging`, `NuGet.Protocol`, `Tmds.DBus.Protocol`.
- Existing nullable/unused field warnings in terrain/editor code.

---

## Next Session

Immediate next steps:
1. Capture a fresh `debug.rdc` after these changes and compare Ocean draw output ranges against CK3 EID 1061 again.
2. Inspect GPU disassembly for Ocean flow normal to confirm the intended `sample_d` / derivative path survived SDSL translation.
3. If Ocean remains too bright, tune `_WaterReflectionIntensity`, `_WaterSpecular`, `_WaterGlossScale`, and scene exposure using RenderDoc pixel history rather than guessing.
4. Consider whether River should also switch shared `water_color.dds` / foam color loading to sRGB, but only after a separate river visual comparison.

---

## Quick Reference

Critical decision:
- Shared water refraction RGB is scene color passthrough; alpha is the only packed distance payload.

Current limitation:
- Ocean still omits CK3 strategy-layer province/border/FOW/flatmap code by user request, so exact parity with CK3 EID 1061 remains limited to core water behavior.
