# River Bottom Alpha Threshold And Depth Mismatch
**Date**: 2026-06-19
**Session**: 9
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续用 RenderDoc 热修改确认 current bank 最终颜色究竟受哪一段 payload 支配，并把剩余 shader 代码差异继续收敛到可修复点。

**Success Criteria:**
- 证明 `248` seed alpha 还是 `276` bottom alpha 才是 surface 真正读取的关键 payload。
- 找到 current `RiverBottom` 与 CK3 `CalcRiverBottomAdvanced` 的明确源码不等价点。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-ck3-shader-pass-equivalence-analysis.md](./2026-06-19-river-ck3-shader-pass-equivalence-analysis.md)
- See: [2026-06-19-river-bank-payload-survival-analysis.md](./2026-06-19-river-bank-payload-survival-analysis.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 已知 current `305` 与 CK3 `466` 的 `CalcWater` 主公式基本同构。
- 剩余主怀疑点落在 pre-surface payload。

---

## What We Did

### 1. 先验证 `248` seed alpha 是否会直接影响最终 bank
**Files Changed:** 无

**Implementation:**
- 对 current `248` 做 HLSL replacement：
  - 保持原 scene-seed RGB 压缩
  - 强行输出 `alpha = 80`
- 复核 pixel history：
  - `249` bank `(140,384)` 确实拿到了 `alpha = 80`
  - 但 `276` bank post 值又回到 `alpha = 9.671875`
  - `305` bank 最终色保持原值约 `[0.3335, 0.2392, 0.1545]`

**Rationale:**
- 这直接证明 surface 最终读取的关键不是 `248` seed alpha，而是 `276` bottom 最终写进 `BottomColor` 的 `RT0.a`。

### 2. 直接热改 `276` bottom 的 `RT0.a`，确认 surface 会立刻翻到暗支路
**Files Changed:** 无

**Implementation:**
- 对 current `276` 做 HLSL replacement，固定输出：
  - `RT0.rgb = [0.317878, 0.221410, 0.138984]`
  - `RT1 = 1`
  - 只改 `RT0.a`
- 结果：
  - `alpha = 80` 时，`305` bank `(280,768)` 变为约 `[0.00513, 0.02145, 0.02198]`
  - `alpha = 40` 时，结果几乎相同
  - `alpha = 20` 时，结果仍几乎相同
  - `alpha = 12` 时，结果变为约 `[0.05373, 0.05299, 0.04015]`
  - baseline `alpha = 9.67` 时，是 `[0.33354, 0.23920, 0.15451]`

**Rationale:**
- current bank 最终亮度对 bottom `RT0.a` 极其敏感，而且阈值很陡。
- 只要 bottom distance payload 从 `9.67` 略微抬到 `~12`，surface 就已经切到明显更暗的分支。

### 3. 对位源码后，定位到 current `CalcBottomDepth()` 仍是简化版
**Files Changed:** 无

**Implementation:**
- current [RiverBottom.sdsl](/abs/path/e:/Stride%20Projects/Terrain/Terrain.Editor/Effects/RiverBottom.sdsl:67)：
  - `CalcBottomDepth(float2 tangentUv)` 仍是：
    - 纯 `cos` 剖面
    - 没有 `_BankAmount`
    - 没有 `_DepthWidthPower`
    - 没有 `1 - BottomNormal.b`
    - 没有 `clamp(depth, 0.001, 10.0)`
- CK3 [jomini_river.fxh](/abs/path/e:/SteamLibrary/steamapps/common/Crusader%20Kings%20III/jomini/gfx/FX/jomini/jomini_river.fxh:88)：
  - advanced `CalcDepth(UV, BottomNormal)` 包含上述全部项
- current [RiverBottom.sdsl](/abs/path/e:/Stride%20Projects/Terrain/Terrain.Editor/Effects/RiverBottom.sdsl:398) 计算 `worldDepth` 时用的仍是这个简化 `CalcBottomDepth(tangentUv)`。
- 另外，current 声明了 `BottomDepthTexture`，但在整个 bottom shader 中完全未使用。

**Rationale:**
- `worldDepth -> bottomWorldPosition.y -> RiverCompressWorldSpace(...) -> RT0.a`
  这条链正是 current bank payload 过浅的直接来源。
- 当前 bottom 深度剖面与 CK3 advanced path 并不等价，这已经是明确的代码缺口，不再只是“观感差异猜测”。

---

## Decisions Made

### Decision 1: 推翻“bank 主要沿用 seed alpha”的旧说法
**Context:** 早先 bank `248 -> 276` 上 alpha 变化很小，看起来像在沿用 seed payload。
**Decision:** 改判为“bottom 总是重写 RT0.a，只是旧 seed alpha 与旧 bottom alpha 恰好接近”。
**Rationale:** `248 alpha = 80` 热改后，`276` 仍写回 `9.67`，证据充分。

### Decision 2: 剩余修复入口优先落在 `RiverBottom` 深度剖面，不再先碰 `RiverSurface`
**Context:** `276 RT0.a` 轻微变深就能让 `305` 立刻压暗。
**Decision:** 优先修 current `CalcBottomDepth` 的 CK3 advanced 不等价项。
**Rationale:** 这是最接近热修改证据、且能直接改变 bottom payload 的源码入口。

---

## What Worked ✅

1. **把热修改目标从 seed 切到 bottom alpha**
   - What: 先试 `248`，再直接试 `276`
   - Why it worked: 很快把“谁在真正支配 payload”说清楚了
   - Reusable pattern: Yes

2. **做 alpha 阈值扫描而不是只试一个极值**
   - What: 依次试 `80/40/20/12`
   - Impact: 证明 current bank 问题是阈值翻支，不是线性亮度比例问题

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md`
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要
- [ ] Update `CURRENT_FEATURES.md` - 不需要

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 先热改 bottom `RT0.a`，再决定是不是继续查 surface
- When to use: surface 公式已基本对齐，但 bank 明暗仍不对
- Benefits: 能快速区分“surface 常量问题”和“bottom payload 问题”
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 把 current `CalcBottomDepth()` 按 CK3 advanced depth 语义补齐到热修改可验证版本
2. 重新截帧确认 bank `276` 的 `RT0.a` 是否自然从 `~9.67` 提升到更接近 dark-bank 阈值
3. 只有 bottom payload 修完仍不对，再回头查 surface wrapper

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `248` seed alpha 不是 current surface 最终读取的关键 payload
- `276` 的 `RT0.a` 才是 current bank 明暗的主开关
- current `CalcBottomDepth()` 仍是简化版，和 CK3 advanced depth 明确不等价
- `BottomDepthTexture` 当前完全没用上

**Gotchas for Next Session:**
- 不要再把 bank 问题先归因到 `RiverSurface.sdsl`
- 不要再把 `81.75 vs 9.67` 当作必须做 8 倍缩放的直接证据
- 要先修 bottom depth profile，再判断是否还要做 scene/world scale 调整

---
