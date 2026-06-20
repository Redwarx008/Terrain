# River CK3 Capture Pass Diff And Hot Edit
**Date**: 2026-06-19
**Session**: 1
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 对比当前 `debug.rdc` 与 `ck3-river.rdc` 的河流渲染，逐个 pass 判断 shader/资源/cbuffer 是否一致或等价。

**Secondary Objectives:**
- 在修改 `.sdsl` 前先用 RenderDoc 热修改验证候选方向。
- 找出当前河流与 CK3 差距最大的实际环节。

**Success Criteria:**
- 明确 current/CK3 对应的 river pass。
- 明确 bottom 与 surface 哪些部分等价，哪些部分不等价。
- 至少完成一次可见结果的 RenderDoc 热修改验证。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-surface-post-chain-editor-terrain.md](./2026-06-19-river-surface-post-chain-editor-terrain.md)
- See: [2026-06-19-river-surface-lighting-cbuffer-parity.md](./2026-06-19-river-surface-lighting-cbuffer-parity.md)
- Related: [ADR-014](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- 项目河流链路仍为 `bottom -> refraction -> surface`。
- 用户补充了目标截帧 `C:\Users\Redwa\Desktop\ck3-river.rdc`，允许直接和当前 `debug.rdc` 对照。

**Why Now:**
- 当前河流实际效果和 CK3 仍明显不一致，需要停止仅看源码，回到 capture 级别逐 pass 定位。

---

## What We Did

### 1. 对齐 current/CK3 的 river pass
**Files Changed:** 无

**Implementation:**
- 复核 RenderDoc/CLI 工件，确认：
- current `debug.rdc`
- seed/refraction 预处理：event `248`
- bottom：events `276/290/304`
- surface：events `343/370/397`
- final 可见合成验证点：event `426`
- target `ck3-river.rdc`
- bottom：events `332/334/336/338`
- surface：events `460/462/464/466`

**Rationale:**
- 先对齐 draw 和 pass 边界，否则“等价/不等价”会混淆 include 文件边界和实际编译命中路径。

### 2. 导出并核对 current/CK3 的实际绑定与 cbuffer
**Files Changed:** 无

**Implementation:**
- current surface `event 343`：
  - 只有一个 `Globals` cbuffer，`736 bytes / 96 vars`
  - 绑定 `RefractionTexture`、`AmbientNormalTexture`、`FlowNormalTexture`、`Foam*`、`WaterColorTexture`、`ShadowNoiseTexture`、`HeightmapSlice0..7`、`ReflectionSpecularTexture`
- current bottom `event 276`：
  - `Globals` cbuffer，`592 bytes / 32 vars`
  - 绑定 `BottomDiffuse/Normal/Properties`、`EnvironmentMapTexture`、`SceneShadowMapTexture`
- CK3 bottom `event 338`：
  - cbuffer 拆成 `pdx_hlsl_cb52/cb53/cb17/cb11/cb10`
  - 绑定 `BottomDiffuse/Normal/Properties`、`EnvironmentMap`、`ShadowTexture`
- CK3 surface `event 466`：
  - 已复核现成工件 `artifacts/renderdoc/river_surface_calcwater_gate/cbuffer-target.json`
  - 明确绑定 `HeightLookupTexture`、`PackedHeightTexture`、`FogOfWarAlpha_Texture`、`ShadowMap_Texture`

**Rationale:**
- 这一步确认了：current bottom 在“资源种类”上接近 CK3；current surface 在“后段输入”上不等价。

### 3. 源码级对照 current SDSL 与 CK3 shader
**Files Changed:** 无

**Implementation:**
- [RiverBottom.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverBottom.sdsl:245) 仍保留 CK3 式 bottom shadow disc kernel、water-surface shadow exclusion、scene sun + cubemap lighting。
- [RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:566) 的 `CalcWater` 前半段已基本是 CK3 `CalcRiverAdvanced -> CalcWater` 路径。
- 但 [RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:487)、[RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:522)、[RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:535) 之后仍是项目侧替代后段：
  - `ApplyTerrainShadowTintWithClouds`
  - `ApplyMapDistanceFogWithoutFoW`
  - editor terrain `HeightmapSlice0..7`
- [RiverRenderFeature.cs](E:\Stride Projects\Terrain\Terrain.Editor\Rendering\River\RiverRenderFeature.cs:608) / [RiverRenderFeature.cs](E:\Stride Projects\Terrain\Terrain.Editor\Rendering\River\RiverRenderFeature.cs:627) 也确认了 current surface 输入来自 editor terrain slice，而不是 CK3 `HeightLookup/PackedHeight/FOW/ShadowMap`。

**Rationale:**
- 这一步把“源码近似移植”和“capture 里真实编译运行的完整 PS”区分开了。

### 4. RenderDoc 热修改验证
**Files Changed:** 无

**Implementation:**
- 先打通热修改链路：
  - 在 surface PS 上用纯绿 replacement 确认最终可见验证点必须选 event `426`，而不是中间的 `343` HDR RT。
- 再做三个定向实验：
  - baseline：`rt_426_0_baseline.png`
  - refraction-only：只输出 `RefractionTexture.rgb + 原 alpha`
  - shadow tint mask：直接输出当前 editor terrain tint mask
  - cloud mask：直接输出当前 cloud mask
- 拼图输出：`artifacts/renderdoc/surface_hotedit_contact_sheet.png`

**Observed Results:**
- baseline 河面明显偏暗。
- refraction-only 后，河面恢复为可见的棕色底色，说明 bottom/refraction 输入本身不是黑源。
- shadow tint mask 基本为全黑，说明当前 `ApplyTerrainShadowTintWithClouds` 的 tint 强度不是主黑源。
- cloud mask 为中灰，说明云影会压暗，但不足以单独解释整条河的差距。

**Rationale:**
- 这一步把根因从“bottom pass / terrain tint”收缩到“surface 主体 shading 与 CK3 后段输入差异”。

---

## Decisions Made

### Decision 1: 可见热修改验证以最终合成 event 为准
**Context:** 直接看 `event 343` 的导出 RT 容易被中间 HDR 目标误导。
**Options Considered:**
1. 继续在 `343` 上读导出图
2. 切到最终可见事件验证 hot replace

**Decision:** Chose Option 2
**Rationale:** `426` 上能直接看到 replacement 是否改变河面可见结果。
**Trade-offs:** 需要先通过 `pixel_history` 找到最终可见 draw 链。

### Decision 2: 先判定“哪一段不是黑源”，再回推 surface 根因
**Context:** 直接改 `.sdsl` 会把底层输入问题和 surface 主体问题混在一起。
**Options Considered:**
1. 直接改 surface SDSL
2. 用 refraction-only / mask-only replacement 先拆链路

**Decision:** Chose Option 2
**Rationale:** 可以最小代价确认 bottom/refraction、terrain tint、cloud mask 各自责任。
**Trade-offs:** 不能一次性得到最终修复代码，但能避免盲改。

---

## What Worked ✅

1. **以最终可见事件做 hot replace 验证**
   - What: 在 event `426` 导出最终图，而不是盯着 event `343`
   - Why it worked: 能直接看到纯绿 replacement、refraction-only replacement 是否生效
   - Reusable pattern: Yes

2. **用 mask-only replacement 排除伪嫌疑人**
   - What: 单独输出 `shadowTintMask`、`cloudMask`
   - Impact: 快速确认 terrain tint 不是主黑源，cloud 只是次要压暗项

---

## What Didn't Work ❌

1. **直接从 event 343 导出 PNG 判断热修改结果**
   - What we tried: 先在中间 surface draw 目标上看 replacement 是否显色
   - Why it failed: 中间 HDR RT 不适合作为最终可见验证面，且导出结果很容易误读
   - Lesson learned: river shader hot replace 优先在最终可见 composite 事件验证
   - Don't try this again because: 会浪费时间在“替换没生效”假象上

2. **先把 HLSL 输入按拆散语义写死**
   - What we tried: 第一版 replacement 用拆散的 `TEXCOORD0/1/3/4/5`
   - Why it failed: 对当前 draw 更稳的是按 VS 实际寄存器形状吃 `POSITION_WS + SV_Position + packed TEXCOORD`
   - Lesson learned: 先看 VS trace 输出寄存器，再决定 replacement 输入结构

---

## Problems Encountered & Solutions

### Problem 1: 热修改看起来“不生效”
**Symptom:** `shader_replace` 返回成功，但在中间 RT 上看不出变化。
**Root Cause:** 验证点选错了；river surface 的中间 HDR RT 不适合直接做可见结果判断。
**Investigation:**
- Tried: 直接导出 `event 343` RT
- Tried: 对最终事件做纯绿 replacement
- Found: `event 426` 才是当前 capture 里最可靠的热修改可见验证点

**Solution:**
- 把 hot replace 统一切到最终可见事件链路上验证，再回到 source/disasm 做归因。

**Pattern for Future:** river/hdr/post-chain 问题优先在最终 composite 事件验证热修改。

### Problem 2: current surface 到底是不是 terrain tint 压黑
**Symptom:** 当前 surface 代码里保留了 terrain tint / cloud / fog，肉眼上又明显偏暗。
**Root Cause:** 只看源码无法区分“存在逻辑”和“实际对当前像素贡献多大”。
**Investigation:**
- Tried: 输出 refraction-only
- Tried: 输出 shadow tint mask
- Tried: 输出 cloud mask
- Found:
  - refraction-only 有明显底色
  - shadow tint mask 近乎 0
  - cloud mask 中等，不足以单独解释主差异

**Solution:**
- 把主要问题收敛到 `CalcWater` 主体与 CK3 surface 后段输入不等价，而不是继续先改 terrain tint。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [x] 记录 hot-edit 验证面选择的 reusable learning
- [ ] 如后续真的改动 surface SDSL，再更新 `CURRENT_FEATURES.md`

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 可见热修改优先绑定最终 composite 事件
- When to use: HDR 中间 RT 不直观、需要验证 replacement 是否真的改变最终画面时
- Benefits: 避免误判“热修改无效”
- Add to: `docs/log/learnings/`

---

## Code Quality Notes

### Testing
- **Manual Tests:** 本轮全部是 RenderDoc capture / cbuffer / shader replacement 验证
- **Automated Tests:** 无

### Technical Debt
- **Created:** 无代码债
- **TODOs:** 下一轮需要把 hot-edit 结论落实到 `RiverSurface.sdsl`

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 先在 `RiverSurface.sdsl` 里排查/改写 `CalcWater` 主体，而不是先动 bottom 或 terrain tint
2. 重点核 current `CalcWater` 与 CK3 `jomini_water_default.fxh` 在 refraction/fresnel/water normal 上的数值差异
3. 单独核资源语义差异：`WaterColorTexture` SRGB、`FoamTexture/FoamRampTexture` 槽位/格式不等价

### Questions to Resolve
1. 当前偏暗主要来自 `CalcWater` 哪个量：`waterFade`、`fresnel`、还是 normal/refraction 组合？
2. 是否需要把 current surface 的后段替代输入进一步靠近 CK3 `HeightLookup/PackedHeight/ShadowMap`，还是先把 `CalcWater` 主体修平？

### Docs to Read Before Next Session
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md) - RenderDoc/hot-edit 经验
- [2026-06-19-river-surface-post-chain-editor-terrain.md](./2026-06-19-river-surface-post-chain-editor-terrain.md) - 当前后段替代输入背景

---

## Session Statistics

**Files Changed:** 1
**Lines Added/Removed:** +0/-0（运行时代码无改动，仅分析）
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- current bottom pass 不是这次主黑源
- current surface 与 CK3 surface 在后段输入上明确不等价
- hot replace 的最终可见验证点是 `debug.rdc` event `426`
- `artifacts/renderdoc/surface_hotedit_contact_sheet.png` 汇总了 baseline / refraction-only / tint-mask / cloud-mask

**What Changed Since Last Doc Read:**
- Implementation: 无代码变更
- Constraints: 已确认本轮可依赖 RenderDoc HLSL replacement 做最终可见验证

**Gotchas for Next Session:**
- 不要再把 `event 343` 的 HDR 导出图当成 hot-edit 最终判据
- 不要先改 `ApplyTerrainShadowTintWithClouds`；它在当前样本里不是主黑源
- 先盯 `CalcWater` 主体和 CK3 surface 输入差异

---

## Links & References

### Related Sessions
- [2026-06-19-river-surface-post-chain-editor-terrain.md](./2026-06-19-river-surface-post-chain-editor-terrain.md)
- [2026-06-19-river-surface-lighting-cbuffer-parity.md](./2026-06-19-river-surface-lighting-cbuffer-parity.md)

### Code References
- current surface post chain: `Terrain.Editor/Effects/RiverSurface.sdsl:487-553`
- current surface main flow: `Terrain.Editor/Effects/RiverSurface.sdsl:566-678`
- current surface input binding: `Terrain.Editor/Rendering/River/RiverRenderFeature.cs:578-658`
- current bottom lighting/shadow: `Terrain.Editor/Effects/RiverBottom.sdsl:245-409`

### Artifacts
- `E:\Stride Projects\Terrain\artifacts\renderdoc\surface_hotedit_contact_sheet.png`
- `E:\Stride Projects\Terrain\artifacts\renderdoc\river_surface_calcwater_gate\cbuffer-target.json`
- `E:\Stride Projects\Terrain\artifacts\renderdoc\river_surface_calcwater_gate\target-surface-bindings.json`

---

## Notes & Observations

- CK3 surface capture 的不等价重点不在 `CalcRiverAdvanced` 前半段，而在完整 PS 后段的输入集合。
- 本轮热修改最重要的结果不是“某个 replacement 更像 CK3”，而是成功把问题责任从 bottom/tint 缩到 surface 主体与替代输入差异。

---
