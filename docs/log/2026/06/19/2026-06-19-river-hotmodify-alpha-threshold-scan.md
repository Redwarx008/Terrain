# River Hotmodify Alpha Threshold Scan
**Date**: 2026-06-19
**Session**: 10
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 按用户要求，停止继续改源码，先用 RenderDoc 热修改把 current bank 的翻支阈值卡清楚。

**Success Criteria:**
- 给出 current `276 RT0.a` 从 baseline 到 dark-bank 分支的阈值区间。
- 明确下一步该优先怀疑“大比例尺度错”还是“bottom depth/profile 略浅”。

---

## What We Did

### 1. 撤回刚才的源码试改，回到 hot-modify-only
**Files Changed:** 无运行时代码保留

**Implementation:**
- 撤回了刚才对 `RiverBottom.sdsl` 和 `RiverShaderTextTests.cs` 的预写回修改。
- 继续只在 `C:\Users\Redwa\Desktop\debug.rdc` 上做 `event 276` 的 PS replacement。

**Rationale:**
- 遵守“先热修改，再回源码”的工作顺序，避免把未验证的猜测落进仓库。

### 2. 继续扫描 `276 RT0.a` 的翻支阈值
**Files Changed:** 无

**Implementation:**
- 在 `event 276` 固定 `RT0.rgb = [0.317878, 0.221410, 0.138984]`、`RT1 = 1`
- 只改 `RT0.a`
- 已有结果：
  - baseline `9.67` -> `305` bank 约 `[0.3335, 0.2392, 0.1545]`
  - `11.0` -> `305` bank 约 `[0.1133, 0.0912, 0.0624]`
  - `12.0` -> `305` bank 约 `[0.0537, 0.0530, 0.0402]`
  - `20+` -> 落入更暗饱和区，结果基本稳定

**Rationale:**
- current bank 不是“必须抬很多倍才生效”。
- 只要比 baseline `9.67` 再深一点点，就会跨过 surface 内部的强衰减阈值。

---

## Decisions Made

### Decision 1: 当前主怀疑从“大尺度错误”收敛为“bottom depth/profile 略浅”
**Context:** 之前 `CK3 81.75 vs current 9.67` 容易让人误以为是数量级错误。
**Decision:** 不再把它当成“至少要乘 8 倍”的直接证据。
**Rationale:** 热修改显示 `11~12` 已足够让 current bank 翻到暗分支。

### Decision 2: 下一步仍优先查 `RiverBottom` depth/profile 语义缺口
**Context:** 最小有效改动量很小。
**Decision:** 继续优先怀疑 `CalcBottomDepth` 缺失的 CK3 advanced 项。
**Rationale:** 这更符合“轻微加深 payload 就翻支”的热修改现象。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 本轮按用户要求撤回了预先源码修改，当前结论全部来自 hot modify
- `276 RT0.a` 的 dark-bank 翻支阈值大约在 `11~12`
- 因此下一步更该先查 bottom depth/profile 轻微偏浅，而不是大比例 scale 错

**Gotchas for Next Session:**
- 不要再先写源码
- 先做下一轮 hot replace，验证 `CalcBottomDepth` 的 CK3 advanced 语义是否能自然把 bank alpha 从 `9.67` 推到 `11+`

---
