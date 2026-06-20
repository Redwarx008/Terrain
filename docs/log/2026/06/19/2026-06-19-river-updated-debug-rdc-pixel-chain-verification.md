# River Updated Debug RDC Pixel Chain Verification
**Date**: 2026-06-19
**Session**: 6
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用用户更新后的 `C:\Users\Redwa\Desktop\debug.rdc` 把 current `223 -> 248 -> 276 -> 305` 串成同一颗河道像素的证据链。

**Secondary Objectives:**
- 纠正旧事件号/旧像素带来的误判。
- 判断当前剩余偏差更像是 source RT、bottom，还是 surface bank attenuation。

**Success Criteria:**
- 找到一组稳定命中的 river interior / bank-edge 像素。
- 给出每层 RT 的代表数值。
- 明确下一轮该继续拆哪一层。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-updated-debug-rdc-postcolor-verification.md](./2026-06-19-river-updated-debug-rdc-postcolor-verification.md)
- See: [2026-06-19-river-refraction-source-and-camera-clamp-alignment.md](./2026-06-19-river-refraction-source-and-camera-clamp-alignment.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- `renderdoc-mcp` 本轮 `open_capture` 直接 `Transport closed`，无法继续走 MCP 持久会话。
- 已知新 capture 的事件链是 `223 -> 248 -> 276 -> 305`，但还缺同一颗河道像素的逐层证据。

**Why Now:**
- 只有把同一颗像素串起来，才能区分“surface 自己压黑了”与“bottom 已经是主导层，只是 bank 没被 CK3 那样压下去”。

---

## What We Did

### 1. 用 CLI 重建当前 capture 基本信息与资源链
**Files Changed:** 无

**Implementation:**
- `renderdoc-cli info`
  - `Total events: 78`
  - `Total draws: 65`
- `renderdoc-cli pipeline -e 223/248/276/305`
  - `223` / `305` 都写全分辨率 `RT 4059 (1672x996, R16G16B16A16_FLOAT)`
  - `248` 写半分辨率 `RT 7799 (836x498)`
  - `276` 写半分辨率 `RT 7802 (836x498)`
- `renderdoc-cli usage`
  - `4059`: `223 ColorTarget -> 248 PS_Resource -> 305 ColorTarget`
  - `7799`: `248 ColorTarget -> 249 CopySrc`
  - `7802`: `249 CopyDst -> 276 ColorTarget -> 305 PS_Resource`

**Rationale:**
- 先确认 current 真正的 GPU 链路，再做像素追踪；否则只会继续把旧 `251` 之类的事件号套到新 capture 上。

### 2. 用整图差分锁定同一颗河心像素
**Files Changed:** 无

**Implementation:**
- 重新导出：
  - `223` → `artifacts/renderdoc/debug_20260619_163305_cli/rt_223_0.png`
  - `248` → `.../rt_248_0.png`
  - `276` → `.../rt_276_0.png`
  - `305` → `.../rt_305_0.png`
- 对半分辨率 `248 -> 276` 做图像差分，最强变化点命中 `(97,384)`。
- 对全分辨率 `223 -> 305` 做图像差分，最强变化点命中 `(194,768)` 一带，正好对应半分辨率点的 `x2` 映射。
- 代表像素：
  - half-res `248 @ (97,384)` = `[1.14941, 1.01953, 0.834473, 9.94531]`
  - half-res `276 @ (97,384)` = `[0.0234375, 0.0249939, 0.0239563, 10.5391]`
  - full-res `223 @ (194,768)` = `[3.21484, 2.08594, 1.23145, 1]`
  - full-res `305 @ (194,768)` = `[0.0243378, 0.0294189, 0.0269928, 1]`

**Rationale:**
- 这条链直接证明：河心像素上，`223/248` 确实是高亮 HDR scene/scene-seed，而真正把像素压到暗水量级的是 `276` bottom；`305` 只是基本沿用了这个暗底。

### 3. 扫一条横截面，确认 bank-edge 剩余问题不在 bottom
**Files Changed:** 无

**Implementation:**
- 沿同一条横截面采样：
  - river interior 代表点：
    - `276 @ (100,384)` = `[0.08099, 0.06256, 0.04471, 10.4453]`
    - `305 @ (200,768)` = `[0.05212, 0.04733, 0.03647, 1]`
  - bank-edge 代表点：
    - `276 @ (140,384)` shader out = `[0.317878, 0.221410, 0.138984, 9.67241]`
    - `305 @ (280,768)` shader out = `[0.333542, 0.239202, 0.154507, 1]`
  - 河外点：
    - `276 @ (160,384)` 与 `248` 完全相同
    - `305 @ (320,768)` 与 `223` 完全相同
- 对照仓库里之前已记录的 CK3 bank 证据：
  - CK3 bottom `338` bank 约 `[0.2712, 0.1851, 0.1000]`
  - CK3 surface `466` bank 约 `[0.0223, 0.0280, 0.0305]`

**Rationale:**
- 这说明 current 的 bank-edge 剩余问题不是 “bottom 完全错了”。
- current `276 -> 305` 在 bank 像素上几乎没有像 CK3 那样继续大幅压暗；剩余偏差更集中在 surface 的 bank/refraction attenuation 语义，而不是 post-color 链，也不是河心 bottom 主色。

---

## Decisions Made

### Decision 1: 继续把 `223` 视为不等价 source，但不再把它当成当前河心主导问题
**Context:** `223` 的确是亮 HDR scene RT，但代表河心像素在 `276` 已经被压到暗水量级，`305` 基本沿用 `276`。
**Options Considered:**
1. 继续把 current 与 CK3 的主要偏差都归因到 `223` source RT
2. 保留 “source 仍不等价” 结论，但把当前视觉剩余问题拆到 bank-edge surface attenuation

**Decision:** 选择 2
**Rationale:** 同一颗河心像素的 `223 -> 248 -> 276 -> 305` 证据已经表明，source RT 不是 current 河心最终颜色的主导层。

### Decision 2: 下一轮优先拆 bank-edge 的 `CalcRefraction/see-through/WaterFade`，而不是回头查 post-color
**Context:** current bank `305` 仍接近 bottom，而 CK3 bank `466` 远暗于 bottom `338`。
**Options Considered:**
1. 再回去怀疑 `HeightLookup/PackedHeight/FoW`
2. 再回去怀疑 bottom 主采样/BRDF
3. 直接把 surface bank attenuation 当成下一轮主查对象

**Decision:** 选择 3
**Rationale:** 新 capture 已经证明 post-color 链不再参与主色；而 current bottom bank 已接近 CK3 bottom 量级，真正没发生的是 CK3 式的 `bottom -> surface` 再压暗。

---

## What Worked ✅

1. **先做 RT 差分，再 pick pixel**
   - What: 先用 `248 -> 276` 和 `223 -> 305` 整图差分锁像素，再做 CLI 采样。
   - Why it worked: 避免继续在河外或 coverage 边界上 pick 到“完全没变化”的假阴性点。
   - Reusable pattern: Yes

2. **把 half-res 点映射到 full-res 点**
   - What: 用半分辨率命中的最强变化点乘 `2` 去找全分辨率对应像素。
   - Impact: 很快把 `248/276` 与 `223/305` 串成同一条证据链。

---

## What Didn't Work ❌

1. **继续依赖 `renderdoc-mcp` 持久会话**
   - What we tried: 重新 `open_capture`
   - Why it failed: MCP transport 直接关闭
   - Lesson learned: 当前工具状态下，静态导出/采样应立刻切回 `renderdoc-cli`
   - Don't try this again because: 会浪费时间在恢复 transport，而不是继续做证据提取

---

## Problems Encountered & Solutions

### Problem 1: 旧“surface 自己把 bottom 压黑”的结论不再可靠
**Symptom:** 旧日志里曾出现 “bottom 亮、surface 黑” 的判断。
**Root Cause:** 用户更新 capture 后事件号漂移；如果还沿用旧事件号或没命中同一颗 river 像素，就会把 scene-seed 或河外背景误当成 bottom/surface 结论。
**Investigation:**
- Tried: 直接沿用旧 `251`
- Tried: 单点 pick
- Found: 必须先重建 `223 -> 248 -> 276 -> 305`，再用差分锁定代表像素

**Solution:**
- 先 `pipeline/usage/export-rt`
- 再 `248 -> 276` / `223 -> 305` 整图差分
- 最后用对应点 pick/debug

**Why This Works:** 它把“事件号漂移”和“像素没命中”这两个最容易污染结论的变量先排掉了。
**Pattern for Future:** 只要 capture 更新，先重建 pass map，再重建代表像素。

### Problem 2: 剩余主问题位于 bank-edge surface attenuation，而不是河心 bottom 主色
**Symptom:** current surface 仍明显比 CK3 宽、更棕、更亮。
**Root Cause:** current bank 像素 `305` 基本沿用了 `276` 的底色，没有出现 CK3 那种 `338 -> 466` 的强压暗。
**Investigation:**
- Found: river interior 上 `305 ≈ 276`
- Found: bank-edge 上 `305` 也仍接近 `276`
- Cross-check: 旧 CK3 bank 记录里 `466 << 338`

**Solution:**
- 下一轮优先拆 `RiverSurface.sdsl` 中 bank-edge 相关的：
  - `CalcRefraction`
  - `ApplyTerrainUnderwaterSeeThrough`
  - `WaterFade`
  - base/offset refraction 选择

**Why This Works:** 这条路径正好覆盖了 current “bank 没被压下去” 而 CK3 “surface 继续强压暗”的关键差异。
**Pattern for Future:** 当 current `surface bank ≈ bottom bank`，但 CK3 `surface bank << bottom bank` 时，先查 surface attenuation，不要回头重开 bottom 主采样战争。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 增加 half/full-res 差分锁像素方法
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，系统状态未变
- [ ] Update `CURRENT_FEATURES.md` - 不需要，功能状态未变

### New Patterns/Anti-Patterns Discovered
**New Pattern:** half/full-res 差分交点锁定同一颗 river pixel
- When to use: river pass 里既有半分辨率 seed/bottom，又有全分辨率 surface
- Benefits: 能快速把 `248/276` 与 `223/305` 串成同一个代表像素链
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Code Quality Notes

### Testing
- **Manual Tests:** `renderdoc-cli info/pipeline/usage/export-rt/pick-pixel/debug pixel`
- **Coverage:** `223`, `248`, `276`, `305` 的 RT 链与代表像素

### Technical Debt
- **Created:** 无
- **Paid Down:** 排除了“继续回到 post-color 链”和“继续把河心主色归因给 source RT”这两条低价值方向
- **TODOs:** 仍需要可用的 MCP / GUI 持久会话做 bank-edge hot-edit 验证

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 以 bank-edge 像素为主，拆 `RiverSurface.sdsl` 的 `CalcRefraction/see-through/WaterFade` - 因为 current `305` 没有像 CK3 那样继续压暗
2. 继续保留 `223` source RT 不等价问题，但只在 bank/offset 像素上评估它的实际影响 - 因为河心像素已经由 `276` 主导
3. 恢复 `renderdoc-mcp` 或改用 RenderDoc GUI 持久会话做最小 hot-edit - 因为 CLI 只能做静态证据，不适合 replacement 回读

### Blocked Items
- **Blocker:** `renderdoc-mcp` transport 当前直接关闭
- **Needs:** 恢复 MCP 或改回 GUI 持久会话
- **Owner:** 工具链

### Questions to Resolve
1. current bank-edge 没被压暗，主要是 `ApplyTerrainUnderwaterSeeThrough` 还是 `WaterFade` 在 current scene 上过弱？
2. `223` 的 bright HDR source 在 bank/offset 像素上到底还有多少实际贡献？

---

## Session Statistics

**Files Changed:** 2
**Lines Added/Removed:** +1 log / +1 learning update
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- current capture 的稳定链路是 `223 -> 248 -> 276 -> 305`
- 同一颗河心像素上，真正把颜色压到暗水量级的是 `276`，`305` 基本沿用 `276`
- current bank-edge 像素 `305` 仍接近 `276`，没有出现 CK3 那种 `338 -> 466` 的大幅压暗

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无
- Constraints: `renderdoc-mcp` 当前不可用，只能走 CLI 静态证据链

**Gotchas for Next Session:**
- Watch out for: 不要再沿用旧 `251`
- Don't forget: half-res 像素要映射到 full-res 再比较
- Remember: current `source RT wrong` 仍成立，但它不再解释河心像素为什么黑

---

## Links & References

### Related Documentation
- [ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md)
- [CURRENT_FEATURES.md](../../../../CURRENT_FEATURES.md)

### Related Sessions
- [2026-06-19-river-updated-debug-rdc-postcolor-verification.md](./2026-06-19-river-updated-debug-rdc-postcolor-verification.md)
- [2026-06-19-river-refraction-source-and-camera-clamp-alignment.md](./2026-06-19-river-refraction-source-and-camera-clamp-alignment.md)

### Code References
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Effects/RiverSceneSeed.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`

---
