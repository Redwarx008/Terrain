# River Surface CBuffer Parity, FXAA Verification, And RenderDoc Transport Crash
**Date**: 2026-06-19
**Session**: 2
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 在不改 `.sdsl` 的前提下，继续确认 current river surface 与 CK3 的剩余差异到底落在常量参数、surface pass 本体，还是后续全屏链路。

**Success Criteria:**
- 核对 current surface 关键 cbuffer 参数是否已和 CK3 一致。
- 核对 event `426` 是否只是后处理/抗锯齿，而不是新的 river 着色阶段。
- 记录本轮 RenderDoc 工具链可继续依赖和不可继续依赖的边界。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md](./2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md)
- Related: [2026-06-18-river-surface-ck3-calcwater-parity-gap.md](../18/2026-06-18-river-surface-ck3-calcwater-parity-gap.md)

**Current State:**
- 已经确认 current `bottom` 不是主要问题。
- 已经确认 current `surface` 的资源集合与 CK3 不等价，但还需要排除“只是常量没对齐”或“只是后续全屏 pass 压暗”的可能。

---

## What We Did

### 1. 复核 current surface cbuffer 关键常量
**Files Changed:** 无运行时代码；新增临时 HLSL 工件目录 `artifacts/renderdoc/surface_term_split_20260619/`

**Implementation:**
- 读取 `debug.rdc` `event 397` pixel shader `Globals` cbuffer。
- 重点复核：
  - `_WaterDiffuseMultiplier_id42 = 1.0`
  - `_WaterColorMapTintFactor_id44 = 0.0106959343`
  - `_WaterSeeThroughDensity_id51 = 0.8`
  - `_WaterFresnelBias_id54 = 0.01`
  - `_WaterFresnelPow_id55 = 4.3`
  - `_WaterCubemapIntensity_id61 = 0.0`
  - `WaterColorShallow_id108 = [0.0055146, 0.0078107, 0.0120865]`
  - `WaterColorDeep_id109 = [0.0001385, 0.0001975, 0.0002263]`

**Finding:**
- 上述参数与 `artifacts/renderdoc/river_surface_calcwater_gate/cbuffer-target.json` 中 CK3 target surface 实际 cbuffer 一致。

**Rationale:**
- 这直接排除了“current 仍在用错误的亮蓝默认水色/错误 Fresnel/错误 see-through 标量”这种旧假设。

### 2. 确认 event 426 的职责
**Files Changed:** 无

**Implementation:**
- 读取 `debug.rdc` `event 426` 的 PS disassembly 与 cbuffer。
- 结果显示：
  - shader 为 `FxaaPixelShader_id4`
  - 只绑定 `Texture0_id14`
  - cbuffer 仅有 `Texture0TexelSize_id15` 等贴图尺寸参数

**Finding:**
- `426` 是 FXAA 全屏 pass，不是 tonemap / fog / color-grade，更不是 river 专属 pass。

**Rationale:**
- 因此 river 的实质问题仍在 `397` surface pass 本身；后续全屏链路不是主要根因。

### 3. 准备新的 RenderDoc 热修改工件
**Files Changed:**
- `artifacts/renderdoc/surface_term_split_20260619/surface_terms_rgb.hlsl`
- `artifacts/renderdoc/surface_term_split_20260619/surface_reflection_only.hlsl`
- `artifacts/renderdoc/surface_term_split_20260619/surface_prepost_color.hlsl`

**Implementation:**
- 从本地匹配 capture 的生成 HLSL
  - [shader_RiverSurface_d0363eff94c6f5cbda08ef91cd3c0349.hlsl](E:\Stride Projects\Terrain\Bin\Editor\Debug\win-x64\log\shader_RiverSurface_d0363eff94c6f5cbda08ef91cd3c0349.hlsl)
  复制出三份临时诊断 shader，准备继续拆：
  - `waterFade / fresnel / baseWaterLuma`
  - `reflectionColor`
  - `跳过 ApplySurfacePostProcessing`

**Status:**
- 文件已准备好，但本轮未能继续完成 MCP 编译替换验证。

### 4. 命中已知 RenderDoc MCP transport 崩溃
**Files Changed:** 无

**Implementation:**
- 在已有 diff session 的情况下尝试 `diff_close`。
- 结果再次触发 `Transport closed`，之后 `open_capture` 也无法恢复。

**Finding:**
- 本轮重现了之前日志里已经出现过的故障：`renderdoc-mcp` 在 diff/replace 某些路径后会直接断 transport。

**Rationale:**
- 这不是新的 river 渲染根因，但它决定了下轮调试方式：要么重新开会话恢复 MCP，要么切到 `renderdoc-cli`/本地脚本链路继续热改。

---

## Decisions Made

### Decision 1: 继续排除“surface 标量常量不对”
**Context:** 旧结论里 surface 资源集合不等价，但还不能证明标量参数已对齐。
**Decision:** 直接以 capture 的 `get_cbuffer_contents` 为准，不再根据源码默认值猜。
**Rationale:** GPU 实际值比 SDSL/C# 默认值更可信。

### Decision 2: 认定 426 不是主要根因阶段
**Context:** 397 与 426 的像素值有差别，必须确认 426 在做什么。
**Decision:** 用 shader disasm 明确 426 是 FXAA。
**Rationale:** 可以把问题继续收敛回 397 surface pass。

---

## What Worked ✅

1. **直接读取 current capture 的 surface cbuffer**
   - What: 把关键 water 常量逐项和 CK3 target cbuffer 对照
   - Why it worked: 一次性排除了一整类“调常量即可”的错误方向

2. **直接验证 426 的 shader 身份**
   - What: 读 event `426` 的 PS disasm
   - Why it worked: 明确它只是 FXAA，不是新的 river 渲染阶段

---

## What Didn't Work ❌

1. **在当前会话里继续操作 diff session**
   - What we tried: `diff_close` 后重开 capture
   - Why it failed: `renderdoc-mcp` transport 直接断开
   - Lesson learned: 一旦 diff session 进入异常状态，不要继续在同一连接上尝试恢复
   - Don't try this again because: 会让整个 RenderDoc MCP 不可用

---

## Problems Encountered & Solutions

### Problem 1: `renderdoc-mcp` transport 再次崩溃
**Symptom:** `diff_close` 之后所有 RenderDoc MCP 调用返回 `Transport closed`。
**Root Cause:** RenderDoc MCP 的已知稳定性问题；此前日志已有同类记录。
**Investigation:**
- Tried: `diff_close`
- Tried: 重新 `open_capture`
- Found: transport 已彻底不可恢复

**Solution:**
- 本轮停止继续 MCP 热改，保留已导出的工件和诊断 HLSL，等待新会话恢复 MCP 或切换到 CLI/脚本链路。

**Pattern for Future:**
- 在 RenderDoc MCP 状态健康时先把最关键的替换做完，不要把 diff close/reopen 留到最后。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 新开会话恢复 `renderdoc-mcp`，优先编译并替换 `surface_prepost_color.hlsl`
2. 若 transport 仍不稳定，改用 `renderdoc-cli` 或本地脚本继续做 shader hot-edit
3. 在恢复热改后，验证 `397` 跳过 `ApplySurfacePostProcessing` 是否仍与 CK3 有明显差距
4. 若仍有差距，再继续拆 `CalcWater` 中间量，而不是回头调水色/Fresnel 常量

### Questions to Resolve
1. 当前 surface 的剩余差距，是否主要来自 editor terrain `HeightmapSlice + terrain normal + tint/fog` 后段，而不是 `CalcWater` 本体？
2. 在 current/CK3 标量参数已对齐后，是否应该直接把 current surface 输入集合往 `HeightLookup/PackedHeight/FoW/ShadowMap` 语义靠齐？

---

## Session Statistics

**Files Changed:** 4（均为分析工件/日志）
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `event 397` 的关键 water cbuffer 标量已经和 CK3 target surface 对齐。
- `_WaterCubemapIntensity_id61 = 0` 在 current/CK3 两边都成立，所以“缺反射”不是当前主问题。
- `event 426` 只是 FXAA。
- `renderdoc-mcp` 本轮在 `diff_close` 后再次彻底断 transport。

**Gotchas for Next Session:**
- 不要再把当前问题先归因到 `WaterColorShallow/Deep` 或 Fresnel 常量。
- 如果要继续 hot-edit，优先恢复 MCP 连接再编译 `surface_prepost_color.hlsl`。
- 如果 transport 再崩，不要在同一连接里反复 `open_capture` 尝试恢复。
