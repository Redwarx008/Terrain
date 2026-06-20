# 河流 bottom shadow 精确移植与直射/IBL 比例复核
**Date**: 2026-06-19
**Session**: river bottom shadow port and balance recheck
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 把 CK3 bottom shadow 路径精确移植回 `RiverBottom.sdsl`，不再使用旧 Stride 5x5 / normal-offset shadow helper。

**Secondary Objectives:**
- 复核 bottom pass 的 direct / IBL 比例是否仍与 CK3 语义一致。
- 更新项目文档，避免继续把 shadow 路径描述成“尚未移植”。

**Success Criteria:**
- `RiverBottom.sdsl` 与 `RiverRenderFeature.cs` 中的 bottom shadow 路径切回目标语义。
- shader 生成、Stride 资产编译和文本测试全部通过。
- 架构文档、功能总览和本次会话日志反映当前真实状态。

---

## Context & Background

**Previous Work:**
- `docs/log/2026/06/18/2026-06-18-river-bottom-scene-lighting-renderdoc-recheck.md`
- `docs/log/2026/06/19/2026-06-19-river-bottom-debug-rdc-parity-recheck.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Current State:**
- 之前 bottom BRDF、scene sun、scene cubemap 已基本对齐 CK3，但 shadow 仍停留在“先不乘非等价 Stride helper”的过渡状态。
- 用户要求 bottom / surface 都完全参照 CK3，本轮重点回到 bottom shadow 正式落地和直射/IBL 比例复核。

**Why Now:**
- 文档仍把 bottom shadow 标成缺口，容易导致后续排障继续基于过时状态。

---

## What We Did

### 1. 把 bottom shadow 切回目标语义
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- 在 `RiverBottom.sdsl` 中加入目标风格 helper：
  - `GetSceneShadowDiscSample`
  - `CalcRandom`
  - `RotateShadowDisc`
  - `CalculateRiverBottomShadow`
- `EvaluateSceneShadow` 改为：
  - 先用 Stride scene 数据选中 cascade
  - 再用 CK3 bottom shadow 的投影/随机 disc kernel/bias/fade 计算 shadow term
- 水面交点使用目标公式：
  - `sunRaySurfaceIntersection = waterDepth / toSunDir.y`
  - `shadowCompareDepth = min(shadowProj.z, waterSurfaceProj.z) - _SceneShadowBias`
- `RiverRenderFeature.cs` 删除旧 Stride helper 绑定残留：
  - `_SceneShadowDepthBias`
  - `_SceneShadowOffsetScale`
  - `_SceneShadowMapTextureSize`
  - `_SceneShadowMapTextureTexelSize`
- 保留 bottom 真正需要的 scene 输入：
  - directional light direction/color
  - cascade count / split / matrices
  - shadow atlas texture

**Rationale:**
- 当前项目仍需要复用 Stride lighting/shadow 系统提供的 cascade 选择和 atlas 资源，但 bottom 的 shadow compare、kernel 和 fade 必须回到 CK3 语义。

**Architecture Compliance:**
- ✅ 继续遵守 `RiverRenderFeature -> DynamicEffectInstance("RiverBottom")` 的正式渲染路径
- ✅ 没有引入新的 river-local lighting workaround

### 2. 复核 bottom direct / IBL 比例
**Files Changed:** none

**Implementation:**
- 复核 `RiverBottom.sdsl` 当前能量路径：
  - `lightIntensity = _SceneSunColor * shadow`
  - `environmentIntensity = _EnvironmentIntensity * _BottomEnvironmentIntensity`
- 复核 editor scene 输入：
  - map sun intensity = `20`
  - map environment intensity = `20`
- 复核 Stride 引擎 light 语义：
  - `RenderLight.Color` 已包含 light intensity，不需要在 river bottom 再额外乘一遍。

**Rationale:**
- bottom 之前的主要偏差不是 direct/IBL 比例本身，而是 shadow 路径不等价、scene 输入曾经不等价、以及旧的 `* 3.0f` / `_BottomSpecularIntensity` 补偿。
- 当前 direct 与 IBL 的 scene-scale 比例保持 `1:1`，这和 CK3 capture 的 `SunIntensity=20`、`CubemapIntensity=20` 一致。

### 3. 跑 Stride shader/asset/test 验证
**Files Changed:** generated `Terrain.Editor/Effects/RiverBottom.sdsl.cs`

**Implementation:**
- 运行：
  - `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
  - `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-build`
- 更新 `RiverShaderTextTests.cs`：
  - 新增 `BottomShaderUsesTargetBottomShadowPath()` 的目标断言
  - 禁止旧 `Get5x5SceneShadowFilterKernel` / `FilterSceneShadow5x5` / `GetSceneShadowPositionOffset`
  - `BottomShaderUsesSceneMaterialLighting()` 改为要求 `EvaluateSceneShadow(...)`

**Rationale:**
- 这条链必须在 Stride 资产编译后验证，否则很容易把旧 shader cache 误当成当前实现状态。

### 4. 更新文档与经验
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- 把“bottom shadow 尚未移植 / direct light 暂不乘 shadow”的描述更新为当前正式实现。
- 明确记录 bottom direct / IBL 的 scene-scale 比例现在以 scene sun / scene skybox intensity 为准，当前是 `20 : 20`。
- 在 learnings 中补充 `renderdoc-cli shader-replace` 不是持久热修会话的限制。

**Rationale:**
- 当前代码状态如果不写回文档，下一轮会继续基于旧结论重复排查。

---

## Decisions Made

### Decision 1: 保留 Stride cascade 选择，shadow compare/kernel/fade 切回 CK3
**Context:** 当前项目的 shadow atlas 和 cascade 数据来自 Stride scene lighting，不应重写整套 scene shadow 系统。
**Options Considered:**
1. 全量重做一套 shadow atlas 输入
2. 继续使用旧 Stride 5x5 helper
3. 保留 Stride cascade 选择，但把 bottom shadow compare/kernel/fade 切回 CK3

**Decision:** 选择 Option 3
**Rationale:** 这样最接近当前工程的 scene 数据边界，同时满足 bottom shadow 的目标语义。
**Trade-offs:** 仍然依赖 Stride 提供 cascade/world-to-shadow 矩阵，但 bottom shadow 的 compare/kernel/fade 已切回目标路径。

### Decision 2: direct / IBL 比例以 scene 输入对齐，不再用 river-local 补偿
**Context:** 之前存在 `* 3.0f` 和 `_BottomSpecularIntensity` 这类补偿历史。
**Options Considered:**
1. 用 river-local multiplier 继续做视觉补偿
2. 直接让 bottom 只读 scene sun / scene skybox，并对齐 scene intensity

**Decision:** 选择 Option 2
**Rationale:** CK3 目标是 scene-driven lighting，不是 river-local 增益。
**Trade-offs:** 如果后续仍有画面偏差，必须继续查 shadow/IBL 语义和资源链，而不是回退到 multiplier。

---

## What Worked ✅

1. **把 shadow 缺口收敛成 scene 输入适配层 + bottom shader 两个点**
   - What: C# 只保留真实 scene 输入，SDSL 回到目标 shadow compare/kernel/fade
   - Why it worked: 能在不重写 scene lighting 系统的前提下把 bottom shadow 语义拉回目标
   - Reusable pattern: Yes

2. **用文本测试锁住目标 shadow 语义**
   - What: 显式要求 `CalcRandom`、disc kernel、water-surface intersection 和 edge fade
   - Why it worked: 后续不容易再回退到旧 Stride helper
   - Reusable pattern: Yes

3. **把 direct / IBL 比例回归到 scene-level 配置复核**
   - What: 不再围绕 river-local 参数猜测能量比例，直接检查 scene sun / skybox intensity
   - Why it worked: 当前 CK3 对齐点本来就建立在 scene 级输入上
   - Reusable pattern: Yes

---

## What Didn't Work ❌

1. **继续依赖 `renderdoc-mcp` 做热修复回读**
   - What we tried: 反复通过 MCP 打开 capture 并保持会话
   - Why it failed: 当前环境仍然 `Transport closed`
   - Lesson learned: 本轮不能把 MCP 当成稳定依赖
   - Don't try this again because: 不会产生新信息

2. **把 `renderdoc-cli shader-replace` 当成持久会话**
   - What we tried: 先替换 shader，再用下一条命令继续读像素/RT
   - Why it failed: CLI 是单命令单进程，替换状态不会跨命令保留
   - Lesson learned: CLI 只能稳定承担 compile/inspection，不适合本轮这种持续热修验证
   - Don't try this again because: 会重复卡在同一个工具边界

---

## Architecture Impact

### Documentation Updates Required
- [x] 更新 `docs/ARCHITECTURE_OVERVIEW.md` - river bottom shadow 与 direct/IBL 比例状态
- [x] 更新 `docs/CURRENT_FEATURES.md` - river feature 状态与当前缺口描述
- [x] 更新 `docs/log/learnings/stride-river-rendering-patterns.md` - 补充 CLI 热替换限制

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 把 `renderdoc-cli shader-replace` 当成跨命令持久热修会话
- What not to do: 用独立 CLI 命令链假设 replacement 状态能自动保留
- Why it's bad: 会把“无法读回”误判成 shader 无效
- Add warning to: `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Code Quality Notes

### Testing
- **Tests Written:** 更新 `RiverShaderTextTests` 中的 bottom shadow 语义断言
- **Coverage:** bottom shadow 路径、bottom scene lighting 绑定、旧 helper 防回归
- **Manual Tests:** 本轮未完成新的持久 RenderDoc replacement 回读；原因是工具会话不可持续

### Technical Debt
- **Paid Down:** 删除旧 Stride bottom shadow helper 残留绑定
- **TODOs:** 后续仍应在新的 capture 上再做一次正式 GPU 画面复核

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 在晚于当前 build 的新 `debug.rdc` 上复核 bottom RT 与 final surface 画面
2. 恢复可用的 `renderdoc-mcp` 或 GUI 热替换会话，做 replacement 后的持续回读
3. 继续检查 surface 后段是否还有和 CK3 不等价的 map-lighting 细节

### Blocked Items
- **Blocker:** `renderdoc-mcp` 当前 transport 不稳定
- **Needs:** 稳定的 MCP session 或直接 GUI 热替换流程
- **Owner:** 调试工具环境

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/log/2026/06/19/2026-06-19-river-bottom-debug-rdc-parity-recheck.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Session Statistics

**Files Changed:** 6
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverBottom.sdsl` 现在已经使用目标 bottom shadow compare/kernel/fade，不再是 `shadow = 1.0f`
- `RiverRenderFeature.cs` 已删旧 Stride shadow helper 绑定残留，只保留真实 scene 输入
- bottom direct / IBL 的 scene-scale 比例当前是 `20 : 20`，即 `1:1`

**What Changed Since Last Doc Read:**
- Implementation: bottom shadow 已正式移植
- Architecture: river bottom 状态不再是“shadow 尚未移植”
- Constraints: `renderdoc-cli shader-replace` 不能当持久热修会话

**Gotchas for Next Session:**
- Watch out for: 不要再把旧 `debug.rdc` 当成当前源码 build 的最终证据
- Don't forget: Stride `RenderLight.Color` 已包含 intensity，不要在 bottom shader 再乘一遍 scene intensity
- Remember: CLI-only 条件下 replacement 不能作为最终 GPU 回读证据

---

*Template Version: 1.0 - Based on Archon-Engine template*
