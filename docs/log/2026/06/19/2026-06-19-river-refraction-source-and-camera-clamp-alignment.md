# 河流 refraction source 与 camera clamp 对齐
**Date**: 2026-06-19
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续分析更新后的 `C:\Users\Redwa\Desktop\debug.rdc`，定位 current 河流与 CK3 的剩余主要差异

**Secondary Objectives:**
- 只落地已经能证明与 CK3 不等价的 shader 语义差异
- 在修改后跑完整 shader 测试与 Stride asset 编译

**Success Criteria:**
- 重新锁定 current capture 的真实 river 事件号
- 找出不是 surface post-color 链的剩余主因
- 修复明确的 shader 语义偏差并验证可编译

---

## Context & Background

**Previous Work:**
- 见：`docs/log/2026/06/19/2026-06-19-river-updated-debug-rdc-postcolor-verification.md`
- 见：`docs/log/2026/06/19/2026-06-19-river-ck3-current-pass-semantic-audit.md`

**Current State:**
- `RiverSurface` 后段已只保留 `alpha/zoom/discard` 可见性控制
- 用户更新了 `debug.rdc`，需要重新确认 current event mapping

**Why Now:**
- 去掉 `HeightLookup/PackedHeight/FoW` 同类颜色依赖后，river 仍与 CK3 差异很大，必须重新定位 root cause

---

## What We Did

### 1. 重新锁定 current capture 事件链
**Files Changed:** 无

**Implementation:**
- 用 `renderdoc-cli` 重新查看 `events/draws/pipeline/export-rt/pick-pixel`
- 确认 current 新链路是：
- `223`：transparent-stage scene RT
- `248`：`RiverSceneSeed`
- `276`：`RiverBottom`
- `305`：`RiverSurface`

**Rationale:**
- 旧会话里的 `251` 在新 capture 中已经不是 bottom draw，继续沿用旧事件号会误判

### 2. 证明 current refraction source 仍不等价于 CK3 pre-bottom payload
**Files Changed:** 无

**Implementation:**
- 导出并查看 `223/248/276/305`
- 代表像素验证：
- current `223` 约为 `[2.1289, 1.3848, 0.8491, 1]`
- current `248` 只是把这个 HDR 源写入 seed，并重建 distance alpha
- current `276` 才开始写入 river bottom

**Rationale:**
- 这说明 current 的 pre-bottom/refraction source 不是 CK3 那种独立暗 payload，而是 transparent stage 当前 HDR scene RT

### 3. 修正已确认不等价的 shader 语义
**Files Changed:** `Terrain.Editor/Effects/RiverCommon.sdsl`, `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- `RiverCommon`：
- 补齐 CK3 `MaxHeight=50` camera clamp，用于 `RiverCompressWorldSpace` / `RiverDecompressWorldSpace`
- `RiverBottom`：
- 使用 `scaledRiverUv.x * _TextureUvScale`
- 保留 steep parallax，但主采样改为 `tangentUv`
- alpha 改为 `bottomDiffuse.a * fadeOut * connectionFade * edgeFade1 * edgeFade2`
- 文本测试同步更新为 advanced UV/alpha 语义与 camera clamp 语义

**Rationale:**
- 这两块都能直接从 CK3 shader 源码和 current/CK3 disasm 差异证明，不是拍脑袋补偿

---

## What Worked ✅

1. **重新从 capture 事件图出发**
   - What: 先重锁 event mapping，再继续逐 pass 对比
   - Why it worked: 避免把旧 capture 的事件号错误套到新 capture 上
   - Reusable pattern: Yes

2. **只修正“源码 + disasm + capture”三方都能证明的差异**
   - What: 先落地 `MaxHeight=50` clamp 和 bottom advanced UV/alpha
   - Impact: 改动范围可控，且文本测试、shader compile、Stride asset compile 全部通过

---

## What Didn't Work ❌

1. **尝试仅用一次次 CLI 命令走 RenderDoc 热替换**
   - What we tried: `shader-build` + `shader-replace`
   - Why it failed: `shader-build` 产出的 shader ID 不跨 CLI 进程保存，单次 one-shot CLI 调用无法直接串成持久会话
   - Lesson learned: CLI 可以做静态导出和单次分析，但需要持久 replay session 的热替换仍应优先 MCP / GUI / 同进程脚本
   - Don't try this again because: 会误以为命令链成功，其实 replacement 没真正生效

---

## Problems Encountered & Solutions

### Problem 1: 新 capture 的 bottom 事件号已经变化
**Symptom:** 继续查看 `251` 时，拿到的还是 scene-seed shader
**Root Cause:** 用户更新了 `debug.rdc`，river 事件号发生漂移
**Investigation:**
- Tried: 直接看旧事件号
- Tried: `draws/events/pipeline/export-rt`
- Found: current 真正 river 链路已变成 `223 -> 248 -> 276 -> 305`

**Solution:**
- 放弃旧事件号，重建本轮 capture 的 pass map

**Why This Works:** 它让后续每一个像素和 RT 证据重新回到正确 draw

### Problem 2: 剩余主因不在 surface post-color
**Symptom:** 移除 surface 后段颜色链后，river 仍明显不对
**Root Cause:** current 的 refraction source/timing 与 CK3 不等价，同时 common/bottom 还有明确 shader 语义差异
**Investigation:**
- Found: `223` 已是 bright HDR scene RT，不是暗色 pre-bottom payload
- Found: `RiverCommon` 缺少 CK3 `MaxHeight=50` clamp
- Found: `RiverBottom` 仍保留 world/tangent 混搭的旧采样与 alpha 语义

**Solution:**
- 先修正 `RiverCommon` 和 `RiverBottom` 的确定性偏差
- 把 “current transparent-stage RT0 不是 CK3 pre-bottom payload” 记录为当前剩余主根因

**Why This Works:** 先收敛 shader 语义，再单独处理 refraction source，避免把所有问题混成一个大黑箱

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `ARCHITECTURE_OVERVIEW.md` - 记录 bottom advanced UV/alpha、camera clamp 与 current refraction source 仍不等价
- [x] Update `CURRENT_FEATURES.md` - 同步河流多 pass 当前状态

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 把 transparent-stage `RT0` 直接当作 CK3 pre-bottom payload
- What not to do: 看到有 scene RT 就直接拿来当 river refraction seed
- Why it's bad: current `223` 已证明这个 RT 可能只是高亮 HDR scene，不具备 CK3 pre-bottom payload 语义
- Add warning to: `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Code Quality Notes

### Testing
- Tests Written: 更新了 `RiverShaderTextTests` 中 bottom UV/alpha 与 refraction pack/unpack 的断言
- Coverage: `RiverCommon`, `RiverBottom`, renderdoc 复核结论对应的文本语义
- Manual Tests: 尚未重新出一帧新的 `debug.rdc` 验证视觉结果

### Technical Debt
- Created: 无新的临时补偿
- Paid Down: 删除了 current 与 CK3 在 common/bottom 上的两类明确语义偏差
- TODOs:
- 后续需要继续处理 current refraction source/timing 与 CK3 pre-bottom payload 的不等价问题

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重新出一帧新的 current capture，验证 `MaxHeight=50` clamp 和 bottom advanced UV/alpha 是否已进入 GPU
2. 继续处理 `RiverRenderFeature` 在 transparent stage 上读取 `commandList.RenderTargets[0]` 的 source/timing 问题
3. 如果 source 改完仍有差异，再回到 surface `CalcWater` 做逐像素 hot-edit 验证

### Blocked Items
- **Blocker:** 当前 `renderdoc-cli` 的 `shader-build -> shader-replace` 不是持久会话
- **Needs:** RenderDoc MCP 恢复，或 GUI / 脚本化持久 replay session
- **Owner:** 工具链

### Questions to Resolve
1. Stride transparent stage 上更等价 CK3 `JominiRefraction` 的 source RT 应该是哪一个？
2. river 应该继续在当前 transparent stage 画，还是只改 refraction seed source？

---

## Session Statistics

**Files Changed:** 6
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/Effects/RiverCommon.sdsl`, `Terrain.Editor/Effects/RiverBottom.sdsl`
- Critical decision: 先只修正有源码/disasm/capture 三方证据支持的差异
- Current status: shader 语义继续收敛，但 refraction source/timing 仍是剩余主根因

**What Changed Since Last Doc Read:**
- Architecture: bottom 改为更接近 CK3 advanced 的 UV/alpha；common 补齐 camera clamp
- Implementation: 文本测试同步更新
- Constraints: CLI 热替换链仍不可靠

**Gotchas for Next Session:**
- Watch out for: 新 capture 的 river 事件号已经不是旧的 `251`
- Don't forget: `223` 才是这轮 current transparent-stage source RT
- Remember: 不能把 bright HDR scene RT 当成 CK3 pre-bottom payload
