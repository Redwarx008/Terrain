# 河流渲染架构落地与顶点变换修复
**Date**: 2026-06-06
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 将河流渲染从 editor service / 临时预览路径落地为正式的 Stride 渲染架构，并修复河流在视口中的屏幕空间闪烁与全屏大三角问题。

**Secondary Objectives:**
- 保留 Vic3 风格的河底/水面多 pass 渲染方向。
- 清理本次排障过程中加入的临时 input layout 诊断代码。
- 更新系统文档，使当前河流架构与功能状态可追踪。

**Success Criteria:**
- 河流渲染走 `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature`。
- `RiverRenderingService` 仅保留 editor façade 职责。
- `RiverBottom.sdsl` / `RiverSurface.sdsl` 正确输出标准变换流，不再出现屏幕空间闪烁。
- 清理临时 `POSITION_WS` 输入布局映射后仍可通过构建。

---

## Context & Background

**Previous Work:**
- See: [2026-06-05-1-river-mesh-generation-fix.md](./2026-06-05-1-river-mesh-generation-fix.md)
- Related: [2026-05-16-1-vic3-river-renderdoc-research.md](../../05/16/2026-05-16-1-vic3-river-renderdoc-research.md)
- Related: [adr-013-vic3-path-rendering.md](../../decisions/adr-013-vic3-path-rendering.md)

**Current State:**
- 河流 mesh 生成已经可用，但渲染链路仍在从“路径材质式做法”迁移到独立渲染子系统。
- 在本次修复前，河流 draw 已经能进入自定义 pass，但顶点位置没有走完整的 `World -> ViewProjection` 标准链路。

**Why Now:**
- 河流 shader 视觉错误已经成为继续推进河流表现的主要阻塞；若不先修正顶点空间和渲染架构，后续折射、泡沫、连接段淡出都没有稳定基础。

---

## What We Did

### 1. 固化河流渲染子系统架构
**Files Changed:** `Terrain.Editor/Rendering/River/RiverComponent.cs`, `Terrain.Editor/Rendering/River/RiverProcessor.cs`, `Terrain.Editor/Rendering/River/RiverRenderObject.cs`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Services/RiverRenderingService.cs`, `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

**Implementation:**
- 以 `RiverComponent` 持有快照化 `RiverMeshData`，通过 `Version` 驱动同步。
- `RiverProcessor` 根据 `Version` 重建 `RiverRenderObject`，管理 GPU buffer 生命周期和可见性组注册。
- `RiverRenderFeature` 负责河底/水面双 pass、折射缓冲与 debug rasterizer 状态。
- `EmbeddedStrideViewportGame` 中注册 `RiverRenderFeature` 并创建 `RiverSystem` 实体。
- `RiverRenderingService` 收窄为 editor façade：接收 `RiverSegment`、调用 `RiverMeshService` 生成网格、同步显隐状态。

**Rationale:**
- 这让河流拥有和地形、刷子 decal 一样明确的渲染生命周期，不再把 mesh 生命周期、渲染状态和编辑器交互混在 service 里。

**Architecture Compliance:**
- ✅ 当前实现与新 ADR-014 一致。

### 2. 修复 shader 顶点空间链路
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl`, 相关生成文件与 `RiverEffect.sdfx`

**Implementation:**
- 将 `RiverBottom` / `RiverSurface` 从 `Transformation + PositionStream4` 切换到 `TransformationWAndVP`。
- 删除两个 shader 中空的 `VSMain()` 覆写，让 Stride 标准变换链自动生成：
  - `streams.PositionWS`
  - `streams.ShadingPosition`
  - `streams.PositionH`
  - `streams.DepthVS`
- 重新运行生成文件与资产编译链，并完成 `dotnet build Terrain.Editor.csproj -c Debug`。

**Rationale:**
- 问题根因不是水体参数，也不是底部/水面混合，而是顶点着色器没有真正把 `POSITION` 变换到裁剪空间，导致 draw 落在错误的屏幕空间位置。

### 3. 用 RenderDoc 与引擎 shader 参照定位根因
**Files Changed:** 无代码变更；结论沉淀到本文档与 ADR

**Implementation:**
- 复核 `bug.rdc` 中河流 pass，确认河流 draw 已经在跑。
- 对照 Stride 引擎 `TransformationWAndVP.sdsl` 与 `PositionStream4.sdsl`，确认标准职责分层。
- 排除 `RiverWaterCommon`、折射贴图、水面参数和 blend/depth 作为根因。

**Rationale:**
- 只有先证明“pass 在跑但变换不对”，才能避免继续在材质参数层面盲修。

### 4. 清理排障残留代码
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- 删除 `POSITION -> POSITION_WS` 的临时特殊映射。
- 删除按 shader reflection 动态拼 `InputElementDescription[]` 的临时代码。
- 删除输入布局日志输出。
- 恢复为固定使用 `RiverVertex.Layout.CreateInputElements()`。

**Rationale:**
- 在 shader 已切回标准 `TransformationWAndVP` 后，这些逻辑只是排障残留，不应继续停留在正式实现里。

---

## Decisions Made

### Decision 1: 河流渲染必须走独立 RenderFeature 架构
**Context:** 用户明确要求不要长期停留在 service-only 或 `ModelComponent` 预览路径。
**Options Considered:**
1. 继续用 `RiverRenderingService` 直接创建/更新运行时渲染资源。
2. 用 `ModelComponent` / 材质系统临时挂载河流 mesh。
3. 建立 `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature` 正式链路。

**Decision:** 选择选项 3。
**Rationale:** 该方案最符合 Stride 自定义渲染扩展方式，也最适合承载河底/水面双 pass、调试模式和后续扩展。
**Trade-offs:** 需要更多渲染基础设施代码，但结构清晰，后续维护成本更低。
**Documentation Impact:** 新增 ADR-014，并更新架构总览与功能清单。

### Decision 2: 顶点空间变换统一交给 `TransformationWAndVP`
**Context:** 河流 shader 需要使用 `PositionWS` 和标准裁剪空间输出，但当前实现未完整混入变换链。
**Options Considered:**
1. 保持现有 shader 结构，在自定义 `VSMain()` 中手写 world/view/projection 变换。
2. 切换到 Stride 标准 `TransformationWAndVP`。
3. 继续在 C# 输入布局侧用 `POSITION_WS` 临时映射规避问题。

**Decision:** 选择选项 2。
**Rationale:** 这是 Stride 已验证的标准做法，能直接提供 `PositionWS / PositionH / DepthVS`，避免 shader 与输入布局继续分叉。
**Trade-offs:** shader 必须遵守 Stride 的标准位置流约定；但这正是我们希望回归的规范路径。
**Documentation Impact:** 记录到 ADR-014 与本 session log。

---

## What Worked ✅

1. **RenderDoc 事件 + 引擎源码对照定位**
   - What: 一边看 `bug.rdc` 中的河流 draw，一边对照 Stride 的 `TransformationWAndVP.sdsl`。
   - Why it worked: 快速把问题从“材质参数异常”收敛到“顶点空间链路缺失”。
   - Reusable pattern: Yes

2. **先让正式修复成立，再回头删除临时代码**
   - What: 先修 `RiverBottom` / `RiverSurface`，确认用户反馈“现在正常了”，再清理 `RiverRenderFeature` 临时映射和日志。
   - Impact: 降低把真正修复一起删掉的风险。

---

## What Didn't Work ❌

1. **试图在输入布局层面兜底 `POSITION_WS`**
   - What we tried: 在 `RiverRenderFeature` 动态按 reflection 生成输入布局，并把 `POSITION` 临时改名映射到 `POSITION_WS`。
   - Why it failed: 这只能暂时绕过 `CreateInputLayout` 侧的问题，无法替代 shader 内缺失的标准顶点变换。
   - Lesson learned: 当 shader 需要标准位置流时，应先回到 Stride 的标准 transformation mixin，而不是继续在 C# 输入布局侧打补丁。
   - Don't try this again because: 会制造更多排障噪音，并掩盖真正的顶点空间问题。

2. **在本项目里使用 worktree 隔离 code review**
   - What we tried: 调起带 worktree isolation 的 reviewer 子代理。
   - Why it failed: 与用户“不要新建 worktree，以后也不要”的约束冲突，而且 reviewer 并没有看到主工作区的真实 shader 改动。
   - Lesson learned: 本项目后续不要再使用 worktree，包括子代理 isolation。
   - Don't try this again because: 既违反用户流程约束，也降低 review 的有效性。

---

## Problems Encountered & Solutions

### Problem 1: 河流 mesh 在屏幕空间闪烁并出现全屏大三角
**Symptom:** 河流 pass 在跑，但画面表现为屏幕空间闪烁或大面积错误三角形，而不是贴合地形的河道。
**Root Cause:** `RiverBottom.sdsl` / `RiverSurface.sdsl` 仅混入了 `Transformation` 常量，没有混入会真正计算 `PositionWS/PositionH/DepthVS` 的 `TransformationWAndVP`。
**Investigation:**
- Tried: 先检查水体公共函数、折射采样、blend/depth 配置。
- Tried: 用 RenderDoc 验证 pass 是否实际执行。
- Found: event 里的河流 VS 没有输出正确的裁剪空间位置。

**Solution:**
```csharp
shader RiverSurface : ShaderBase, TransformationWAndVP, RiverVertexStreams, RiverWaterCommon
```

**Why This Works:** 标准 mixin 会从 `streams.Position` 生成 `streams.PositionWS`，再乘 `Transformation.ViewProjection` 输出 `PositionH` 与 `DepthVS`。
**Pattern for Future:** 只要 shader 依赖 `PositionWS`、`DepthVS` 或标准裁剪空间输出，优先复用 Stride 的 transformation mixin，不要手搓并行体系。

### Problem 2: 排障阶段代码污染正式渲染路径
**Symptom:** `RiverRenderFeature` 中残留日志、动态输入布局与 `POSITION_WS` 特殊映射。
**Root Cause:** 为了快速定位 `CreateInputLayout` / 语义不匹配问题，临时把诊断逻辑直接放进正式渲染代码。
**Solution:**
```csharp
pipelineState.State.InputElements = RiverInputElements;
```

**Why This Works:** 在 shader 回归标准位置流后，固定输入布局与 `RiverVertex.Layout` 一致即可，不再需要 reflection 分支与日志噪音。
**Pattern for Future:** 排障补丁在确认根因后应立即删掉；不要让“曾经有用的诊断代码”留在正式渲染路径里。

---

## Architecture Impact

### Documentation Updates Required
- [x] 更新 `docs/ARCHITECTURE_OVERVIEW.md` - 加入河流渲染子系统现状与关键文件。
- [x] 更新 `docs/CURRENT_FEATURES.md` - 改正旧的河流 feature 记录，补充 River 渲染/网格/调试状态。
- [x] 新增 `docs/log/decisions/adr-014-river-rendering-architecture.md`。
- [x] 创建本次 session log。

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 自定义渲染子系统用“Component + Processor + RenderObject + RenderFeature + façade service”分层
- When to use: 需要独立 mesh 生命周期、多 pass 或 editor/runtime 桥接的渲染对象。
- Benefits: 生命周期清晰，调试和扩展边界明确。
- Add to: 架构概览与 ADR。

**New Anti-Pattern:** 用输入布局特殊映射替代 shader 标准变换链
- What not to do: 在 C# 侧通过 `POSITION -> POSITION_WS` 之类的特殊映射兜底。
- Why it's bad: 掩盖 shader 变换缺失的真正问题，容易把临时修复留进正式代码。
- Add warning to: 本 session log 与 ADR-014。

### Architectural Decisions That Changed
- **Changed:** 河流渲染接入方式
- **From:** editor service 驱动 + 临时渲染路径
- **To:** `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature`
- **Scope:** `Terrain.Editor` 的河流渲染与视口集成代码
- **Reason:** 需要正式承载双 pass 渲染、可见性同步和后续扩展

---

## Code Quality Notes

### Testing
- **Tests Written:** 无新增自动测试。
- **Coverage:** 本次主要通过构建、RenderDoc 与人工视口验证闭环。
- **Manual Tests:** 用户已确认“现在正常了”；清理排障残留后重新执行 `dotnet build Terrain.Editor.csproj -c Debug` 通过。

### Verification
- `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug` ✅ PASS
- 构建仍包含既有 NuGet vulnerability warnings 与少量项目内 warning；本次未处理。

### Technical Debt
- **Created:** 无新增明确技术债。
- **Paid Down:** 删除了 `RiverRenderFeature` 中的临时输入布局诊断逻辑。
- **TODOs:** 后续可为河流渲染补正式自动化可视验证或更细粒度 shader 回归测试。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 继续打磨河流视觉表现参数（泡沫、底色、折射强度）—— 现在已经有稳定渲染基线。
2. 视需要补充河流连接段/主支流视觉规则 —— 进一步贴近参考实现。
3. 如要加强回归保护，可为河流渲染增加自动化截图或 RenderDoc 断言流程。

### Blocked Items
- **Blocker:** 无硬阻塞。
- **Needs:** 如需更高保真视觉对齐，需要继续参考外部资源与真实截帧。
- **Owner:** 当前可继续由本仓库开发推进。

### Questions to Resolve
1. 是否需要为河流底部与水面 pass 引入更多独立材质参数集？ - 这决定后续 artist 调参边界。
2. 河流连接段是否需要单独几何/参数通道？ - 这影响交汇口视觉质量。

### Docs to Read Before Next Session
- [adr-014-river-rendering-architecture.md](../../decisions/adr-014-river-rendering-architecture.md) - 复习当前河流渲染分层与约束
- [2026-06-05-1-river-mesh-generation-fix.md](./2026-06-05-1-river-mesh-generation-fix.md) - 复习河流几何基线

---

## Session Statistics

**Files Changed:** 多个河流渲染相关代码文件 + 4 个文档文件
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 当前河流正式架构是 `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature`
- `RiverRenderingService` 只负责 editor façade，不再承担正式渲染管线职责
- 这次闪烁/大三角根因是 shader 没走 `TransformationWAndVP`
- `RiverRenderFeature` 里的 `POSITION_WS` 特殊映射与输入布局日志已被移除，不要再加回去

**What Changed Since Last Doc Read:**
- 河流渲染已从“研究/实现中”变成“正式接入 Stride 渲染架构”
- 文档状态从旧的 path/river 占位描述更新为当前真实结构

**Gotchas for Next Session:**
- 不要在本项目中创建新 worktree，包括子代理 isolation
- 不要把 `RiverWaterCommon` 或折射参数误判成这次顶点空间 bug 的根因
- 如果再出现类似空间错乱，先检查 shader 是否仍在标准 transformation 链路上

---

## Links & References

### Related Documentation
- [ADR-013: Vic3 风格路径渲染](../../decisions/adr-013-vic3-path-rendering.md)
- [ADR-014: 河流渲染架构](../../decisions/adr-014-river-rendering-architecture.md)

### Related Sessions
- [River Mesh 生成修复](./2026-06-05-1-river-mesh-generation-fix.md)
- [Vic3 河流 RenderDoc 调研归档](../../05/16/2026-05-16-1-vic3-river-renderdoc-research.md)

### Code References
- 关键实现：`Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- Shader 修复：`Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl`

---

## Notes & Observations

- 用户对目标约束一直很明确：不要 service-only，要完整河流渲染架构。
- 本次修复的价值不仅是“画面正常了”，更重要的是把河流正式纳入了可维护的渲染分层。
