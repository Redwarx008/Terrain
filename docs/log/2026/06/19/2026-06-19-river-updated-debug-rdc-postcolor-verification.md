# River Updated Debug RDC Postcolor Verification
**Date**: 2026-06-19
**Session**: 5
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用用户更新后的 `C:\Users\Redwa\Desktop\debug.rdc` 验证“去掉 surface post color 后，`HeightLookup/PackedHeight/FoW` 同类依赖是否已经不再影响最终水面颜色”。

**Success Criteria:**
- 确认新的 shader 已进入 GPU
- 确认 surface shader 已不再声明/绑定后段颜色输入
- 确认如果河流仍偏黑，问题已经不在这条 post color 链

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-surface-post-color-removal.md](./2026-06-19-river-surface-post-color-removal.md)
- See: [2026-06-19-river-ck3-current-pass-semantic-audit.md](./2026-06-19-river-ck3-current-pass-semantic-audit.md)

**Current State:**
- 上一轮已把 `ApplySurfacePostProcessing` 改成只保留 `alpha/zoom/discard`。
- 用户补充了新的 `debug.rdc` 作为 GPU 级验证证据。

---

## What We Did

### 1. 复核新 capture 基本信息
**Files Changed:** 无

**Implementation:**
- `debug.rdc` `LastWriteTime = 2026-06-19 15:43:27`
- `renderdoc-cli info`
  - `Total events: 78`
  - `Total draws: 65`
- 相比前一轮旧 `debug.rdc` 的 `82/69`，说明这不是旧 capture。

### 2. 重新锁定 current river pass
**Files Changed:** 无

**Implementation:**
- `renderdoc-cli pass-stats` 复核：
  - scene seed：`event 248`
  - bottom/refraction：`event 251`
  - surface：`event 305`
  - final LDR/tone-mapped output：`event 961`
- CK3 参考仍使用：
  - bottom：`338`
  - surface：`466`
  - final composite：`1146`

### 3. 证明新的 surface shader 已经不再依赖 post color 输入
**Files Changed:** 无

**Implementation:**
- `renderdoc-cli shader ps -e 305` 显示 current surface PS 资源声明只剩：
  - `RefractionTexture`
  - `AmbientNormalTexture`
  - `FlowNormalTexture`
  - `FoamTexture`
  - `FoamRampTexture`
  - `FoamMapTexture`
  - `FoamNoiseTexture`
  - `WaterColorTexture`
  - `ReflectionSpecularTexture`
- 已经没有：
  - `HeightmapSlice0..7`
  - `ShadowNoiseTexture`
  - `FogOfWarAlpha`
- 同一份 disasm 还明确显示：
  - `ApplySurfacePostProcessing_id64()` 只剩 `color.a *= 1.0f - _FlatMapLerp`
  - `color.a *= zoomBlendOut`
  - `discard`
  - 最后直接 `return color`

**Rationale:**
- 这说明“去掉 HeightLookup/PackedHeight/FoW 同类颜色依赖”已经真正进了 GPU，不是源码改了但 capture 还在跑旧 shader。

### 4. 证明河流仍黑，但黑源已经转移到 surface 本体
**Files Changed:** 无

**Implementation:**
- 导出并查看：
  - current bottom `event 251` → `rt_251_0.png`
  - current surface `event 305` → `rt_305_0.png`
  - current final `event 961` → `rt_961_0.png`
  - CK3 surface `event 466` → `rt_466_0.png`
  - CK3 final `event 1146` → `rt_1146_0.png`
- 当前图像结论：
  - `251` 底层输入是亮的米色/浅色地表，不是近黑
  - `305` surface 已经直接是深色河带
  - `961` final 仍是深色窄河带
- 像素采样：
  - current `251` `(600,165)`：`[1.08887, 0.939941, 0.743164, 9.24219]`
  - current `305` `(1200,330)`：`[0.101379, 0.0728149, 0.0428467, 1]`
  - current `961` `(1200,330)`：`[0.0705882, 0.0392157, 0.00784314, 0.00392157]`

**Rationale:**
- 这组数据直接证明：在同一条河上，bottom/refraction 输入亮度并不低，但 surface pass 本身把结果压成了深色。
- 所以这次移除 post color 后，黑色来源已经明确不在 `HeightLookup/PackedHeight/FoW` 同类后段链。

---

## Decisions Made

### Decision 1: 不再把 surface post color chain 当作主嫌疑人
**Context:** 新 capture 已证明该链路不再参与最终 `color.rgb`。
**Decision:** 后续分析重心转移到 `CalcRiverAdvanced -> CalcWater` 主体和 remaining 资源语义。
**Rationale:** 继续围绕 terrain/FoW 后段排查只会重复劳动。

### Decision 2: 下一轮优先查 surface 主体和水体资源语义
**Context:** 当前 surface PS 剩余资源已缩到水体相关集合。
**Decision:** 后续优先核：
1. `CalcWater` 主体能量路径
2. `FoamTexture/FoamRampTexture` 资源语义
3. bottom/refraction 到 surface 的使用方式

---

## What Worked ✅

1. **用新的 capture 直接看 surface disasm**
   - 这比继续猜源码或绑定更直接。
   - 一次性证明 post color 输入已经真的从 GPU shader 里消失。

2. **用 bottom/surface/final 同点采样串链**
   - 直接把“bottom 亮、surface 黑”锁死。
   - 不再需要继续怀疑 post color 或 FoW 路径。

---

## Next Session

### Immediate Next Steps
1. 优先继续拆 current `event 305` 的 `CalcWater` 主体，而不是再看 terrain/FoW 后段
2. 复核 `FoamTexture/FoamRampTexture` 是否仍然是当前最可疑的资源语义差异
3. 若需要热修改，优先在 surface 本体上做最小 replacement，而不是改 post chain

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 新 `debug.rdc` 不是旧帧，事件数/绘制数已变化
- current surface `event 305` 的 GPU shader 已不再声明 `HeightmapSlice* / ShadowNoise / FoW`
- current 河流仍黑，但黑源已经明确在 surface 本体，不在 post color chain

**Gotchas for Next Session:**
- 不要再回头怀疑 `ApplySurfacePostProcessing` 颜色链
- 不要再把 `HeightLookup/PackedHeight/FoW` 当成当前主问题

---
