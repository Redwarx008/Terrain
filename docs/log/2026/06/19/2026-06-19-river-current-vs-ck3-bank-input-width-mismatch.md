# River Current Vs CK3 Bank Input Width Mismatch
**Date**: 2026-06-19
**Session**: 11
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续只用热修改和截帧证据，确认 current 与 CK3 bottom bank 差异到底落在哪个输入量级上。

**Success Criteria:**
- 把 current `event 276` 和 CK3 `event 338` 的 bottom bank shader 输入正式对齐。
- 判断剩余主差异更像 depth/profile 公式问题，还是 ribbon/world width 输入问题。

---

## What We Did

### 1. 对齐 current bottom bank 输入语义
**Implementation:**
- current `event 276` bank `(140,384)` 的 `debug pixel` summary：
  - `PositionWS = (14047.2, 12.6178, 4038.66)`
  - `riverUv.y = 0.978153`
  - `riverWidth(normalized) = 8.28315e-05`
  - `distanceToMain = 0.783333`
  - shader output `o0.a = 9.67241`
- 结合 current `RiverBottom` disasm：
  - `riverUv.x = v2.y`
  - `riverUv.y = v2.z`
  - `riverWidth = v2.w`
  - `distanceToMain = v3.x`
  - `worldWidth = RiverWidth * MapExtent * 2`

### 2. 对齐 CK3 bottom bank 输入语义
**Implementation:**
- CK3 `event 338` bank `(55,369)` 的 `debug pixel` summary：
  - `v1.xy = UV = (0.0557563, 1.0)`
  - `v1.z = Transparency = 1`
  - `v1.w = Width = 1.46506`
  - `v2.w = DistanceToMain = 1`
  - `v4.xyz = WorldSpacePos = (7575.1, 4.29509, 2967.28)`
  - shader output `o0.a = 81.7683`
- 用 MCP 取 `338` 的 VS/PS signature 和 VS disasm 后确认：
  - `Out.UV = Input.UV`
  - `Out.Transparency = Input.Transparency`
  - `Out.Width = Input.Width * max(MapSize)`
  - `Out.DistanceToMain = Input.DistanceToMain`
  - `Out.WorldSpacePos = Input.Position`

### 3. 锁定 current 与 CK3 的 width 量级差异
**Implementation:**
- current bank 的 `worldWidth` 由输入与默认参数可还原为约 `0.339`
- CK3 bank 的 `Input.Width` 是 `1.46506`
- 也就是说，CK3 bank 的 bottom width 输入量级约为 current 的 `4.3x`

**Rationale:**
- 之前热修改已经证明 current `RT0.a` 只需要从 `9.67` 抬到 `11~12` 就会翻到 dark-bank 分支。
- 现在又看到 current vs CK3 在 bank 的 width 输入量级上相差 `4x+`，这比“需要的最小阈值改动”大得多。
- 因此当前最值得优先怀疑的已不是 surface，也不再是 `_BankAmount/_DepthWidthPower` 这类默认值项，而是 current ribbon width / geometry input 本身偏小。

---

## Decisions Made

### Decision 1: `_BankAmount/_DepthWidthPower` 不是当前第一怀疑项
**Context:** current render settings 里 `_BankAmount=0`、`_DepthWidthPower=2`，与当前简化 depth 基本等价。
**Decision:** 不再优先围着这两个参数做热修改。
**Rationale:** 即使补回 CK3 advanced profile，这两项在 current 默认设置下也不会显著改变 bank depth。

### Decision 2: 下一步优先追 current bank width 为什么只有 CK3 的约四分之一
**Context:** current bank `worldWidth≈0.339`，CK3 bank `Width≈1.46506`。
**Decision:** 继续查 mesh/segment width 来源，而不是继续回 surface。
**Rationale:** 这个量级差异已经足以解释为什么 current payload 需要被额外“补深”才能跨过 dark-bank 阈值。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- current `event 276` bank 的 bottom 输入宽度量级约 `0.339`
- CK3 `event 338` bank 的 bottom 输入宽度量级约 `1.465`
- 两者相差约 `4.3x`
- current bank 只要把 `RT0.a` 从 `9.67` 推到 `11~12` 就会翻到暗分支
- 因此 width/geometry input mismatch 比 surface 或 advanced depth 参数更值得优先追

---
