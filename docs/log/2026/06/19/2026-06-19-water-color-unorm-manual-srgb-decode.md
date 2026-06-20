# WaterColorTexture UNORM Import With Manual RGB Decode
**Date**: 2026-06-19
**Session**: River rendering texture parity follow-up
**Status**: ⚠️ Superseded on 2026-06-20
**Priority**: High

---

## Superseded Note

2026-06-20 后续改动把 bottom/water DDS 全部绕过 Stride 内容系统，改为从 `game/map/water` 直接 `Texture.Load`。最新实现按用户复核把 `water_color.dds` 设为 `loadAsSrgb:false`，即 UNorm/linear view；`RiverSurface` 仍不做手动 `DecodeWaterColorSrgb`，避免把诊断 decode 路径留在正式 shader 中。

## Session Goal

**Primary Objective:**
- Explain why `C:\Users\Redwa\Desktop\debug3.rdc` still showed a darker `WaterColorTexture` than CK3 after disabling BC3 recompression, then apply the smallest source-side workaround.

**Success Criteria:**
- Avoid changing global postprocessing.
- Preserve `water-color.dds` RGB bytes more closely than the Stride sRGB import path.
- Keep the alpha/spec channel untouched.
- Rebuild Stride assets and run tests.

---

## Context & Background

**Previous Work:**
- `2026-06-19-water-color-linear-import-test.md`
- `2026-06-19-water-color-disable-stride-bc3-recompression.md`
- `2026-06-19-stride-linear-srgb-output-chain-investigation.md`

**Current State:**
- Source DDS is identical to CK3's `watercolor_rgb_waterspec_a.dds`.
- `debug3.rdc` imported the texture as `R8G8B8A8_SRGB`, not BC3, but it was still darker than CK3's `BC7_SRGB`.

---

## What We Found

RenderDoc MCP confirmed:
- `debug3.rdc` `ResourceId::549`: `R8G8B8A8_SRGB`, max RGB `[0.194, 0.102, 0.104]`, alpha max `0.592`.
- `ck3-river.rdc` `ResourceId::46073`: `BC7_SRGB`, max RGB `[0.508, 0.209, 0.262]`, alpha max `0.592`.

This rules out BC3 compression as the remaining primary cause. The RGB is already darker after Stride's `UseSRgbSampling:true` + decompression/import path. Alpha stayed identical, matching the hypothesis that only RGB color-space handling was wrong.

---

## What We Did

### 1. Imported WaterColorTexture as UNORM and Uncompressed
**Files Changed:** `Terrain.Editor/Assets/River/Water/water-color.sdtex`

```yaml
IsCompressed: false
Type: !ColorTextureType
    UseSRgbSampling: false
```

**Rationale:**
- `IsCompressed:false` avoids Stride BC3 recompression.
- `UseSRgbSampling:false` avoids the Stride sRGB import path that produced dark RGB.

### 2. Manually Decoded Only RGB in RiverSurface
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

Added `SampleWaterColorTexture`, which samples UNORM bytes and applies an sRGB-to-linear approximation to `rgb` only. Alpha is not decoded because it stores water spec/gloss data.

### 3. Locked the Behavior With Text Tests
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

Tests now require:
- `water-color.sdtex` uses `UseSRgbSampling:false`.
- `water-color.sdtex` keeps `IsCompressed:false`.
- `RiverSurface` samples through `SampleWaterColorTexture`.
- RGB is manually decoded through `DecodeWaterColorSrgb`.

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
- Asset compile succeeded: `913 succeeded, 0 failed`.
- Existing shader warning remains: `X3557 loop doesn't seem to do anything`.
- Tests passed with exit code `0`.
- NuGet vulnerability warnings remain unrelated to this change.

---

## Next Session

### Immediate Next Steps
1. Run the editor and capture a new `debug.rdc`.
2. Confirm `WaterColorTexture` is now `R8G8B8A8_UNORM` or otherwise no longer `R8G8B8A8_SRGB`.
3. Compare exported texture RGB against CK3. Expected result: PNG/displayed bytes should be much closer because Stride no longer pre-darkens RGB.
4. Inspect surface pass output. If water is still too dark after this, the remaining issue is downstream surface composition/refraction, not the imported texture bytes.

### Gotchas
- Do not revert `water-color.sdtex` to `UseSRgbSampling:true` unless testing a custom import path; that path has been directly observed to darken RGB.
- Do not apply this pattern to normal terrain albedo textures by default. This is a specific workaround for CK3's BC7 source bytes plus SRGB view behavior.

---

## Quick Reference for Future Claude

**Key implementation:**
- `Terrain.Editor/Assets/River/Water/water-color.sdtex`
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Critical decision:**
- Store WaterColorTexture as UNORM/uncompressed in Stride content and manually decode RGB in the river shader, preserving alpha.
