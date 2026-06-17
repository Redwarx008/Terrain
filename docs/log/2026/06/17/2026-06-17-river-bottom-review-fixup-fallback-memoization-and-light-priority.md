# River Bottom Review Fixup: Fallback Memoization And Light Priority
**Date**: 2026-06-17
**Session**: River bottom review fixup
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 收口 subagent review 指出的 river bottom 两个遗留问题：optional cubemap fallback 的逐帧重试，以及 bottom scene-light 仍然“取第一盏灯”的不稳定选择逻辑。

**Secondary Objectives:**
- 把新的 fallback / 选灯语义写回架构文档和功能总览。
- 补一条独立日志，避免后续只能从大段 river 调试日志里回溯这轮 review 修复。

**Success Criteria:**
- `RiverResourceLoader` 对 legacy bottom cubemap 的按需加载只尝试一次。
- `RiverRenderFeature` 不再简单取第一盏 directional / skybox，而是优先选择真正参与当前 `LightingView` 光照的候选。
- 构建与测试保持绿色。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-bottom-view-light-selection-and-optional-env-fallback.md](./2026-06-17-river-bottom-view-light-selection-and-optional-env-fallback.md)
- Related: [2026-06-17-river-bottom-scene-driven-lighting.md](./2026-06-17-river-bottom-scene-driven-lighting.md)
- Related: [adr-014-river-rendering-architecture.md](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- 上一轮已经把 bottom lighting 的主语义切到 scene-driven，但 review 继续指出：
  - legacy `Skybox texture` 缺失时，`EnsureBottomEnvironment()` 会每帧再次尝试 `Content.Load`
  - directional / skybox 仍存在“取第一盏可见灯”的隐患
  - 文档没有写清 `reflection-specular` 仍是最终第三级 cubemap fallback

**Why Now:**
- 这些问题不会立刻改坏画面，但会持续制造不稳定输入与运行时异常路径，属于必须在继续做 RenderDoc 对齐前先收口的工程问题。

---

## What We Did

### 1. 让 optional bottom environment fallback 对失败也做记忆
**Files Changed:** `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`

**Implementation:**
- 新增 `bottomEnvironmentLoadAttempted`
- `EnsureBottomEnvironment(ContentManager content)` 改成：
  - 已尝试过则直接返回缓存结果
  - 首次调用先记录 `bottomEnvironmentLoadAttempted = true`
  - 再执行 `BottomEnvironment = LoadOptionalTexture(content, BottomEnvironmentUrl)`
- `Dispose()` 时同时重置 `bottomEnvironmentLoadAttempted` 与 `BottomEnvironment`

**Rationale:**
- optional fallback 资源不应该在 render loop 中重复触发加载异常；成功和失败都应当视作“一次性决议”。

### 2. 把 bottom directional / skybox 选择改成 view-scoped priority 语义
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- 在 `PrepareBottomSceneLighting(...)` 中：
  - 先解析 `lightingView = renderView.LightingView ?? renderView`
  - 优先读取 forward-lighting 的 per-view `renderViewDatas`
  - `lights` 优先使用 `renderViewLightData.VisibleLights`，取不到再退回 `CollectFallbackVisibleLights(lightingView)`
- directional 选择规则：
  - 优先从 `VisibleLightsWithShadows` 中找 `LightDirectional`
  - 仅接受真正拿得到 shadow map 的候选
  - 多个候选按 `Intensity` 选更强的
  - 如果没有 shadowed directional，再从全部可见 directional 中按强度选
- skybox 选择规则：
  - 优先选择 `TryGetSceneEnvironmentTexture(light) != null` 的 `LightSkybox`
  - 多个候选按 `Intensity` 选更强的
  - 没有任何带 cubemap 的 skybox 时，才退回“强度最高的任意 skybox”

**Rationale:**
- 这让 river bottom 更接近“当前 `LightingView` 实际正在参与 scene shading 的太阳与 skybox”，而不是靠可见光列表顺序碰运气。

### 3. 补全架构文档里的 fallback 与选灯语义
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- 记录 directional / skybox 的 priority selection 规则
- 记录 `Skybox texture` 是 lazy optional fallback，且失败会记忆
- 记录 `reflection-specular` 仍作为 bottom 的 final non-null cubemap fallback

**Rationale:**
- 这几条语义如果不写进文档，后续 review 很容易再次把“当前实现”误读成“仍然是 river-local fallback 常量路径”。

---

## Decisions Made

### Decision 1: optional fallback 失败也要缓存 attempt
**Context:** review 指出缺失 skybox 资源时，river feature 会每帧再次进入异常路径。

**Options Considered:**
1. 保持现状，让每帧都重新尝试加载
2. 只缓存成功结果
3. 成功与失败都缓存 attempt

**Decision:** 选择 3
**Rationale:** 资源存在性对同一 loader 生命周期通常是稳定事实；对失败不记忆只会制造热路径噪音。
**Trade-offs:** 如果外部资源在运行中被补上，需要重建 loader 或 reload 内容后才会重新尝试。

### Decision 2: directional / skybox 都按“更贴近 scene shading”而不是“第一盏灯”选择
**Context:** 仅仅改成 per-view `VisibleLights` 还不够，多灯场景仍可能拿错优先级。

**Options Considered:**
1. 继续取第一盏 directional / skybox
2. 按强度选第一层优先级
3. 按“shadowed directional / real-cubemap skybox”优先，再按强度选

**Decision:** 选择 3
**Rationale:** 对 bottom lighting 来说，“是否真的接进 scene shadow / scene cubemap”比列表顺序更重要。

---

## What Worked ✅

1. **把 review 问题还原成 runtime 语义而不是表面代码样式**
   - What: 先明确“失败重试”和“第一盏灯”分别会造成什么运行时后果，再改代码
   - Why it worked: 改动面很小，但直接消掉了真正的热路径和多灯不稳定性
   - Reusable pattern: Yes

2. **用 per-view light cache + 本地 fallback 双层结构收口**
   - What: 优先读 `renderViewDatas`，失败时再回退本地 frustum 过滤
   - Impact: 不会把 river bottom 完全绑死在 Stride internal field 名称上

---

## Problems Encountered & Solutions

### Problem 1: legacy cubemap 缺失时每帧都再次加载
**Symptom:** `Skybox texture` 不存在时，bottom env fallback 每帧再次走 `Content.Load` 异常路径。
**Root Cause:** 旧实现只缓存成功结果，没有记录“已经尝试过且失败”的状态。
**Investigation:**
- 检查 `RiverResourceLoader.EnsureBottomEnvironment(...)`
- 对照 review 结论确认该方法只有 `BottomEnvironment` 非 null 才算“已完成”

**Solution:**
- 增加 `bottomEnvironmentLoadAttempted`
- 首次调用无论成功失败都记录 attempt，后续直接返回缓存结果

**Why This Works:** optional fallback 的存在性在单次内容会话中通常不变，因此“一次性尝试 + 记忆结果”最符合运行时预期。
**Pattern for Future:** 任何挂在 draw path 上的 optional `Content.Load` fallback，都应采用 one-shot memoization。

### Problem 2: 多灯场景下 bottom 仍可能拿错太阳或 skybox
**Symptom:** 即使切到 per-view visible lights，简单 `FirstOrDefault` 仍可能绑定到错误的 directional / skybox。
**Root Cause:** light list 的顺序不等于 scene shading 的优先级。
**Investigation:**
- 检查 `PrepareBottomSceneLighting(...)` 的 light 选择逻辑
- 对照 review，确认真正需要的是“优先有 shadow map 的 directional”和“优先有真实 scene cubemap 的 skybox”

**Solution:**
- 增加 `SelectBottomDirectionalLight(...)`
- 增加 `SelectBottomSkyboxLight(...)`
- shadow map 优先复用 per-view `RenderLightsWithShadows`

**Why This Works:** 选择逻辑现在依据的是“是否真的接入当前 view 的 shadow / cubemap 语义”，而不是碰巧排在列表前面。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** river bottom scene-light selection 与 optional env fallback 行为
- **From:** 可见灯列表里取第一盏 directional / skybox；optional cubemap 缺失时每帧重试
- **To:** 当前 `LightingView` 上优先选择真实 shadow / cubemap 候选；optional cubemap 只尝试一次并记忆结果
- **Scope:** `RiverRenderFeature`, `RiverResourceLoader`, river rendering 文档

---

## Code Quality Notes

### Testing
- **Verified Commands:**
  - `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - `dotnet build Terrain.Editor\\Terrain.Editor.csproj -c Debug -p:UseSharedCompilation=false`
  - `dotnet run --project Terrain.Editor.Tests\\Terrain.Editor.Tests.csproj -c Debug`
- **Result:** 通过

### Technical Debt
- **Paid Down:** bottom optional cubemap repeated-load failure path；“第一盏灯”式的不稳定 scene-light 选择
- **Remaining:** `reflection-specular` 作为第三级 fallback 只是防空纹理兜底，不代表它和 CK3 scene skybox 语义完全等价

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 再抓一帧最新 RenderDoc，确认多灯 / 缺失 legacy skybox 场景下 bottom 仍绑定到预期 directional / cubemap
2. 继续评估 current `lighting_x3` 是否还能向更物理的 scene-lighting 收敛
3. 如果 bank 泄漏问题仍在，再把 pre-bottom seed payload 与 CK3 的差异单独拆开诊断

### Questions to Resolve
1. 当前场景是否存在需要进一步显式指定“主 directional”的用例？
2. surface 侧的 neutral-lighting fallback 是否也需要在下一轮按 scene-driven 语义继续收口？

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `EnsureBottomEnvironment(...)` 现在是 one-shot optional load，失败也会记忆
- bottom directional 优先选有真实 shadow map 的候选，再按强度选
- bottom skybox 优先选有真实 scene cubemap 的候选，再按强度选
- `reflection-specular` 仍保留为 final non-null cubemap fallback

**Gotchas for Next Session:**
- 不要再把 `CurrentLights.FirstOrDefault(...)` 当成 scene-driven 选灯
- 不要让 optional fallback 资源在 render loop 里反复 `Content.Load`

---

## Code References

- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

---
