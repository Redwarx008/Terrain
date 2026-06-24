# Ocean 源路径细节校准
**Date**: 2026-06-25
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 在不新增 CK3 mapface / Tony LUT / ColorCube / 全屏提亮后处理的前提下，提升 Ocean 水面可见细节。

**Success Criteria:**
- 所有 SDSL 改动前先通过 RenderDoc 热替换验证。
- 保持当前最终水色低频均值基本不漂移。
- 提升浪纹、反射、高光等 highpass/gradient 细节。
- 不改 shared refraction capture、River、global tonemap、scene lighting。

---

## Context & Background

**Current State:**
- 本地 `C:\Users\Redwa\Desktop\debug.rdc`：
  - Ocean draw: EID 280
  - Final pass: EID 3445
- CK3 参考 `C:\Users\Redwa\Desktop\ck3-cocean-ltaly.rdc`：
  - Water draw: EID 490
  - Final pass: EID 1263

**Why Now:**
- 用户确认当前最终水色方向已经接近，不需要实现 CK3 最后全屏提亮。
- 剩余问题主要是 Ocean 缺少 CK3 那种可见水面细节，而不是整体颜色继续漂移。

---

## What We Did

### 1. RenderDoc 热替换筛选细节参数
**Files Changed:** `tmp/renderdoc/hot-ab/ocean-hot-common.hlsl` only

**Candidates Tested:**
- v1-v4: 小幅降低 reflection normal flatten、提高 reflection/specular/gloss，或试探 `_WaterFlowNormalScale=0.04`。
- v5/v9: 在细节候选基础上降低 response strength。
- v6: `_WaterReflectionNormalFlatten=0.6`、`_WaterReflectionIntensity * 2.5`、`_WaterSpecular * 2.0`、`_WaterGlossScale * 4.0`。
- v7: 更激进的 reflection/specular/gloss 组合。
- v8: v6 + `_WaterFlowNormalScale=0.04`。

**Result:**
- v1-v4 细节提升太弱。
- v5/v9 会改变低频颜色，不采用。
- v7 细节更强但 final mean 漂移过大，不采用。
- v8 没有比 v6 明显更好，且 flow normal scale 变化有浪纹尺度/速度漂移风险。
- 采用 v6。

### 2. 落地到 OceanSurface
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`

**Implementation:**
```hlsl
stage float _WaterReflectionNormalFlatten = 0.6f;
stage float _WaterReflectionIntensity = 0.25f;
stage float _WaterSpecular = 0.02f;
stage float _WaterGlossScale = 0.4f;
```

**Rationale:**
- 这些参数只增强 Ocean 源路径中 reflection/specular/gloss 对 normal 结构的承载。
- `_WaterFlowNormalScale` 保持 `0.025f`，避免把细节修复变成浪纹尺度/速度变化。
- 现有 color/response 参数不变，避免再次用低频颜色补偿掩盖细节。

### 3. 回归测试和文档
**Files Changed:**
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/ocean-ck3-renderdoc-validation.md`

**Implementation:**
- 测试锁定 v6 参数。
- 测试禁止 Ocean shader / render feature 引入 `Tony`、`ColorCube`、`Mapface`、`FixedExposure`、`SunIntensity`、`ToneMap`、`LUT` 等 CK3 后处理或亮度 token。
- 文档记录本次 hot gate、采用参数和被拒绝方向。

---

## Decisions Made

### Decision 1: 保留当前低频水色基线，只修源路径细节
**Context:** 当前最终颜色已经接近目标截图，继续调 response 会压平区域差异和波纹。

**Decision:** 不新增 post pass，不继续扩大 display/close/far response；只修改 reflection/specular/gloss/normal 承载参数。

**Rationale:** 源路径细节能提升真实水面结构，response 更适合低频颜色校正，不适合制造浪纹。

### Decision 2: 不提高 flow normal scale
**Context:** `_WaterFlowNormalScale=0.04` 热替换没有明显优于 v6。

**Decision:** 保持 `_WaterFlowNormalScale=0.025f`。

**Rationale:** 用户明确反馈过浪速过快；flow normal scale 变化容易被误读为动画/尺度变化，不适合作为本次细节修复。

---

## Verification

**RenderDoc Gate:**
- 在 `debug.rdc` 中热替换 Ocean PS。
- v6 final mid-water mean 相对 baseline 只小幅漂移，同时 red/blue highpass 与 gradient 上升。
- v7 因均值漂移更明显被拒绝。

**Commands Run:**
```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet build Terrain.sln --no-restore
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

**Result:**
- All commands passed.
- `dotnet build` / tests still emit existing NuGet vulnerability warnings and existing code warnings, but no errors.

---

## Next Session

### Immediate Next Steps
1. 让用户用 fresh `debug.rdc` 或运行时截图检查 v6 的实际运动中细节。
2. 如果仍偏平，优先做新的 RenderDoc hot replacement，围绕 reflection/specular/fresnel 的组合继续小步调参。
3. 不把 CK3 mapface/post 亮度参数塞回 Ocean shader；若以后需要正式后处理，应作为独立 renderer/post pass 设计。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 本次落地的是 v6：`flatten=0.6`、`reflection=0.25`、`specular=0.02`、`glossScale=0.4`。
- `_WaterFlowNormalScale` 保持 `0.025`。
- 颜色 response 未改变。
- 禁止用 `Tony` / `ColorCube` / `Mapface` / `LUT` / `SunIntensity` / `ToneMap` 解决本轮 Ocean 细节问题。

**Gotchas:**
- v7 看起来更有细节，但均值漂移更大。
- 降 response strength 会改变当前已接近的水色基线。
- `tmp/renderdoc/**` 是热替换/导出产物，不要提交。

---

## Links & References

- Related learning: `docs/log/learnings/ocean-ck3-renderdoc-validation.md`
- Previous session: `docs/log/2026/06/25/2026-06-25-ocean-ck3-renderdoc-hot-validation.md`

