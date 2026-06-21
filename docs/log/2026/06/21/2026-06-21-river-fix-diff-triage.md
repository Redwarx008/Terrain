# 河流修复 diff 清理评估
**Date**: 2026-06-21
**Session**: river fix diff triage
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 在最终 `_RefractionMaxCameraHeight` 修复确认后，检查前面几轮错误方向的源码改动是否仍有残留。

**Success Criteria:**
- 明确哪些改动必须保留。
- 明确哪些错误试错已经撤回。
- 明确哪些改动不是最终根因但值得保留或单独验证。

---

## What We Did

### 1. 检查当前工作区 diff

**Commands:**
- `git diff --name-status`
- `git diff --check`
- `rg` 搜索 `_SurfaceBankFade`、`seeThroughDepth`、`effectiveDepth`、five-lane/miter 试错、`RefractionMaxCameraHeight`。

**Findings:**
- `height_scale=50` 临时配置已撤回；`game/map/default.toml` 为 `200`，`game/map_data/default.toml` 为 `200.0`。
- 五横截面 lane / acute miter clamp 的 mesh 试错没有残留在当前 diff。
- `seeThroughDepth = min(refractionDepth, Depth)` 这类错误 depth cap 没有残留。
- `_SurfaceBankFade` / `ComputeSurfaceAlpha` 旧 workaround 没有残留；当前 `RiverSurface` 使用 CK3 active `CalcRiverAdvanced` 的 `_BankFade` edge fade。
- 当前需要保留的最终修复集中在 `RiverCommon`、`RiverSceneSeed`、`RiverMeshData`、`RiverMeshService`、`RiverRenderObject`、`RiverRenderFeature` 和生成 key / 测试。

### 2. 分类非根因改动

**Worth keeping:**
- `RiverSurface` 的 refraction alpha payload `Texture2D.Load`：虽不是最终 height-clamp 根因，但已有 `debug2.rdc` 热替换证明线性过滤 distance payload 会制造 pointed depth。
- `RiverSurface` 的 advanced alpha 分支：它纠正了上一轮误读 CK3 helper 的错误，属于恢复目标 active shader path。
- `RiverStrideLighting` diffuse IBL：它不是最终 height-clamp 根因，但 CK3 bottom lighting 本身包含 diffuse IBL；建议作为单独视觉修复保留或单独提交。

**Already reverted / no source action:**
- 降低 `height_scale` 到 `50`。
- mesh 五 lane / acute miter clamp。
- see-through depth cap。
- surface-only `_SurfaceBankFade` workaround。

---

## Decisions Made

### Decision 1: 不做新的源码回退

**Decision:** 当前源码没有必须立刻回退的错误试错残留。

**Rationale:**
- 已知错误方向均已不在源码中。
- 剩余几项虽然不是最终 height-clamp 根因，但分别有 CK3 source audit 或 RenderDoc hot-replace 证据支持。

**Trade-offs:**
- `RiverStrideLighting` diffuse IBL 会影响整体水底亮度，若需要最小化最终 patch，可以单独拆成一个 commit 或单独 A/B capture 验证。

---

## Quick Reference for Future Claude

**Keep with final height-clamp fix:**
- `_RefractionMaxCameraHeight`
- `RiverSceneSeed : ..., RiverCommon`
- seed/bottom/surface 统一 `RiverCompressWorldSpace`
- `RefractionMaxCameraHeight` 从 terrain height scale 传到 shader cbuffer

**Keep as validated adjacent corrections:**
- refraction RGB linear + alpha payload `Load`
- surface advanced `_BankFade` alpha branch
- diffuse IBL, but最好单独验证/提交

**Do not reintroduce:**
- `height_scale=50` 作为修复
- mesh five-lane / acute miter clamp
- `seeThroughDepth=min(refractionDepth, Depth)`
- `_SurfaceBankFade`
