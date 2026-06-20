# 河流 segment direction 归一补全与 bottom TBN RenderDoc 复核
**Date**: 2026-06-19
**Session**: river segment direction normalization from renderdoc bottom tbn
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续用 RenderDoc 热替换定位 `C:\Users\Redwa\Desktop\debug.rdc` 里 bottom 黑线的真实根因。

**Secondary Objectives:**
- 在不先改 SDSL 的前提下，确认问题到底是 shadow、bottom 贴图，还是 mesh/TBN 方向。
- 把确认后的修复落到 river graph / segment 方向归一，而不是 shader workaround。

**Success Criteria:**
- 用 RenderDoc 热替换给出可证伪的证据链。
- 如果根因落在 segment 方向，补最小代码修复和回归测试。

---

## Context & Background

**Previous Work:**
- Related: [2026-06-18-river-bottom-tangent-sign-correction.md](../18/2026-06-18-river-bottom-tangent-sign-correction.md)
- Related: [2026-06-19-river-junction-marker-priority.md](./2026-06-19-river-junction-marker-priority.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 之前一度把 bottom 发黑收敛到 shadow term，但本轮需要在当前 `debug.rdc` 上重新验证。
- `RiverMapService` 已修 junction marker priority，但 `Confluence->None` 主干段仍然存在。

**Why Now:**
- 用户明确要求 bottom/surface 都按 CK3 语义对齐，不能继续靠猜测决定是 shader 还是 mesh。

---

## What We Did

### 1. 用 RenderDoc 热替换重新拆 bottom shadow / direct / normal
**Files Changed:** none

**Implementation:**
- 重新打开 `C:\Users\Redwa\Desktop\debug.rdc`，定位 bottom draw `eventId=276`、黑点 `(494,255)`。
- replacement 依次验证：
  - `shadowProj.xy / shadowProj.z / waterSurfaceProj.z`
  - `compareDepth / atlasDepth / center SampleCmp`
  - exact 8-tap bottom shadow term
  - exact direct diffuse（不乘 shadow）
  - bottom diffuse sample
  - `surfaceNormal / current bottomNormal / flip tangent / flip bitangent` 的 `nDotL`

**Findings:**
- current capture 上 center compare 和 exact 8-tap shadow 都是 `1.0`，说明这份 `debug.rdc` 的黑点不是 shadow 压黑。
- exact direct diffuse（不乘 shadow）只有 `~[2.2e-6, 1.39e-6, 7.27e-7]`，说明 darkening 在 shadow 之前就发生了。
- bottom diffuse sample 约 `[0.0346, 0.0252, 0.0151]`，不是近黑。
- `surfaceNormal` 的 `nDotL ≈ 0.55`；current bottom normal 经过 normal-map + TBN 后只剩 `1e-5`。
- 单独翻 `bitangent` 后 `nDotL ≈ 0.77`；整条 `tangent` 链翻转后 `nDotL ≈ 0.85`。

**Rationale:**
- 这条证据链把问题从“shadow / albedo”正式收敛到“segment/tangent direction 与 CK3 语义不一致”。

### 2. 对照 current code，确认修复点应在 segment direction normalization
**Files Changed:** `Terrain.Editor/Services/RiverMapService.cs`

**Implementation:**
```csharp
private static void NormalizeDirection(RiverSegment seg)
{
    if (GetDirectionRank(seg.StartKind) > GetDirectionRank(seg.EndKind))
    {
        seg.Cells.Reverse();
        (seg.StartKind, seg.EndKind) = (seg.EndKind, seg.StartKind);
        (seg.StartNodeKey, seg.EndNodeKey) = (seg.EndNodeKey, seg.StartNodeKey);
    }
}
```

**Rationale:**
- `RiverMeshGenerator` 早就把 `TaperStart` 定义成 `Source/None`、`TaperEnd` 定义成 `Confluence/Bifurcation`，但旧 `NormalizeDirection()` 只处理 `Confluence->Source`。
- 这导致 `Confluence->None` / `Bifurcation->None` 段保留错误方向，`RiverMeshService` 的 tangent、parallax 和 flow 就会一起反。

### 3. 新增最小回归测试并跑完整测试集
**Files Changed:** `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 新增：
  - `confluence to none segment is reversed to flow into confluence`
  - `bifurcation to none segment is reversed to flow into bifurcation`

**Verification:**
- `dotnet run --project E:\Stride Projects\Terrain\Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug` ✅
- `dotnet build E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj -c Debug` ✅

---

## Decisions Made

### Decision 1: 不在 shader 里留 tangent/bitangent workaround
**Context:** RenderDoc 证明翻 `bitangent` 或整条 `tangent` 链都能把黑点打亮。

**Options Considered:**
1. 在 `RiverBottom.sdsl` 里固定翻 `bitangent`
2. 在 `RiverBottom.sdsl` 里固定翻 `tangent`
3. 维持 CK3 shader 语义，把修复落在 segment 方向归一

**Decision:** 选择 Option 3
**Rationale:** 用户要求 shader 语义完全参考 CK3；current root cause 也确实落在 mesh/graph direction，不应再用 shader fallback。
**Trade-offs:** 需要对 river graph 的方向语义更严格，后续若还有局部异常，需要继续查 segment ordering，而不是改 shader。

### Decision 2: `NormalizeDirection` 按 `Source/None -> Confluence/Bifurcation` 统一排序
**Context:** 现有 mesh taper 语义已经隐含了这条方向规则。

**Decision:** 用 rank 统一决定是否反转 segment。
**Rationale:** 这比单独枚举 `Confluence->Source` 更完整，也和现有 `TaperStart/TaperEnd` 契合。

---

## What Worked ✅

1. **用多轮热替换拆单变量**
   - What: 先拆 shadow，再拆 direct，再拆 diffuse sample，最后拆 `nDotL`。
   - Why it worked: 把“黑”从视觉描述收敛成了可量化的 `nDotL` 问题。

2. **把 RenderDoc 证据和 graph 语义对回 `NormalizeDirection`**
   - What: 不停留在 shader 侧翻符号，而是回到 `RiverMapService` 的段方向约定。
   - Why it worked: 最终修复点和用户要求的 CK3 shader 等价语义一致。

---

## What Didn't Work ❌

1. **继续沿用“这份 capture 是 shadow 把 bottom 压黑”的旧结论**
   - What we tried: 先按前一轮结论重新查 shadow compare。
   - Why it failed: 在当前 `debug.rdc` 上 center compare 和 exact 8-tap 都是 `1.0`。
   - Lesson learned: relative root cause 必须跟 capture 绑定，不能跨 capture 复用。

2. **把 `bitangent` 热替换当成最终修复**
   - What we tried: 单独翻 `bitangent` 确实让 `nDotL` 变亮。
   - Why it failed: 它没有解释 flow direction，也不是 CK3 shader 原语义。
   - Lesson learned: 如果整条 `tangent` 链翻转更合理，应优先查 graph / mesh 方向。

---

## Architecture Impact

### Documentation Updates Required
- [x] 更新 `docs/ARCHITECTURE_OVERVIEW.md`
- [x] 更新 `docs/CURRENT_FEATURES.md`
- [x] 更新 `docs/log/learnings/stride-river-rendering-patterns.md`

### New Patterns/Anti-Patterns Discovered
**New Pattern:** `NormalizeDirection` 必须和 `TaperStart/TaperEnd` 语义一致
- When to use: path/river graph 方向会直接驱动 tangent、parallax、flow、endpoint taper 的系统
- Benefits: 修复 lighting 和 flow 时不需要再靠 shader sign workaround
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Code Quality Notes

### Testing
- **Tests Written:** 2 条 river direction normalization 回归测试
- **Coverage:** `Confluence->None`、`Bifurcation->None` 两类 segment 的方向归一
- **Manual Tests:** RenderDoc 热替换已证实 current `debug.rdc` 的黑点会随着 tangent direction 修正而回亮；本轮未生成新的 post-fix capture

### Technical Debt
- **Paid Down:** `NormalizeDirection()` 只处理 `Confluence->Source` 的不完整规则
- **TODOs:** 生成一份新的 editor capture，确认 current 黑线段在正式 build 下已不再反向

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 生成新的 editor capture，复核 current 黑线段 bottom `nDotL` 和 surface flow direction
2. 对真实 `rivers.png` 抽样检查是否还存在方向异常但 endpoint rank 相同的 segment
3. 若仍有局部反向，再继续深挖 `Confluence <-> Bifurcation` 这类 rank 相同的特殊段

### Questions to Resolve
1. 真实资产里是否存在需要额外规则的 `Bifurcation <-> Confluence` 段？
2. runtime 路径是否也会复用同一套 segment direction 语义，还是还需要单独复核？

### Docs to Read Before Next Session
- `docs/log/learnings/stride-river-rendering-patterns.md`
- `docs/log/2026/06/18/2026-06-18-river-bottom-tangent-sign-correction.md`
- `docs/log/2026/06/19/2026-06-19-river-junction-marker-priority.md`

---

## Session Statistics

**Files Changed:** 6
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 当前 `debug.rdc` 的黑点不是 shadow；center compare 和 exact 8-tap 都是 `1.0`。
- 真实 darkening 是 current segment 方向错误导致的 bottom TBN 近乎背光：`surfaceNormal nDotL≈0.55`，current bottom normal `≈1e-5`，flip tangent full chain `≈0.85`。
- 正式修复点是 `RiverMapService.NormalizeDirection()`，不是 `RiverBottom.sdsl`。

**What Changed Since Last Doc Read:**
- Implementation: `NormalizeDirection` 从特殊 case 扩展成 rank-based 方向归一
- Testing: 新增 `confluence/bifurcation -> none` 两条回归测试
- Constraints: 没有新的 post-fix capture；RenderDoc 证据来自 hot replacement

**Gotchas for Next Session:**
- Watch out for: 不要再把“翻 bitangent 有效”直接落成 shader workaround
- Don't forget: `RiverMeshGenerator` 的 taper 语义已经隐含方向约定
- Remember: 当前未处理的边界更可能是 rank 相同的特殊 endpoint 组合

---

*Template Version: 1.0 - Based on Archon-Engine template*
