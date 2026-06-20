# River Surface Post Color Removal
**Date**: 2026-06-19
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 按用户要求验证“去掉 `HeightLookupTexture + PackedHeightTexture + FogOfWarAlpha` 同类颜色依赖不应影响主水面颜色”，并把 current `RiverSurface` 的 post step 改成只保留可见性控制。

**Secondary Objectives:**
- 用测试先锁住行为，再改 shader。
- 跑完整 Stride shader/asset 编译链，排除旧缓存。

**Success Criteria:**
- `ApplySurfacePostProcessing` 不再修改 `color.rgb`
- 仍保留 `alpha/zoom/discard`
- 文本测试、shader 编译、asset 编译全部通过

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-ck3-current-pass-semantic-audit.md](./2026-06-19-river-ck3-current-pass-semantic-audit.md)
- See: [2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md](./2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md)

**Current State:**
- `RiverSurface` 仍保留 Editor terrain `HeightmapSlice`、cloud shadow、terrain shadow tint、distance fog 的函数和绑定。
- 但用户要求先验证这整段颜色后处理移除后，水面主色是否更接近 CK3。

**Why Now:**
- 前一轮分析已经确认当前关键 water cbuffer 常量基本对齐，剩余最大嫌疑是 surface 后段颜色链。

---

## What We Did

### 1. 先把测试改成“post step 只保留可见性控制”
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 把原来的 `river surface shader includes target map post chain` 改成 `river surface shader post step keeps only visibility controls`
- 断言保留：
  - `color.a *= 1.0f - _FlatMapLerp;`
  - `color.a *= zoomBlendOut;`
- 断言移除：
  - `GetCloudShadowMask(...)`
  - `ApplyTerrainShadowTintWithClouds(color.rgb, ...)`
  - `ApplyMapDistanceFogWithoutFoW(color.rgb, ...)`
  - cloudy tint `lerp`

**Rationale:**
- 先把目标行为锁死，再改 shader，避免“改了但测试仍接受旧逻辑”。

### 2. 只改 `ApplySurfacePostProcessing` 的颜色路径
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- 保留 `zoomBlendOut`、`_FlatMapLerp`、alpha discard 逻辑
- 删除最终颜色上的：
  - cloud shadow mask 混色
  - terrain shadow tint
  - distance fog

**Rationale:**
- 按用户选择的最小方案，只验证颜色链，不扩大到资源解绑或函数删除。

### 3. 跑验证链
**Files Changed:** 无

**Commands Run:**
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`

**Results:**
- 文本测试先红后绿
- `RiverBottom` / `RiverSurface` / `RiverSceneSeed` shader compile tests 通过
- Stride generated files / clean / compile asset 全部成功

---

## Decisions Made

### Decision 1: 先不删 terrain/cloud/fog 相关声明和绑定
**Context:** 用户要先验证“去掉颜色依赖”的方向，而不是马上重构 provider 链。
**Decision:** 只停用 `ApplySurfacePostProcessing` 的 `color.rgb` 改写，不删 helper、不删 C# 绑定。
**Rationale:** 这样最容易把视觉变化归因到 post color chain，而不是资源解绑副作用。
**Trade-offs:** 当前代码仍保留一批未参与最终颜色的 surface 后段输入。

### Decision 2: 保留 alpha/zoom/discard
**Context:** 用户要去掉的是颜色后处理，不是河流可见性逻辑。
**Decision:** `ApplySurfacePostProcessing` 保留 `alpha` 和 `discard`。
**Rationale:** 避免把问题从“颜色差异”变成“河面消失/边缘不对”。

---

## What Worked ✅

1. **文本测试先红再绿**
   - What: 先把测试改成禁止 surface post 修改 `color.rgb`
   - Why it worked: 失败点直接命中 `cloud shadow` 颜色链，说明测试确实覆盖到了目标逻辑

2. **最小 shader 改动**
   - What: 只改一个函数体
   - Impact: 风险小，验证链清晰

---

## What Didn't Work ❌

1. **用 `dotnet test --filter RiverShaderTextTests` 找单组测试**
   - What we tried: 直接用 `dotnet test` 跑筛选
   - Why it failed: 这个测试项目主要走自定义 `TestHarness`/`Program` 入口，不适合那样筛
   - Lesson learned: 这里直接 `dotnet run` 更可靠

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`
- [x] 新增本次会话日志

### Architectural Decisions That Changed
- **Changed:** river surface 后段颜色链当前是否参与最终颜色
- **From:** `ApplySurfacePostProcessing` 会对 `color.rgb` 执行 terrain/cloud/fog 处理
- **To:** `ApplySurfacePostProcessing` 当前只负责可见性，不改 `color.rgb`
- **Reason:** 先验证主水色应由 `CalcRiverAdvanced -> CalcWater` 主导

---

## Code Quality Notes

### Testing
- **Tests Written:** 修改既有 `RiverShaderTextTests` 断言
- **Manual Tests:** 尚未重新抓新 `debug.rdc`
- **Compile Verification:** river shader compile tests 与 Stride asset compile 均通过

### Technical Debt
- **Created:** 保留了暂时不参与最终颜色的 terrain/cloud/fog helper 与绑定
- **TODOs:** 下一轮重新抓帧，看移除 post color 后是否仍偏黑，再决定是否解绑或删除这些输入

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 抓新 `debug.rdc` - 验证 `RiverSurface` 去掉 post color 后的实际画面
2. 如果仍偏黑，继续拆 `CalcWater` 主体，而不是回头加 terrain/cloud/fog
3. 如果画面明显回正，再决定是否清理未使用的 `HeightmapSlice` / `ShadowNoiseTexture` 绑定

### Questions to Resolve
1. 去掉 post color 后，当前和 CK3 的主差距是否还主要在 `FoamTexture/FoamRampTexture` 资源语义？
2. 这些未使用 helper 是暂时保留用于热修改，还是下一轮直接删掉更合适？

### Docs to Read Before Next Session
- [2026-06-19-river-ck3-current-pass-semantic-audit.md](./2026-06-19-river-ck3-current-pass-semantic-audit.md)
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

---

## Session Statistics

**Files Changed:** 4
**Lines Added/Removed:** 约 +100 / -10
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverSurface` 当前 post step 只保留 alpha/zoom/discard
- terrain/cloud/fog helper 与输入仍在代码里，但不再参与最终 `color.rgb`
- Stride generated files / clean / compile asset 都已经跑过

**Gotchas for Next Session:**
- 不要把“helper 还在文件里”误判成“它还在影响最终颜色”
- 下一步优先抓新帧，不要先继续删绑定

---
