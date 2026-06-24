# Ocean Close-Mapface Response
**Date**: 2026-06-24
**Status**: ❌ Rejected
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 解释并收敛 `C:\Users\Redwa\Desktop\debug1.rdc` 与 `C:\Users\Redwa\Desktop\ck3-ocean-tw.rdc` 在东亚近海的最终显示色差异。

**Success Criteria:**
- 不机械照搬 CK3 raw water color 参数。
- 不改 shared refraction capture、River、global tonemap、scene light。
- 不接入 CK3 province / FOW / flatmap / `_WaterToSunDir` 策略层。
- 先用 RenderDoc 证明方向，再落地 Ocean-only shader 参数。

---

## What We Found

- 两个参考都不是远景 far-map 路径：本项目 `debug1.rdc` 相机距海面约 `161.5`，低于 `_OceanZoomedOutStartHeight=650`，`ComputeOceanZoomedOutFactor()` 为 `0`；CK3-tw 的 `_WaterZoomedInZoomedOutFactor=0`。
- 本地 Ocean raw 到 final 的 post 提亮较弱，例如 `debug1.rdc` 代表点 `(640,330)` raw 约 `[0.09,0.13,0.16]`，final 约 `[0.13,0.18,0.21]`。
- CK3-tw Ocean raw 同样较低，但 CK3 final post/mapface/LUT 会显著提到青绿水色，例如 `(980,520)` raw `[0.0297,0.1003,0.1032]` 到 final `[0.251,0.467,0.475]`。
- 常量 hot-replace 证明，在当前本地 post 链路下，Ocean raw 约 `[0.20,0.38,0.39]` 才接近 CK3-tw 近海 final 量级。

---

## What We Tried And Rejected

### Ocean shader

**Files Changed:** `Terrain/Effects/Ocean/OceanSurface.sdsl`, `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`

- 曾短暂新增 close-mapface Ocean-only 参数：
  - `_OceanCloseMapfaceDeepBase = float3(0.23, 0.47, 0.48)`
  - `_OceanCloseMapfaceShallowBias = float3(0.18, 0.33, 0.31)`
- `C:\Users\Redwa\Desktop\ck3-cocean-ltaly.rdc` 反证该方案：Italy 近景海色不是东亚近海的统一青绿，代表点 `(420,980)` final 约 `[0.243,0.345,0.361]`，右侧深水 `(1500,760)` final 约 `[0.169,0.153,0.212]`。
- 后续 pixel history 修正了早期判断：EID 490 虽绑定水体资源，但这些代表点的可见海色主要由更早的 terrain/map-water 路径 EID 385 写入，再经 EID 1263 / final post 进入最终图。因此它更强地说明 CK3 的最终近海颜色并非单一 Ocean close-mapface 常量。
- 因此 `_OceanCloseMapface*` 属于会让不同地区近海趋同的全局 trick，已从 `OceanSurface` 撤掉。
- 近景继续保留现有 water composition、lighting、reflection、refraction、flow、foam 和保守 display detail；后续东亚差异应从 water-color/depth/refraction/post 链路继续拆。

### Shader text tests

**Files Changed:** `Terrain.Editor.Tests/OceanShaderTextTests.cs`

- 删除 close-mapface 参数默认值和 `zoomBlend` 混合公式断言。
- 新增禁止 `_OceanCloseMapface` 回归的断言。
- 保持禁止 `ApplyOceanRegionalDisplayTint`、`SanitizeOceanRegionalWaterColor`、`warmReject`、province/FOW/flatmap/`_WaterToSunDir` 回归。

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`

All completed successfully. Existing NuGet vulnerability warnings and existing nullable/analyzer warnings remain unrelated.

---

## Remaining Validation

- 还没有做重启后的 fresh RenderDoc 截帧，因此目前不能声称最终视觉已经完全匹配 CK3-tw。
- 后续 fresh capture 应确认 Ocean shader/cbuffer 不再出现 `_OceanCloseMapface*`，并继续从 CK3 water-color/refraction/post 链路拆差异，而不是重新加入全局近景目标色。
