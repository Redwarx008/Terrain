# Ocean CK3 CalcWater / Post Validation
**Date**: 2026-06-24
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 回答 CK3 ocean shader 如何避免单一近海颜色，并在动 SDSL 前用 RenderDoc 热替换验证可参考路径。

**Success Criteria:**
- 不重新引入 `_OceanCloseMapface*` 全局近景目标色。
- 不把 water-color map 直接当 final display chroma。
- 区分 CK3 `CalcWater` 本体与后续 mapface/tonemap 对最终截图的贡献。

---

## What We Found

- CK3 `game/gfx/FX/pdxwater.shader` 的 ocean wrapper 调用 `CalcWater(Input, ShadowTerm)` 后，还会经过 province/border overlay、Fog of War、distance fog、flatmap / paper transition。
- CK3 `jomini_water_default.fxh` 的 `CalcWater` 里，近景 diffuse 不是 `WaterColorTexture.rgb` 直出，而是 `_WaterColorDeep/_WaterColorShallow` 按 facing 混合后乘 `_WaterDiffuseMultiplier`。
- `WaterColorTexture.rgb` 主要进入 refraction / underwater see-through 的 water map；远景通过 `_WaterZoomedInZoomedOutFactor` 把 refraction 逐渐退到 water-color-map tint。
- CK3 final post 还可走 `TonyMcMapfaceLUT`，低 raw water 会在最终图被明显抬亮。

---

## RenderDoc Hot Replacement

**Capture:** `C:\Users\Redwa\Desktop\debug1.rdc`

### CK3-like core only

- 替换 Ocean EID 280 为 CK3-like `CalcWater` 结构，不加本地 display response。
- 代表 final EID 1099：
  - `(340,330)` `[0.051,0.082,0.094]`
  - `(640,330)` `[0.086,0.157,0.169]`
  - `(820,670)` `[0.086,0.153,0.165]`
- 导出图：`tmp/renderdoc/debug1-ck3-core-only-hotreplace.png`

**Conclusion:** 只参考 `CalcWater` 本体会和 CK3 raw 一样偏暗；本项目当前 post 不会像 CK3 mapface 那样把它抬到截图量级。

### Approximate final display lift

- 替换本地 final pass EID 1099 为 RenderDoc-only linear display lift，模拟 CK3 final post 对 water 的提亮，不作为源码方案。
- 代表 final EID 1099：
  - `(340,330)` `[0.227,0.345,0.357]`
  - `(640,330)` `[0.263,0.447,0.455]`
  - `(820,670)` `[0.267,0.439,0.447]`
- 导出图：`tmp/renderdoc/debug1-ck3-core-plus-display-lift-hotreplace.png`

**Conclusion:** 显示响应确实能把同一 raw water 拉到 CK3-tw 量级，但全局替换 final pass 会让 terrain 严重过曝，因此不能作为当前项目方案。

---

## Source Changes

**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`

**Implementation:**
- 补齐 CK3 see-through shore mask：
  - `_WaterSeeThroughShoreMaskDepth`
  - `_WaterSeeThroughShoreMaskSharpness`
- `CalcTerrainUnderwaterSeeThrough` 先按 density lerp bottom，再按 shore mask 回到 water map。
- 近景 `CalcOceanWaterColor` 不再按 water color alpha 直接混入 water map；近景 diffuse 保持 shallow/deep/facing。
- `ComputeOceanWaterColorTextureInfluence` 简化为 close `0`、far `_OceanZoomedOutWaterColorTextureInfluence`。
- `CalcRefraction` 使用 CK3-style `pow(1 - zoom, 2)` 衰减，并以 refraction-space water color map 作为 far-zoom fallback。
- 删除已无效的 `_OceanWaterColorTextureInfluence` shader key。

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
- `git diff --check`

All completed successfully. Existing NuGet vulnerability warnings and existing nullable/analyzer warnings remain unrelated.

---

## Next Steps

1. Capture a fresh `debug1.rdc` after restarting editor/runtime to confirm the generated shader is actually resident.
2. Compare final ocean pixels against `ck3-ocean-tw.rdc` and `ck3-cocean-ltaly.rdc`.
3. If color is still too dark, do not add close-map constants; decide explicitly between an Ocean-local CK3 display response and a broader CK3-like post/tonemap path.
