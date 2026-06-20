# River Bank Payload Survival Analysis
**Date**: 2026-06-19
**Session**: 7
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续分析更新后的 `C:\Users\Redwa\Desktop\debug.rdc`，确认 current bank-edge 为什么没有像 CK3 一样在 surface 阶段继续压暗。

**Success Criteria:**
- 证明剩余主因是在 `waterFade`、surface composition，还是 bank 上存活的 pre-bottom payload。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-updated-debug-rdc-pixel-chain-verification.md](./2026-06-19-river-updated-debug-rdc-pixel-chain-verification.md)
- See: [2026-06-19-river-refraction-source-and-camera-clamp-alignment.md](./2026-06-19-river-refraction-source-and-camera-clamp-alignment.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 已确认 current capture 的稳定链路是 `223 -> 248 -> 276 -> 305`。
- 河心像素上 `276` 已经主导最终颜色；剩余问题集中在 bank-edge。

---

## What We Did

### 1. 排除 `waterFade` 作为 bank 主因
**Files Changed:** 无

**Implementation:**
- 对 `event 305` 的 bank/interior 像素做 `debug pixel`：
  - bank `(280,768)` 输出 `o0 = [0.333542, 0.239202, 0.154507, 1]`
  - interior `(200,768)` 输出 `o0 = [0.0521452, 0.0473394, 0.0364698, 1]`
- 结合 [RiverSurface.sdsl](/abs/path/e:/Stride%20Projects/Terrain/Terrain.Editor/Effects/RiverSurface.sdsl:623)：
  - `OutputColor = float4(waterColor, waterFade);`

**Rationale:**
- 当前这两颗像素的 surface shader 输出 alpha 都是 `1`，说明这轮 bank 偏亮不是因为 `waterFade` 过低或被压到黑色 ramp。

### 2. 证明 bank 上 bottom 改的是 RGB，不是 alpha payload
**Files Changed:** 无

**Implementation:**
- 对半分辨率 bank / interior 像素复核 `248 -> 276`：
  - bank `(140,384)`：
    - `248 = [1.16211, 1.03516, 0.846191, 9.67969]`
    - `276 = [0.354736, 0.256836, 0.169922, 9.67188]`
  - interior `(100,384)`：
    - `248 = [1.07324, 0.923828, 0.735352, 9.92969]`
    - `276 = [0.0809937, 0.062561, 0.0447083, 10.4453]`

**Rationale:**
- bank 像素在 `248 -> 276` 上 RGB 明显变成了 bottom 颜色，但 alpha 几乎不变：`9.67969 -> 9.67188`。
- interior 像素则连 alpha 也被 bottom 继续改写：`9.92969 -> 10.4453`。
- 这说明 current bank-edge 上真正继续喂给 surface 的 distance payload 主要还是 seed/pre-bottom 的 alpha，不是 bottom 新写出的 payload。

### 3. 把 bank payload 结论与当前 bottom 输出链对上
**Files Changed:** 无

**Implementation:**
- [RiverBottom.sdsl](/abs/path/e:/Stride%20Projects/Terrain/Terrain.Editor/Effects/RiverBottom.sdsl:414)：
  - `alpha = bottomDiffuse.a * fadeOut * connectionFade * edgeFade1 * edgeFade2;`
  - `streams.ColorTarget = float4(color, compressedWorld);`
  - `streams.ColorTarget1 = float4(alpha, alpha, alpha, alpha);`
- bank 像素输入 `riverUv.y ~= 0.97374`，位于边缘淡出区，因此 secondary alpha 覆盖权重天然偏低。

**Rationale:**
- bank 区域 secondary alpha 小，说明 bottom pass 主要在改 RGB，而 RT0 alpha 很大概率继续保留了更多 seed/pre-bottom payload。
- 这和上面的 `248 -> 276` 实测现象完全一致。

### 4. 对位 CK3 旧证据，锁定 current bank payload 过浅
**Files Changed:** 无

**Implementation:**
- current bank：
  - `276` alpha 约 `9.67`
  - `305` final 约 `[0.3335,0.2392,0.1545]`
- 仓库内已记录的 CK3 bank：
  - bottom `338` bank 约 `[0.2712,0.1851,0.1000,81.75]`
  - surface `466` bank 约 `[0.0223,0.0280,0.0305]`

**Rationale:**
- current bank 的 surviving payload 量级远浅于 CK3 bank 记录的 `81.75`。
- 在 current `ApplyTerrainUnderwaterSeeThrough` 里，这会让 shallow-bank attenuation 更接近保留 bottomColor，而不是像 CK3 那样把颜色压回更暗的水体结果。

---

## Decisions Made

### Decision 1: bank 主问题从“surface 没压暗”进一步收敛为“bank seed/pre-bottom alpha payload 过浅且继续存活”
**Context:** 之前已经知道 current `305` bank 很接近 `276` bank，但还不清楚这是 `waterFade`、see-through，还是 payload 源头导致。
**Decision:** 认定这轮主因是 bank 像素上 surviving alpha payload 太浅。
**Rationale:** `waterFade` 已被 alpha=1 排除；`248 -> 276` 又明确显示 bank alpha 基本没变。

### Decision 2: 下一轮优先处理 bank 侧 pre-bottom/source payload，而不是继续调 `WaterFade`
**Context:** current bank `305` 继续沿用浅 payload，导致最终偏亮偏棕。
**Decision:** 下一轮优先拆 `RiverSceneSeed` / source RT 语义与 bank 覆盖传播。
**Rationale:** 如果 bank 真正用到的 payload 还停留在 current 的 `~9.68`，继续调 surface water constants 不会把它变成 CK3 那种暗 bank。

---

## What Worked ✅

1. **把 surface 输出 alpha 当成 `waterFade` 代理信号**
   - 当前 shader 结构下，这能很快排掉一条低价值怀疑链。

2. **同时比较 bank 与 interior 的 `248 -> 276` alpha 是否变化**
   - 这一步直接把“bottom 是否真的改写了 payload”说清楚了。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 补充 bank payload 存活诊断模式
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要
- [ ] Update `CURRENT_FEATURES.md` - 不需要

---

## Next Session

### Immediate Next Steps
1. 在 bank-edge 继续追 `223/248` 的 alpha 来源与 CK3 对位差距
2. 恢复 RenderDoc 持久会话后，优先做 bank-only 的 source/seed payload hot-edit
3. 只有 bank payload 对齐后，才继续细拆 `ApplyTerrainUnderwaterSeeThrough`

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- current bank surface 亮，不是 `waterFade` 主因
- bank `(140,384)` 在 `248 -> 276` 上 RGB 被 bottom 改写，但 alpha 基本不变
- current bank 更像是在用“浅 seed payload + bottom RGB”组合出最终颜色

**Gotchas for Next Session:**
- 不要再把 bank 问题首先归因到 `WaterFade`
- 不要只看 `276/305` 的 RGB；bank 上 alpha payload 是否变化同样关键

---
