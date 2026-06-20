# WaterColorTexture Direct DDS SRGB View
**Date**: 2026-06-20
**Session**: River rendering texture import follow-up
**Status**: ⚠️ Superseded on 2026-06-20
**Priority**: High

---

## Superseded Note

当前实现已按用户复核改为 `water_color.dds` 使用 UNorm/linear view 加载：`Texture.Load(..., loadAsSrgb:false)`。本日志记录的是已废弃的 sRGB-view 尝试，不再代表当前 intended path。

## Session Goal

**Primary Objective:**
- Finish the migration after all river water resources were moved off Stride content assets and are now loaded directly from `game/map/water`.

**Success Criteria:**
- `water_color.dds` should match CK3's BC7 SRGB sampling model.
- `RiverSurface` must not manually sRGB-decode water color after the resource is loaded as an sRGB texture.
- Stride shader generated files, asset build, and tests must pass.

---

## Context & Background

Previous session found that Stride `.sdtex` sRGB import produced a darker `WaterColorTexture` than CK3. A temporary workaround loaded water color as UNORM and manually decoded RGB in `RiverSurface`.

That workaround became obsolete once bottom/water DDS resources were moved to direct file loading through `RiverResourceLoader`. Stride runtime `Texture.Load(stream, loadAsSrgb:true)` uses `Image.Load` and `ConvertFormatToSRgb`, which changes the texture format/view to sRGB without TextureTool recompression.

---

## What We Did

### 1. Loaded WaterColor DDS as SRGB
**Files Changed:** `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`

Changed:
```csharp
WaterColor = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, WaterColorFileName, loadAsSrgb: false);
```

This should create a GPU texture equivalent to CK3's `BC7_SRGB` resource when the source DDS is BC7.

### 2. Removed Manual Shader Decode
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

Removed the temporary `DecodeWaterColorSrgb` / `SampleWaterColorTexture` path and restored direct sampling:
```hlsl
WaterColorTexture.Sample(WaterColorSampler, uv)
```

This avoids double sRGB decode because the SRGB texture view already performs hardware decode.

### 3. Updated Text Tests
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

Tests now require:
- `WaterColor` local DDS loading uses `loadAsSrgb:true`.
- `RiverSurface` samples `WaterColorTexture` through `WaterColorSampler`.
- `RiverSurface` does not contain `DecodeWaterColorSrgb`.

---

## Verification

Ran:
```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
dotnet test Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug
```

Results:
- Generated shader file update target succeeded.
- Asset compile succeeded: `889 succeeded, 0 failed`.
- Existing shader warning remains: `X3557 loop doesn't seem to do anything`.
- Tests passed with exit code `0`.
- NuGet vulnerability warnings remain unrelated.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh `debug.rdc`.
2. Confirm `WaterColorTexture` is `BC7_SRGB`, not `BC3_*`, `R8G8B8A8_*`, or `BC7_UNORM`.
3. If surface is still dark, investigate downstream refraction/surface composition rather than import color-space.

---

## Quick Reference for Future Claude

**Current intended path:**
- River bottom/water DDS: direct local file load from `game/map/water`.
- `water_color.dds`: `Texture.Load(..., loadAsSrgb:false)`.
- `RiverSurface`: no manual RGB decode.
- Reflection cubemap still remains a Stride content asset.
