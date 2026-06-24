# Ocean CK3-like water color map 路径修正
**Date**: 2026-06-24
**Session**: Ocean regional color path correction
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 按用户要求撤掉 warm reject / final chroma transfer 这类 workaround，改成更接近 CK3 的 water color map 参与路径。

**Success Criteria:**
- `WaterColorTexture` 不再直接进入 final display response。
- 不再保留 `ApplyOceanRegionalDisplayTint` 或 `SanitizeOceanRegionalWaterColor`。
- water color map 只在 water/refraction color map 阶段参与，并带 CK3-style tint factor。
- 通过 shader key 生成、Stride asset 编译、build 和 editor tests。

---

## Context & Background

**Previous Work:**
- `2026-06-24-ocean-regional-water-color.md` 引入 final display response 色相转移。
- `2026-06-24-ocean-regional-warm-reject.md` 短暂加入 warm reject，防止英伦/北欧暖棕样本把海面推黄。

**Why Changed:**
- 用户指出 warm reject 是 trick，要求改成类似 CK3 shader 的路径。
- CK3 EID 2668 的 water color map 参与发生在 water composition 里，且有 `_WaterColorMapTintFactor=0.05`，不是把 `WaterColorTexture.rgb` 放大成 final display chroma。

---

## What We Did

### 1. Removed final response regional tint
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`

**Removed:**
- `_OceanDisplayRegionalReference`
- `_OceanDisplayRegionalColorInfluence`
- `_OceanDisplayRegionalBrightnessInfluence`
- `_OceanDisplayRegionalWarmRejectThreshold`
- `_OceanDisplayRegionalWarmRejectScale`
- `ApplyOceanRegionalDisplayTint`
- `SanitizeOceanRegionalWaterColor`
- `ApplyOceanDisplayResponse(finalColor, depth, waterColorAndSpec.rgb)`

### 2. Added CK3-like pre-display water color map path
**Implementation:**
```hlsl
float3 ApplyOceanWaterColorMapTint(float3 waterColorMap)
{
    return lerp(waterColorMap, _WaterColorMapTint, saturate(_WaterColorMapTintFactor));
}

float3 CalcOceanWaterColor(float4 waterColorAndSpec, float facing)
{
    float3 baseWaterColor = lerp(DeepColor.rgb, ShallowColor.rgb, saturate(facing));
    float3 waterColorMap = ApplyOceanWaterColorMapTint(waterColorAndSpec.rgb);
    return lerp(baseWaterColor, waterColorMap, saturate(waterColorAndSpec.a * _OceanWaterColorTextureInfluence));
}
```

**New defaults:**
- `_WaterColorMapTint = float3(0.8324021f, 0.7931101f, 1.0f)`
- `_WaterColorMapTintFactor = 0.05f`

**Refraction path:**
- Refraction world-position water map re-sample now uses the same tint/influence rule:
  - sample `float4 refractionWaterColorAndSpec`
  - apply `ApplyOceanWaterColorMapTint`
  - mix by `refractionWaterColorAndSpec.a * _OceanWaterColorTextureInfluence`

---

## Decisions Made

### Keep WaterColorTexture Out of Final Display Response
**Context:** Final response operates in the current Stride HDR/post chain and is calibrated to visible deep/shallow output. Feeding map RGB directly into it made warm map samples too dominant.

**Decision:** The final display response only receives `finalColor` and `baseRefractionDepth`. Water map data is upstream water-color data only.

**Rationale:**
- Matches CK3's structure more closely.
- Avoids color-specific rejection tricks.
- Keeps the existing Ocean-only boundaries: no shared capture, River, tonemap, scene light, province/FOW/flatmap changes.

**Trade-offs:**
- Regional color variation may be more subtle than the final-response tint attempt.
- Full CK3 far-map parity still requires a separate strategic/far-map path with FOW/surround/flatmap layers.

---

## Code Quality Notes

### Testing
- Updated `OceanShaderTextTests` to lock:
  - `_WaterColorMapTint`
  - `_WaterColorMapTintFactor`
  - `ApplyOceanWaterColorMapTint`
  - `CalcOceanWaterColor`
  - facing-based `DeepColor/ShallowColor` base water color
  - refraction/world water map still sampled
  - final display response no longer consumes `waterColorAndSpec.rgb`
  - removed `ApplyOceanRegionalDisplayTint`, `SanitizeOceanRegionalWaterColor`, `regionalWaterColor`, `warmReject`

### Verification
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`

All passed. Remaining warnings are existing NuGet security warnings plus existing nullable/unused-field warnings.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh editor/runtime RenderDoc and inspect the British/Nordic seas after the CK3-like path change.
2. If regional variation is too subtle, tune `_OceanWaterColorTextureInfluence` from pixel traces rather than reintroducing final display chroma transfer.
3. If full far-map parity is needed, design a separate far/strategy water path for FOW/surround/flatmap instead of adding those branches to close-up Ocean.

---

## Quick Reference

**What Changed Since Last Doc Read:**
- Warm reject workaround was removed.
- Final display response no longer uses water-color texture RGB.
- CK3-like water map tint is now pre-display and upstream of lighting/refraction.

**Gotchas for Next Session:**
- Do not reintroduce `ApplyOceanRegionalDisplayTint` unless the target explicitly changes back to final-response compensation.
- Do not interpret subtle region variation as a bug until a fresh capture confirms the visual gap.

