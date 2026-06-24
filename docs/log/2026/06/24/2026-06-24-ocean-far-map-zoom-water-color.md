# Ocean far-map zoom water color
**Date**: 2026-06-24
**Status**: Complete
**Priority**: High

---

## Session Goal

解释并修复 `C:\Users\Redwa\Desktop\debug.rdc` 中 Ocean 全图视角海面过深、东西方水色差异不像 CK3 的问题。

---

## Context & Background

前一轮已经撤掉 `ApplyOceanRegionalDisplayTint` 和 warm reject workaround，改成 CK3-like pre-display water color map 路径。但新的 `debug.rdc` 仍显示全图海面大面积接近黑，地图东西水色差异被压掉。

---

## What We Did

### RenderDoc diagnosis

`debug.rdc` 的 Ocean draw 为 EID 280，final swapchain draw 为 EID 939。shader 搜索确认当前捕获已经包含 `_WaterColorMapTint`，且没有旧 `ApplyOceanRegionalDisplayTint` / `OceanDisplayRegional`。

代表水面点：
- 西侧开阔水面 `(120,130)` Ocean raw 约 `[0.068, 0.081, 0.099]`，final 约 `[0.082, 0.102, 0.125]`。
- 印度洋/中东附近 `(760,720)` Ocean raw 约 `[0.030, 0.016, 0.032]`，final 约 `[0.024, 0.004, 0.020]`。

trace 里 `WaterColorTexture` 样本 alpha 约 `0.19..0.21`，而当前权重是 `waterColorAndSpec.a * _OceanWaterColorTextureInfluence`，默认 `_OceanWaterColorTextureInfluence=0.20`，地图水色实际只有约 `4%` 权重。当前帧 `_WaterRefractionScale=500`，而 CK3 远景捕获为 `_WaterRefractionScale=0`，所以本地远景还在用强折射把深色 capture/refraction 内容拉回海面。

### Shader change

**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`

新增 CK3-like far-map 分支：
- `ComputeOceanZoomedOutFactor()` 用相机距水面高度在 `_OceanZoomedOutStartHeight=650` 到 `_OceanZoomedOutEndHeight=2500` 之间近似 CK3 zoom factor。
- 近景继续用 `waterColorAndSpec.a * _OceanWaterColorTextureInfluence`。
- 远景改用 `_OceanZoomedOutWaterColorTextureInfluence=1.0`，让地图空间 `water_color.dds` 真正主导水色。
- `CalcRefraction` 远景通过 `_OceanZoomedOutRefractionInfluence=0.0` 淡出 refraction offset 和 see-through refraction，并回退到地图空间 `WaterColor`。
- `ApplyOceanDisplayResponse` 远景使用 `_OceanZoomedOutDisplayDetailGain=1.0`，避免 deep detail gain `2.0` 继续压黑低于 reference 的 far-map 水色。

保持不变：
- 不恢复 final display regional tint。
- 不恢复 warm reject workaround。
- 不改 shared refraction capture。
- 不改 River。
- 不接入 province / FOW / flatmap / `_WaterToSunDir`。

---

## Decisions Made

### 用相机高度近似 CK3 zoom factor

**Context:** CK3 远景 draw 有 `_WaterZoomedInZoomedOutFactor=1.0` 和 `_WaterRefractionScale=0.0`；本项目目前没有同名 CPU 参数。

**Decision:** 在 Ocean shader 内用 `_CameraWorldPosition.y - _WaterHeight` 计算远景因子。

**Rationale:** 这是渲染路径层面的 zoom 近似，直接对应 CK3 远景水色/折射分支，不是最终颜色贴片。

**Trade-offs:** 相机高度不是完整策略相机 zoom state；如果未来有正式 zoom 参数，应由 render feature 绑定并替换这个近似。

---

## What Worked

1. RenderDoc pixel trace 直接定位到 water map 权重过低和远景折射过强，而不是继续猜材质颜色。
2. Shader 文本测试锁定 far-map path 的关键结构，防止回退到 final tint 或 warm reject。

---

## Problems Encountered & Solutions

### Editor tests first run failed with CS2012

**Symptom:** `Terrain.dll` 被占用，`dotnet run --project Terrain.Editor.Tests` 失败。

**Root Cause:** 同时并行运行了 `dotnet build Terrain.csproj` 和 editor tests，两个进程竞争同一输出文件。

**Solution:** build 完成后串行重跑 editor tests，通过。

---

## Testing

Commands run:
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`

Result:
- All commands passed after the editor tests were rerun serially.
- Existing warnings remain: NuGet vulnerability warnings and existing nullable/unused-field warnings.

---

## Next Session

1. Capture a fresh RenderDoc frame after this shader build and verify far-map water points no longer sit near black.
2. Compare west/east water samples against `ck3-ocean远.rdc`; if large differences are still missing, the remaining gap is likely CK3 strategy/FOW/surround/black/cloud mask layers rather than the Ocean water pass.
3. If close-up water loses too much refraction, tune `_OceanZoomedOutStartHeight` / `_OceanZoomedOutEndHeight` rather than changing shared refraction capture.

---

## Quick Reference

Key implementation:
- `Terrain/Effects/Ocean/OceanSurface.sdsl`: `ComputeOceanZoomedOutFactor`, `ComputeOceanWaterColorTextureInfluence`, zoomed-out refraction fade, zoomed-out display detail gain.
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`: locks the far-map path and keeps the final regional tint/warm reject banned.

