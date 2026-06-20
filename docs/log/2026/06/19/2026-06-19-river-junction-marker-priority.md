# 河流 junction marker 优先级修正
**Date**: 2026-06-19
**Session**: river junction marker priority
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续追 `debug.rdc` 里 bottom 黑线对应的 river mesh / segment 语义，确认 `RiverMapService -> RiverMeshService` 是否存在真实拓扑错误。

**Secondary Objectives:**
- 为 river graph 提取增加最小回归测试。
- 把真实 `rivers.png` 上已经确认的 segment 提取缺陷写回文档。

**Success Criteria:**
- 用真实 `rivers.png` 和最小 fixture 证明当前提取器会在 junction 前一格提前终止。
- 在不改 SDSL 的前提下修正该提取规则并通过测试。
- 明确说明该修正与 current `debug.rdc` 黑线根因的关系。

---

## Context & Background

**Previous Work:**
- Related: [2026-06-18-river-bottom-tangent-sign-correction.md](../18/2026-06-18-river-bottom-tangent-sign-correction.md)
- Related: [2026-06-19-river-bottom-shadow-port-and-balance-recheck.md](./2026-06-19-river-bottom-shadow-port-and-balance-recheck.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- RenderDoc 当前 `C:\Users\Redwa\Desktop\debug.rdc` 的 bottom 黑线像素 world position 约落在 `rivers.png` 的 `(2993,549)` 附近。
- 该位置映射到当前提取结果中的 segment `665`，类型是 `Confluence -> None`。
- 继续只盯 shader 已经不够，需要确认 graph / segment 本身是否在真实 `rivers.png` 上失真。

**Why Now:**
- 用户明确要求 bottom / surface 都按 CK3 语义对齐；如果 river graph 自己丢了 semantic endpoint，后面的 taper / flow / tangent 诊断都站不稳。

---

## What We Did

### 1. 用真实 `rivers.png` 复盘当前 segment 提取统计
**Files Changed:** none

**Findings:**
- 真实 `game/map_data/rivers.png` 在旧规则下提取出 `1651` 个 segment。
- 其中 `EndKind` 几乎全部退化成 `None`：
  - `Confluence -> None`: `980`
  - `Source -> None`: `630`
  - `Bifurcation -> None`: `40`
  - 只有 `1` 条 `Source -> Confluence`
- RenderDoc 黑线样本对应的 `(2993,549)` 落在 segment `665` 上；这条 segment 从 `(2931,538)` 的 `Confluence` 出发，沿着当前 centerline / tangent 一直走到 `(3092,468)`。

**Rationale:**
- 这说明当前提取器不只是“个别 branch 方向奇怪”，而是在真实资产上系统性丢失了大量 semantic endpoint。

### 2. 建立最小回归测试
**Files Changed:** `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 新增 `branch honors adjacent confluence marker before side continuation`。
- fixture 构造了一个最小拓扑：branch 的最后一个 `River` 像素同时邻接 `Confluence` marker 和 side continuation。
- 预期行为：segment 仍应优先以 `Confluence` 结束，而不是提前停成 `EndKind.None`。

**Red Result:**
- 新测试在实现前失败：
  - `end kind: expected Confluence, actual None`

### 3. 修正 `TracePath` 的下一步选择规则
**Files Changed:** `Terrain.Editor/Services/RiverMapService.cs`

**Implementation:**
```csharp
if (neighborType is RiverPixelType.Source or RiverPixelType.Confluence or RiverPixelType.Bifurcation)
{
    nextX = nx; nextY = ny;
    specialNeighborCount++;
}
else if (cells[nx, ny].IsFilled)
{
    if (specialNeighborCount == 0)
    {
        nextX = nx; nextY = ny;
    }
    filledNeighborCount++;
}

if (specialNeighborCount == 1)
{
    px = cx; py = cy;
    cx = nextX; cy = nextY;
    continue;
}
```

**Rationale:**
- 真实 `rivers.png` 里，branch 的最后一个 `River` 像素可能同时碰到 semantic marker 和 side continuation。
- 旧逻辑只认“排除来路后 filled neighbor 必须唯一”，因此会在 junction 前一格提前断开。
- 新逻辑明确把“唯一相邻 semantic marker”视为更高优先级的下一步。

### 4. 重新验证并复盘真实资产统计
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/learnings/stride-river-rendering-patterns.md`

**Verification:**
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug` ✅
- 新测试通过。

**Real-asset effect (same logic replayed on `rivers.png`):**
- 仍然是 `1651` 个 segment，但 semantic endpoint 明显恢复：
  - `EndKind.None`: `1289`
  - `EndKind.Confluence`: `345`
  - `EndKind.Bifurcation`: `17`
  - `Source -> Confluence`: `213`
- 这说明问题不是“真实资产没有 endpoint”，而是旧提取规则把它们截断了。

---

## Decisions Made

### Decision 1: 在 junction 邻域优先踏入唯一相邻 semantic marker
**Context:** 真实 `rivers.png` 并不保证 marker 恰好位于“唯一后继”的理想拓扑中心。

**Options Considered:**
1. 继续要求普通 filled neighbor 唯一
2. 在提取结束后靠启发式补回 endpoint
3. 在 `TracePath` 当下就优先踏入唯一相邻 semantic marker

**Decision:** 选择 Option 3
**Rationale:** 这是最局部、最可证伪、也最贴合真实资产的修正点。
**Trade-offs:** 它恢复了 semantic endpoint，但不自动证明 tangent / flow 已和 CK3 完全一致。

### Decision 2: 明确把这次修正和 `debug.rdc` 黑线根因分开
**Context:** RenderDoc 黑线样本对应的 segment `665` 本身就是 `Confluence -> None` 主干 continuation。
**Decision:** 文档中明确写明：这次 graph 修正不是 current 黑线的最终根因。
**Rationale:** 避免把“修好了一个真实提取 bug”误报成“bottom 黑线已解决”。

---

## What Worked ✅

1. **先把 RenderDoc world position 映射回 `rivers.png`**
   - What: 用黑线像素的 `PositionWS.xz * 0.5` 对回真实 river cell。
   - Why it worked: 把 GPU 现象直接锚到 segment graph，而不是停留在 shader 猜测。

2. **用最小 fixture 锁住真实资产里的拓扑坑位**
   - What: 单独构造“marker 邻接 side continuation”的回归测试。
   - Why it worked: 精准复现旧 `TracePath` 的提前终止行为。

---

## What Didn't Work ❌

1. **把黑线直接归因成“segment 方向反了”**
   - What we tried: 先从 RenderDoc 的 tangent 反事实出发，想把问题直接收敛到 segment 方向。
   - Why it failed: 黑线样本所在的 segment `665` 是 current graph 里的 downstream continuation；这次修正恢复的是 branch endpoint，不是这条主干的最终黑线根因。
   - Lesson learned: graph 提取 bug 和当前可见黑线可以同时存在，但不一定是同一个 root cause。

---

## Architecture Impact

### Documentation Updates Required
- [x] 更新 `docs/ARCHITECTURE_OVERVIEW.md` - 记录 `TracePath` 的 marker-priority 规则
- [x] 更新 `docs/CURRENT_FEATURES.md` - 记录河流网格生成的新提取语义
- [x] 更新 `docs/log/learnings/stride-river-rendering-patterns.md` - 增加 junction marker 优先级模式

### New Patterns/Anti-Patterns Discovered
**New Pattern:** junction 邻域优先踏入唯一相邻 semantic marker
- When to use: color-map path extraction，且 marker 可能偏离理想分叉中心时
- Benefits: 恢复真实 branch 的 semantic endpoint，避免大量 `EndKind.None`
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Code Quality Notes

### Testing
- **Tests Written:** 1 条新的 river extraction 回归测试
- **Coverage:** branch 在 marker 邻接 side continuation 时，仍应以 `Confluence` 结束
- **Manual Tests:** 尚未抓新的 editor frame；本轮只做 graph 提取修正，不声称画面已修复

### Technical Debt
- **Paid Down:** `TracePath` 在真实 `rivers.png` 上系统性丢 semantic endpoint 的问题
- **TODOs:** 继续追 current `debug.rdc` 黑线 segment `665` 的 bottom/world-UV/TBN 问题

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 继续追 current `debug.rdc` 黑线 segment `665` 的 bottom 采样 / TBN / bank profile 问题
2. 对比修正后的真实 graph，确认 surface 流向异常是否还集中在 `Confluence -> None` 主干 segment
3. 如果要改 shader，继续遵守“先 RenderDoc 热验证，再改 SDSL”

### Questions to Resolve
1. current 黑线为何仍集中在 segment `665`，即使它现在已经是 graph 上较可信的 downstream continuation？
2. bottom 暗线更像是 bank-profile handedness、world-UV 相位，还是别的 mesh-level 约定差异？

### Docs to Read Before Next Session
- `docs/log/learnings/stride-river-rendering-patterns.md`
- `docs/log/2026/06/18/2026-06-18-river-bottom-tangent-sign-correction.md`
- `docs/log/2026/06/19/2026-06-19-river-bottom-shadow-port-and-balance-recheck.md`

---

## Session Statistics

**Files Changed:** 6
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 本轮修的是 `RiverMapService.TracePath` 的 semantic marker 优先级，不是 shader。
- 真实 `rivers.png` 在旧规则下几乎所有 segment 都以 `EndKind.None` 结束；新规则能恢复数百条 `Confluence/Bifurcation` endpoint。
- current `debug.rdc` 的黑线样本仍在 segment `665` 上，这次修正不等于画面已好。

**What Changed Since Last Doc Read:**
- Implementation: `TracePath` 现在优先踏入唯一相邻 semantic marker
- Testing: 新增 `branch honors adjacent confluence marker before side continuation`
- Constraints: 继续不要把 graph 修正直接等同于 bottom 黑线修复

**Gotchas for Next Session:**
- Watch out for: `Confluence -> None` 主干 continuation 仍然存在，不能一刀切全反向
- Don't forget: CK3 capture 里的 sampled tangent 也是负 `Y`，不支持“全局 tangent 取反”这种粗暴结论

---
