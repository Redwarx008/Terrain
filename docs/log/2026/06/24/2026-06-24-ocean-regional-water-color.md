# Ocean 远景区域水色响应修正
**Date**: 2026-06-24
**Session**: Ocean CK3 far-map comparison follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 解释并修正 CK3 远景海洋在不同区域呈现不同深浅/色相的问题，避免继续用单一全局 shallow/deep 颜色机械调参。

**Success Criteria:**
- 明确 CK3 shader 如何产生区域海色差异。
- 保持 Ocean-only，不引入 CK3 province/FOW/flatmap/surround 策略层分支。
- 让本项目已有 `game/map/water/water_color.dds` 的区域水色影响最终 Ocean 显示响应。
- 通过 shader key 生成、Stride asset 编译、runtime build 和 editor tests。

---

## Context & Background

**Previous Work:**
- `2026-06-24-ocean-debug1-detail-response.md` 把 Ocean 从偏黑修到接近 CK3 final 深水均值，但区域色差仍被全局 display response 压平。

**Current State:**
- 用户提供 `C:\Users\Redwa\Desktop\ck3-ocean远.rdc` 和 CK3 远景截图，指出 CK3 不同海域的颜色深度不同。
- 本地 Ocean 已采样 `WaterColorTexture`，但 `ApplyOceanDisplayResponse` 仍使用全局 deep/shallow 目标，没有接收区域贴图颜色。

---

## What We Did

### 1. RenderDoc 复核 CK3 远景水面
**Files Changed:** none

**Findings:**
- CK3 远景主水面 draw 为 EID 2668，PS 绑定：
  - `WaterColorTexture_Texture(t6)`
  - `FogOfWarAlpha_Texture(t17)`
  - `FlatMapTexture_Texture(t18)`
  - province/border/pattern 等策略层纹理
- 该 draw 直接用地图 UV 采样 `WaterColorTexture`，同时计算基础 shallow/deep 水色。
- 本帧 cbuffer 中 `_WaterZoomedInZoomedOutFactor=1.0`、`_WaterRefractionScale=0.0`，因此远景主要保留 water color map 路径，而不是通过折射深度生成区域色。
- EID 2843/2852 后续还用 `SurroundMask`、`BlackMask`、cloud 等 mask 压暗地图边缘/未探索区域。

### 2. 检查本地 water color map
**Files Changed:** generated preview files under `tmp/`

**Findings:**
- 本地 `game/map/water/water_color.dds` 尺寸为 `4608x2304`，包含清晰的全地图区域色差。
- RGB 以 sRGB texture 采样进 shader 后的线性均值约为 `[0.036, 0.056, 0.052]`，适合作为区域调色参考点。
- 该贴图不能直接替换最终颜色，否则会重新把 Ocean 压暗；更合适的是做保亮度的色相/区域亮度调制。

### 3. Source changes
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Implementation:**
```hlsl
float3 ApplyOceanRegionalDisplayTint(float3 displayColor, float3 regionalWaterColor)
{
    float3 luma = float3(0.2126f, 0.7152f, 0.0722f);
    float displayLum = max(dot(displayColor, luma), 0.0001f);
    float regionalLum = max(dot(regionalWaterColor, luma), 0.0001f);
    float referenceLum = max(dot(_OceanDisplayRegionalReference, luma), 0.0001f);
    float3 regionalDisplayColor = regionalWaterColor * (displayLum / regionalLum);
    float brightnessRatio = clamp(regionalLum / referenceLum, 0.65f, 1.35f);
    regionalDisplayColor *= lerp(1.0f, brightnessRatio, saturate(_OceanDisplayRegionalBrightnessInfluence));
    return lerp(displayColor, regionalDisplayColor, saturate(_OceanDisplayRegionalColorInfluence));
}
```

**New defaults:**
- `_OceanDisplayRegionalReference = float3(0.036f, 0.056f, 0.052f)`
- `_OceanDisplayRegionalColorInfluence = 0.55f`
- `_OceanDisplayRegionalBrightnessInfluence = 0.45f`

---

## Decisions Made

### Use Water Color Map Regional Tint, Not CK3 Strategy Layers
**Context:** CK3 far map color variation is not only water shader math; it also includes FOW/surround/flatmap/province-adjacent strategy layer inputs.

**Decision:** Use only the shared water color map information already present in this project, and keep province/FOW/flatmap/surround out of Ocean.

**Rationale:**
- It matches the reusable part of CK3's mechanism without pulling strategy-view rendering into the close-up Ocean pass.
- It avoids changing River, shared refraction capture, scene lighting, or global post.

**Trade-offs:**
- This will not reproduce CK3's unexplored-region darkening or map-edge mask exactly.
- A future strategic/far-map renderer can implement those layers separately.

---

## What Worked ✅

1. **RenderDoc draw-level isolation**
   - EID 2668 showed regional water color already before final post, proving this is not just tonemap correction.

2. **Luminance-preserving chroma transfer**
   - The shader can take `water_color.dds` hue while preserving the previous display response brightness calibration.

---

## Code Quality Notes

### Testing
- Updated `OceanShaderTextTests` to lock:
  - regional display parameters
  - `ApplyOceanRegionalDisplayTint`
  - luminance-preserving color transfer
  - bounded regional brightness ratio
  - `waterColorAndSpec.rgb` feeding `ApplyOceanDisplayResponse`
  - continued ban on province/FOW/flatmap/`_WaterToSunDir`

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
1. Capture a fresh runtime/editor RenderDoc after this build and compare regional ocean colors against `ck3-ocean远.rdc`.
2. If far-map parity is still required, design a separate strategic/far-map water path instead of adding FOW/flatmap/province logic to close-up Ocean.
3. Tune `_OceanDisplayRegionalColorInfluence` / `_OceanDisplayRegionalBrightnessInfluence` from a fresh capture if the map color is too strong or too subtle.

---

## Quick Reference

**What Changed Since Last Doc Read:**
- `ApplyOceanDisplayResponse` now accepts `waterColorAndSpec.rgb`.
- Ocean display response now uses `water_color.dds` for regional hue/depth variation.
- CK3 strategy-layer tokens remain excluded.

**Gotchas for Next Session:**
- CK3 `WaterColorTexture` is only part of the far-map look. FOW/surround/black/cloud masks are separate and should not be silently folded into close-up Ocean.
- Do not directly replace final Ocean color with `water_color.dds`; the texture is too dark in the current Stride HDR/post chain.

