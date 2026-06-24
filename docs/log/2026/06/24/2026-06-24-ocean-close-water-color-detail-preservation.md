# Ocean close water-color detail preservation
**Date**: 2026-06-24
**Session**: Ocean close water-color response follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

`C:\Users\Redwa\Desktop\debug1.rdc` 更新后，近景海洋颜色已被 close water-color response 拉近 CK3 东亚近海区间，但用户指出水面细节大量丢失。本轮目标是在不恢复 `_OceanCloseMapface*` 全局近景常量、不改 River/shared refraction/global tonemap 的前提下，保留 close water-color 的区域水色，同时恢复 Ocean lighting/reflection/wave/refraction 高频细节。

---

## What We Did

### RenderDoc 复核
**Files/Artifacts:** `tmp/renderdoc/debug1-detail-loss-final.png`, `tmp/renderdoc/debug1-detail-loss-ocean-raw.png`

- 打开更新后的 `debug1.rdc`，确认 Ocean draw 仍为 EID 280，final composite 为 EID 2323。
- EID 280 cbuffer 已包含上一版参数：`_OceanCloseWaterColorDetailStrength=0.35`、`_OceanCloseWaterColorResponseStrength=0.85`。
- 导出 final 与 Ocean raw 后可见大面积水面变为低频青绿色块；代表水点 `(260,630)` 与 `(600,500)` 的 Ocean raw 分别约 `[0.258,0.487,0.511]`、`[0.256,0.487,0.514]`，空间差异明显不足。

### Shader 参数修正
**Files Changed:** `Terrain/Effects/Ocean/OceanSurface.sdsl`, `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`, `Terrain.Editor.Tests/OceanShaderTextTests.cs`

- 保留 `BuildOceanCloseWaterColor` 的低频 map-space 水色底座、16m shallow mask 和 `_OceanFarMap*` 远景分支。
- 将 close 默认值改为：
  - `_OceanCloseWaterColorDetailStrength = 1.0f`
  - `_OceanCloseWaterColorResponseStrength = 0.70f`
- 在 `zoomedOut=0` 时，当前公式等价于保留完整 composed water 高频，再叠加 70% 的地图水色偏移；避免上一版只保留约一半原始高频并让低频 target 主导输出。
- 更新 shader 文本测试锁定新默认值和“颜色偏移而非替代层”的意图。

---

## Decisions Made

### Close water-color 只能作为偏移层
**Context:** 直接或强权重输出 `waterColorMap` target 能接近 CK3 东亚近海颜色，但会抹掉水面浪纹和反射。

**Decision:** 保留 close target 的区域/深浅水趋势，但让原始 composed water detail 成为主体。

**Rationale:** CK3 近景水面不是统一全局目标色；Italy 与 East Asia 截帧已经证明区域差异真实存在。当前项目也没有 CK3 完整 post/mapface/LUT 链路，因此 close response 必须局限为 Ocean-only 能量/色相补偿，不能替代光照结果。

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`

All commands passed. Build still reports existing NuGet vulnerability warnings and the existing nullable warning in `TerrainRenderFeature.cs`.

---

## Next Session

1. Capture a fresh frame after rebuilding and compare the new Ocean raw/final against `debug1-detail-loss-*` exports.
2. If water remains too flat, inspect whether detail is lost before `ApplyOceanWaterColorMapResponse` or inside earlier display response/refraction composition.
3. Avoid reintroducing `_OceanCloseMapface*` or any global close-mapface constants; use region/depth/refraction evidence from CK3 captures instead.
