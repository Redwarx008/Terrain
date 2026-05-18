# Vic3 河流 RenderDoc 调研归档
**Date**: 2026-05-16
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 分析 `C:\Users\Redwa\Desktop\vic3-river.rdc` 中 Vic3 河流是如何渲染的，并确认河流交汇的处理方式。

**Secondary Objectives:**
- 核对用户指出的 `2707 Draw(5958)` 与 `2715 Draw(1428)` 的职责
- 用 Vic3 游戏目录中的真实 shader 源码交叉验证 RenderDoc 结论
- 解释“为什么先画河底，但后面的 RT 里只看到混合后的水面结果”

**Success Criteria:**
- 明确河流 pass 拆分
- 明确交汇机制不是猜测而是有源码/截帧证据支撑
- 将结论沉淀到可复用文档

---

## Context & Background

**Previous Work:**
- Related: [vic3-road-river-rendering.md](../../learnings/vic3-road-river-rendering.md)
- Related: [adr-013-vic3-path-rendering.md](../../decisions/adr-013-vic3-path-rendering.md)

**Current State:**
- 项目内已有 Vic3 道路/河流渲染研究文档，但之前更偏总体机制。
- 本次补充了具体 RenderDoc draw 事件映射、交汇判断与折射缓冲链路。

**Why Now:**
- 用户正在对照 Vic3 的真实实现，提炼可迁移到 Terrain 路径/河流系统的渲染与交汇方案。

---

## What We Did

### 1. 分析 RenderDoc 捕获中的河流 draw 组
**Files Changed:** `docs/log/learnings/vic3-road-river-rendering.md`

**Implementation:**
- 使用 RenderDoc 工具核对 `2707` 和 `2715` 所属 pass。
- 向前追踪到对应河底 draw 组 `2392–2408`。
- 确认河流是“两组配对 draw”：河底一组，水面一组。

**Rationale:**
- 单看 `2707` / `2715` 会误以为它们就是完整河流实现；实际它们只是水面 pass。

### 2. 用游戏目录真实 shader 源码交叉验证
**Files Changed:** `docs/log/learnings/vic3-road-river-rendering.md`

**Implementation:**
- 读取 `E:\Victoria.3.v1.2.7\game\jomini\gfx\FX\jomini\jomini_river.fxh`
- 读取 `E:\Victoria.3.v1.2.7\game\jomini\gfx\FX\jomini\jomini_river_surface.fxh`
- 读取 `E:\Victoria.3.v1.2.7\game\jomini\gfx\FX\jomini\jomini_river_bottom.fxh`
- 读取 `E:\Victoria.3.v1.2.7\game\jomini\gfx\FX\jomini\jomini_water_default.fxh`

**Findings:**
- `DistanceToMain : TEXCOORD5` 由顶点输入直接提供。
- 水面与河底 shader 都用 `DistanceToMain` 做 connection fade。
- 河底输出 `CompressWorldSpace(WorldSpacePos)` 到 alpha，并使用 dual-source blending。
- 水面通过 `JominiRefraction` 采样前一阶段结果并解压世界坐标，做折射/水下混合。

### 3. 补充交汇与折射缓冲结论到研究文档
**Files Changed:** `docs/log/learnings/vic3-road-river-rendering.md`

**Implementation:**
- 增加 RenderDoc 事件映射表
- 增加“河流交汇处理”小节
- 增加“为什么先画河底但后面只看到混合结果”小节

**Rationale:**
- 这部分是本次最关键的新信息，后续实现 Stride 河流时会直接复用。

---

## Decisions Made

### Decision 1: 交汇结论以“几何拆段 + 顶点连接参数 + shader 渐隐”表述
**Context:** 用户重点关心 Vic3 的河流交汇如何实现。
**Options Considered:**
1. 交汇由单独 junction shader/draw 完成
2. 交汇由纯像素 mask/alpha 拼接完成
3. 交汇由预生成连接段网格 + `DistanceToMain` 渐隐完成

**Decision:** 选择选项 3
**Rationale:** RenderDoc 中未发现专门 junction shader；真实 shader 明确存在 `DistanceToMain`；短条带 draw 也符合连接段特征。
**Trade-offs:** 没有直接看到离线河网生成代码，所以“连接段由几何预先拆分”是高置信推断而不是源码级最终证明。
**Documentation Impact:** 已更新 `docs/log/learnings/vic3-road-river-rendering.md`

### Decision 2: 将“河底消失”解释为中间折射缓冲链路，而非被覆盖
**Context:** 用户观察到河底先画，但后续 RT 里单独河底不见了。
**Options Considered:**
1. 河底直接画到最终 RT，后续被水面覆盖
2. 河底画到中间缓冲，后续被水面采样合成

**Decision:** 选择选项 2
**Rationale:** 河底 shader 输出压缩世界坐标到 alpha；水面公共 shader 明确采样 `JominiRefraction` 并解压世界坐标。
**Trade-offs:** 需要同时结合 RenderDoc 和公共水体 shader 才能完整看懂链路。
**Documentation Impact:** 已更新 `docs/log/learnings/vic3-road-river-rendering.md`

---

## What Worked ✅

1. **截帧 + 源码交叉验证**
   - What: 先用 RenderDoc 锁定 draw，再回到真实 shader 文件确认语义。
   - Why it worked: 既能避免纯看反汇编误判，也能避免只看源码却不知道实际跑了哪条分支。
   - Reusable pattern: Yes

2. **沿着 alpha/中间 RT 数据链路追踪**
   - What: 从 `CompressWorldSpace` 追到 `DecompressWorldSpace`。
   - Impact: 直接解释了“河底为什么在最终 RT 里不单独可见”。

---

## What Didn't Work ❌

1. **一开始用错了 Vic3 shader 目录**
   - What we tried: 读取 `game/gfx/FX/...`
   - Why it failed: 本次相关 shader 实际位于 `game/jomini/gfx/FX/jomini/...`
   - Lesson learned: Paradox/Jomini 项目路径经常分层，先用 Grep 定位真实文件更稳。
   - Don't try this again because: 容易浪费时间并造成路径级误判。

---

## Problems Encountered & Solutions

### Problem 1: 用户指出截帧不是 Stride 项目
**Symptom:** 初始分析容易沿着当前仓库语境走偏。
**Root Cause:** 当前工作目录是 Terrain 仓库，但被分析对象是外部 Vic3 截帧与游戏资源目录。
**Investigation:**
- Tried: 先基于已有研究和截帧判断
- Found: 需要回到 `E:\Victoria.3.v1.2.7\game` 中的真实 shader 才能定案

**Solution:**
- 改为直接读取 Vic3 游戏目录的 shader 源码，并与 RenderDoc 事件一一对照。

**Why This Works:**
- 避免把仓库内已有结论当成最终证据。
**Pattern for Future:**
- 对外部捕获文件做研究时，优先建立“截帧事件 ↔ 实际 shader 文件”的映射。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/log/learnings/vic3-road-river-rendering.md` - 补充河流 draw 映射、交汇机制、折射缓冲链路
- [ ] Update `docs/ARCHITECTURE_OVERVIEW.md` - 当前仓库实现状态未变化，无需更新

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 外部渲染案例逆向时用“事件组 + 顶点语义 + 中间 RT 链路”三段式分析
- When to use: 分析第三方游戏/引擎渲染特性
- Benefits: 能同时看懂几何、shader 和跨 pass 数据流
- Add to: `docs/log/learnings/`

**New Anti-Pattern:** 只盯住用户指出的单个 draw 就下结论
- What not to do: 看到某个 draw 很像目标对象就直接归纳完整机制
- Why it's bad: 很容易漏掉前置 pass 或中间缓冲依赖
- Add warning to: `docs/log/learnings/`

---

## Code Quality Notes

### Testing
- **Tests Written:** 无
- **Coverage:** 本次为研究归档，无代码改动
- **Manual Tests:** 如需继续研究，可在 RenderDoc 中继续核对 `2392` 与 `2707` 的资源绑定对应关系

### Technical Debt
- **Created:** 无
- **Paid Down:** 补全了之前研究文档里缺失的交汇/折射链路说明
- **TODOs:** 如后续真正落地 Stride 河流交汇，可再写独立 ADR

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 将 Vic3 的 `DistanceToMain` 交汇渐隐机制转译为 Terrain 的河流网格顶点格式
2. 设计 Terrain 侧“主河段 + 连接段”生成规则
3. 如需要，继续核对 `JominiRefraction` 在截帧中对应的具体 RT 资源号

### Questions to Resolve
1. Terrain 的河流编辑数据结构里是否要显式存“连接段”节点？ - 这决定运行时网格生成策略
2. 是否需要复刻 Vic3 的双输出河底缓冲，还是用 Stride 现有材质/后处理链路替代？ - 这决定实现复杂度

### Docs to Read Before Next Session
- [vic3-road-river-rendering.md](../../learnings/vic3-road-river-rendering.md) - 复习本次归档结论
- [adr-013-vic3-path-rendering.md](../../decisions/adr-013-vic3-path-rendering.md) - 对齐当前项目的路径渲染方向

---

## Session Statistics

**Files Changed:** 2
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `2707` / `2715` 是河流水面 pass，不是完整河流实现
- 河底对应 `2392–2408`，水面对应 `2707–2723`
- 交汇靠 `DistanceToMain` + 连接段网格渐隐，不是 junction 专用 shader
- 河底先写入折射中间缓冲，水面再读取并混合

**What Changed Since Last Doc Read:**
- 研究文档新增了 draw 级别证据链
- 明确了交汇机制与中间 RT 数据流

**Gotchas for Next Session:**
- Vic3 相关 shader 实际在 `game/jomini/gfx/FX/jomini/`
- 不要把最终 RT 里“看不到单独河底”误判成河底 pass 没起作用
- 只看单个 draw 很容易漏掉前置 pass

---

## Links & References

### Related Documentation
- [Vic3 道路与河流渲染技术调研](../../learnings/vic3-road-river-rendering.md)
- [ADR-013: Vic3 风格路径渲染](../../decisions/adr-013-vic3-path-rendering.md)

### External Resources
- Vic3 shader source: `E:\Victoria.3.v1.2.7\game\jomini\gfx\FX\jomini\`
- RenderDoc capture: `C:\Users\Redwa\Desktop\vic3-river.rdc`

---

## Notes & Observations

- 用户的问题指向很准：2707/2715 的确是关键 draw，但必须连同前面的河底 pass 一起看。
- 本次归档内容已经足够作为后续 Terrain 河流交汇实现的参考基线。
