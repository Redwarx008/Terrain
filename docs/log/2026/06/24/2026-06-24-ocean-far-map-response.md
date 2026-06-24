# Ocean Far-Map Response
**Date**: 2026-06-24
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 复核更新后的 `C:\Users\Redwa\Desktop\debug.rdc` 为什么仍和 CK3 远景海洋有差距，并在不引入 CK3 策略层、不改 shared capture / River / global post 的前提下收敛远景海洋最终色。

**Success Criteria:**
- 先用 RenderDoc 热替换验证方向，再落地 `OceanSurface.sdsl`。
- 远景 Ocean 不再被 refraction/terrain shadow 压成块状深色。
- 保留 water color map 的区域差异，不恢复 regional tint / warm reject trick。

---

## What We Found

- 本地更新后的 `debug.rdc` 中 Ocean EID 280 已经使用 `_OceanZoomedOut*` shader 路径；相机高度约 `10108`，`ComputeOceanZoomedOutFactor()` 饱和为 `1.0`。
- 本地最终 EID 939 仍偏暗。有效 Ocean 水点示例：`(820,860)` raw `[0.163,0.167,0.189]`，final `[0.208,0.208,0.231]`；`(1440,420)` raw `[0.127,0.185,0.205]`，final `[0.169,0.231,0.247]`。
- CK3 `ck3-ocean远.rdc` 的 Ocean raw 更低，但 EID 3063 final post 会明显提亮：`(1810,760)` raw `[0.054,0.083,0.096]` 到 final `[0.231,0.302,0.329]`；`(650,820)` raw `[0.089,0.117,0.133]` 到 final `[0.310,0.365,0.392]`。
- 差距不是单纯 water color 参数问题，而是 CK3 mapface/LUT final response 与本项目 post 链路不同。

---

## What We Changed

### Ocean shader

**Files Changed:** `Terrain/Effects/Ocean/OceanSurface.sdsl`, `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`

- 新增远景 Ocean-only 参数：
  - `_OceanFarMapWaterBase = float3(0.10, 0.135, 0.15)`
  - `_OceanFarMapWaterScale = float3(1.0, 1.25, 1.45)`
  - `_OceanFarMapDetailStrength = 0.35`
  - `_OceanFarMapResponseStrength = 1.0`
- 新增 `ApplyOceanFarMapResponse(...)`：
  - 用 tint 后的 `WaterColorTexture` 构造远景目标 raw 水色。
  - 只保留高于目标色的正向 lighting/reflection 细节。
  - 不把低于目标的 refraction/terrain shadow 深块作为细节带入远景。
- 保持现有近景 water composition、lighting、wave、flow、foam 路径。

### Shader text tests

**Files Changed:** `Terrain.Editor.Tests/OceanShaderTextTests.cs`

- 锁定 far-map response helper、base/scale/detail/strength 参数。
- 锁定 `finalColor = ApplyOceanFarMapResponse(...)`。
- 继续禁止 `ApplyOceanRegionalDisplayTint`、`SanitizeOceanRegionalWaterColor`、`warmReject` 等临时 trick 回归。

---

## RenderDoc Validation

- 第一版热替换 `raw = [0.10,0.16,0.18] + waterMap * [1.4,1.8,2.0]` 能消除深块，但过青且过统一。
- 第二版热替换 `raw = [0.10,0.135,0.15] + waterMap * [1.0,1.25,1.45]` 更接近 CK3 远景：
  - `(820,860)` final 约 `[0.216,0.267,0.302]`
  - `(120,310)` final 约 `[0.200,0.302,0.341]`
  - `(1440,420)` final 约 `[0.196,0.286,0.322]`
  - `(1380,620)` final 约 `[0.196,0.298,0.341]`

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`

All completed successfully. Existing NuGet vulnerability / nullable warnings remain unrelated.

---

## Next Session

- 重新启动编辑器或运行时后 fresh capture `debug.rdc`，确认 EID 280 的 `_OceanFarMap*` 参数进入 cbuffer。
- 对全图远景再抽样东西海域 final RGB，判断是否需要微调 `_OceanFarMapWaterBase` / `_OceanFarMapWaterScale`。
- 若仍要接近 CK3 边缘黑雾/未探索区域，只能另开策略层 mask 议题；不要把 province/FOW/flatmap 逻辑偷偷塞进 Ocean core shader。

