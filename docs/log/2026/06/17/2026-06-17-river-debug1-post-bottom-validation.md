# River Debug1 Post-Bottom Validation
**Date**: 2026-06-17
**Session**: River debug1 post-bottom validation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 分析 `C:\Users\Redwa\Desktop\debug1.rdc`，验证 `RiverBottom` 改成 CK3 capture 对齐 `worldUv` 主路径后，当前河流问题是否仍主要来自 bottom。

**Secondary Objectives:**
- 判断 bright pre-bottom payload 是否还在实际污染 river center / bank。
- 找出当前 remaining mismatch 主要落在 bottom 还是 surface。

**Success Criteria:**
- 至少确认 `157 -> 184 -> 213` 这条 river 链路在 `debug1.rdc` 上的实际像素变化。
- 明确这轮改动后，当前主要问题是否已经从 bottom 转移到 surface。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-bottom-worlduv-capture-alignment-implementation.md](./2026-06-17-river-bottom-worlduv-capture-alignment-implementation.md)
- See: [2026-06-17-river-hotedit-root-cause-confirmation.md](./2026-06-17-river-hotedit-root-cause-confirmation.md)

**Current State:**
- `RiverBottom.sdsl` 已经切到 `worldUv` 主采样、`tangentUv` depth/profile、capture 对齐 alpha。

**Why Now:**
- 用户给了新抓帧 `debug1.rdc`，需要确认运行时实际结果是否跟之前的 RenderDoc hot-edit 预期一致。

---

## What We Did

### 1. 确认 `debug1.rdc` 仍是同一条 river 链路，但 shader/mesh 已发生结构变化
**Files Changed:** 无

**Implementation:**
- 打开 `debug1.rdc` 后确认：
  - D3D11
  - `totalEvents = 74`
  - `totalDraws = 61`
  - top draws 仍然包含 `119 / 157 / 184 / 213`
- 与旧 `debug.rdc` 做 diff：
  - draw/event/resource 总数相同
  - `184`/`213` 的 shader hash 已变化
  - `184`/`213` 的 triangles 从 `120` 变为 `442`

**Rationale:**
- 这说明新 capture 命中了新的 river shader/mesh 状态，不是旧帧重复抓取。

### 2. 重新定位了这份 capture 上的河心/河岸像素
**Files Changed:** 无

**Implementation:**
- 旧 capture 的代表坐标 `(344,598)` / `(352,705)` 在 `debug1.rdc` 上已经不再落在 river 上。
- 导出：
  - `event 184` RT0: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_184_0.png`
  - `event 213` RT0: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_213_0.png`
- 新代表坐标改为：
  - river center final: `(840,480)`，half-res 对应 `(420,240)`
  - river bank final: `(620,300)`，half-res 对应 `(310,150)`

**Rationale:**
- 不先重定位，继续沿用旧坐标会误判成 river draw 没生效。

### 3. 确认 bright pre-bottom payload 仍存在，但 `184` 已经会把它覆盖掉
**Files Changed:** 无

**Implementation:**
- center:
  - `event 157` `(420,240)` = `[3.0137, 3.4375, 1.0098, 0.0]`
  - `event 184` `(420,240)` = `[0.1410, 0.1520, 0.1447, 5.1328]`
  - `event 184` pixel history 明确显示 `184` 自己写入了这个像素
- bank:
  - `event 157` `(310,150)` = `[0.2686, 0.3840, 0.0876, 0.0]`
  - `event 184` `(310,150)` = `[0.1182, 0.1332, 0.1238, 5.3906]`
  - `event 184` pixel history 同样显示 `184` 自己覆盖了该像素

**Rationale:**
- 这和旧 `debug.rdc` 已经不是同一个问题态。
- bright seed 还在，但它不再直接穿透到 current river bottom result。

### 4. 当前 remaining mismatch 已经主要落在 `RiverSurface`
**Files Changed:** 无

**Implementation:**
- center final:
  - `event 213` `(840,480)` = `[0.1030, 0.1864, 0.2329, 1.0]`
  - 其 pixel history 显示最终有效写入来自 `event 213`
- bank final:
  - `event 213` `(620,300)` = `[0.1248, 0.2053, 0.2498, 1.0]`
  - 同样由 `event 213` 最终写入
- 对比 `184 -> 213`：
  - center 从 `[0.1410, 0.1520, 0.1447]` 被 surface 改到 `[0.1030, 0.1864, 0.2329]`
  - bank 从 `[0.1182, 0.1332, 0.1238]` 被 surface 改到 `[0.1248, 0.2053, 0.2498]`
- 结合 `RiverSurface.sdsl` 当前路径：
  - `waterDiffuse = lerp(WaterColorDeep, WaterColorShallow, facing) * _WaterDiffuseMultiplier`
  - `waterColor += lerp(refractionColor, reflectionColor, saturate(fresnel))`
- 当前默认 surface 常量颜色：
  - `WaterColorShallow = (0.0, 0.3, 0.5, 0.7)`
  - `WaterColorDeep = (0.0, 0.05, 0.15, 0.85)`

**Rationale:**
- 这说明 bottom 现在已经是暗底，但 surface 又把结果整体往蓝青色抬了一遍。
- 因此当前 remaining mismatch 的主战场已经不是 `RiverBottom`，而是 `RiverSurface` 的 diffuse/tint/fresnel 合成。

---

## Decisions Made

### Decision 1: 当前不再把 pre-bottom payload 视为第一优先级
**Context:** `debug1.rdc` 上 bright seed 仍然存在，但 `184` 已能把 center / bank 覆盖为暗底。

**Options Considered:**
1. 继续把剩余问题主要归因给 pre-bottom
2. 将主要注意力转向 surface

**Decision:** 选择 2
**Rationale:** 当前最终颜色的主要抬升来自 `213` 自身，而不是 seed 泄漏。
**Trade-offs:** pre-bottom 仍不是“正确实现”，但它已经不是当前视觉偏差的最大贡献项。

---

## What Worked ✅

1. **导出 RT 重定位代表像素**
   - What: 先导出 `184/213` 的 RT，再重新取 center/bank
   - Why it worked: 避免沿用旧 capture 坐标导致误判
   - Reusable pattern: Yes

2. **`157 -> 184 -> 213` 链式 pixel history**
   - What: 对新 center/bank 坐标逐层看修改历史
   - Why it worked: 直接锁定当前主要偏差已经从 bottom 转到 surface
   - Reusable pattern: Yes

---

## Problems Encountered & Solutions

### Problem 1: 旧代表坐标在 `debug1.rdc` 上失效
**Symptom:** 旧坐标读到的 final color 来自 `event 119`，而不是 river draw。
**Root Cause:** 新 capture 的镜头/mesh 分布与旧 capture 不再完全相同。
**Solution:** 先导出 `184/213` RT，再在图像上重新定位 river center / bank。

### Problem 2: RenderDoc MCP 在 diff 会话释放时卡住
**Symptom:** `diff_close` / 重新 `open_capture` 后续操作超时。
**Root Cause:** 当前 MCP diff 会话释放异常。
**Solution:** 本轮停止继续做 surface shader hot-edit，只保留已获取证据并记录结论。

---

## Architecture Impact

### Documentation Updates Required
- [ ] 暂无；本轮没有新的仓库代码修改

### Architectural Decisions That Changed
- **Changed:** 当前 remaining mismatch 的定位重点
- **From:** 主要怀疑 bottom / pre-bottom
- **To:** 主要怀疑 surface diffuse/tint/fresnel 合成
- **Reason:** `debug1.rdc` 已证明 bottom 现在会有效覆盖 bright seed

---

## Code Quality Notes

### Testing
- **Manual Tests:** `debug1.rdc` RenderDoc 像素与 pixel history 复核
- **Runtime Verification:** 还没做新的 surface hot-edit；MCP diff/session 卡住后中止

### Technical Debt
- **Created:** 无
- **Paid Down:** “current 仍主要坏在 bottom”这条判断
- **TODOs:** 下一轮先恢复 RenderDoc MCP 正常状态，再对 `event 213` 做极小 surface hot-edit 验证

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 恢复 RenderDoc MCP 正常会话后，对 `event 213` 做极小 hot-edit：
   - 只透 `refractionColor`
   - 或压低 `waterDiffuse`
2. 如果 hot-edit 有效，再回到 `RiverSurface.sdsl` 改 surface diffuse/tint/fresnel 组合
3. 只有在 surface 收敛后，再决定 pre-bottom payload 是否还值得继续对 CK3 parity

### Questions to Resolve
1. `RiverSurface` 当前偏蓝主要来自 `WaterColorDeep/Shallow` 常量，还是来自 `refractionWaterColorMap` / fresnel 叠加？
2. 当前明显的纵向条纹主要来自 flow normal，还是来自 water-color/refraction 采样路径？

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `debug1.rdc` 已证明 new `RiverBottom` 实装生效
- center `(420,240)` / bank `(310,150)` 在 `184` 后都会被重写成暗底
- 当前主要偏差已转移到 `event 213` 的 surface 合成

**Gotchas for Next Session:**
- 不要再沿用旧 capture 的 `(344,598)` / `(352,705)` 代表坐标
- RenderDoc MCP 的 diff 会话本轮在释放时卡住，先处理工具状态再继续 hot-edit

---
