# River Surface CalcWater Strict Parity Brainstorm

**Date**: 2026-06-18  
**Session**: River surface strict parity brainstorming  
**Status**: 🔄 In Progress  
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 收敛当前河流 surface 与目标游戏 `CalcWater` 语义不等价问题的设计方案。

**Success Criteria:**
- 明确目标不是局部调色，而是完整 surface water 语义边界。
- 明确缺失输入必须补齐，不能长期 fallback。
- 明确资源复制到现有 `Terrain.Editor/Assets/River/` 路径，不新增外部产品名、产品缩写或“参考来源”类目录/符号。
- 明确先 RenderDoc hot-replace 验证，再落地 SDSL/C#。

---

## Context & Background

**Previous Work:**
- 上轮 RenderDoc 诊断确认 current raw refraction/bottom 与目标帧同量级，主要偏差集中在 surface。
- current `RiverSurface` 把 raw refraction 推成高饱和蓝青色；目标 surface 输出是低能量暗水色。
- current surface cbuffer 缺少完整 water wave / gloss / specular / foam / normal flatten 等参数组。

**Current State:**
- `Terrain.Editor/Assets/River/` 已有 `Water`、`Bottom`、`Environment` 三组中性资源路径。
- `.sdtex` 使用 sibling `.dds` source，runtime 通过 `River/...` asset URL 加载。
- 仓库已有大量未提交改动，本轮只新增设计文档与日志。

---

## What We Did

### 1. 使用 brainstorming 流程确认目标范围

**Files Changed:**
- `docs/superpowers/specs/2026-06-18-river-surface-calcwater-strict-parity-design.md`
- `docs/log/2026/06/18/2026-06-18-river-surface-calcwater-strict-parity-brainstorm.md`

**Result:**
- 用户选择严格逐项语义等价。
- 验收优先级为源码语义一致优先，目标 RDC 单帧作为第一道硬门。

### 2. 使用 visual companion 展示差距归因

**Files Changed:**
- `.superpowers/brainstorm/...` 临时 companion 页面。

**Result:**
- 展示 current surface 原始输出、current refraction-only、目标 bottom、目标 final surface。
- 用户继续推进，视为认可“问题集中在 surface `CalcWater` 语义”这个归因。

### 3. 收敛资源与命名约束

**Decision:**
- 资源临时复制到 `Terrain.Editor/Assets/River` 的现有结构。
- 不新增外部产品名、产品缩写或“参考来源”类命名。
- 项目代码、目录、资源名、shader 参数、日志标签都使用中性河流语义。

---

## Decisions Made

### Decision 1: 完整语义边界，分层激活

**Context:** current surface 缺少完整 water 参数组，直接改 SDSL 会把 shader 语义、资源绑定和 Stride asset 编译问题混在一起。

**Decision:** 先定义完整 `CalcWater` 端口边界，再通过 RenderDoc hot-replace 验证，最后落地 SDSL/C#。

**Rationale:** 保持严格等价目标，同时让每个阶段都有可验证硬门。

### Decision 2: 缺失输入必须补齐

**Context:** 早期设计曾允许 fog/FoW/cloud/shadow/environment 暂时 fallback。

**Decision:** fallback 只能作为热验证/迁移阶段短期工具。最终缺失项必须补齐真实输入或明确阻塞任务。

**Rationale:** 用户明确要求缺失项都要补上，并从外部游戏目录拿资源。

### Decision 3: 沿用现有 `Assets/River` 路径

**Context:** 项目已经有 `Water`、`Bottom`、`Environment` 中性路径和内容 URL。

**Decision:** 同名语义资源覆盖现有 `.dds`，保留 `.sdtex` 与 asset URL；新增资源也归入现有三类目录。

**Rationale:** 避免引入外部产品名或并行资源树，也降低 C# loader 变动面。

---

## What Worked ✅

1. **先展示 RenderDoc 证据再讨论方案**
   - 让方案讨论围绕已验证事实，而不是主观截图。

2. **把命名约束前置**
   - 避免后续实现中出现需要再重命名的目录、类型、参数和日志标签。

---

## What Didn't Work ❌

1. **最初提出带来源含义的并行命名**
   - 用户明确否定。
   - 以后相关项目内命名直接沿用 `River/Water`、`River/Bottom`、`River/Environment` 等中性路径。

---

## Next Session

### Immediate Next Steps

1. 用户审阅 `docs/superpowers/specs/2026-06-18-river-surface-calcwater-strict-parity-design.md`。
2. 获批后写 implementation plan。
3. 实施计划从 RenderDoc / resource extraction 开始，不直接改 SDSL。

### Docs to Read Before Next Session

- `docs/superpowers/specs/2026-06-18-river-surface-calcwater-strict-parity-design.md`
- 上轮 surface `CalcWater` 差距归因日志
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Quick Notes for Future Codex

**What Codex Should Know:**
- 当前目标是 strict surface water parity，不是调暗当前水色。
- 资源复制到现有 `Terrain.Editor/Assets/River/{Water,Bottom,Environment}`。
- 项目内命名不要出现外部产品名、缩写或“参考来源”类英文符号。
- 先 RenderDoc hot-replace，验证有效后才改 `RiverSurface.sdsl` / C#。

**Gotchas for Next Session:**
- 仓库有大量既有未提交改动，不能回滚。
- `.superpowers/` 是 brainstorming 临时输出，不应作为实现产物提交。
- Stride shader 参数或文件变更后必须按 shader asset workflow 跑 key 生成和 asset rebuild。
