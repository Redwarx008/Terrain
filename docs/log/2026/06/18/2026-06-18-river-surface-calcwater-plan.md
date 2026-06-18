# River Surface CalcWater Implementation Plan

**Date**: 2026-06-18  
**Session**: River surface CalcWater implementation planning  
**Status**: 🔄 In Progress  
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 将已批准的 surface strict parity 设计转换成可执行 implementation plan。

**Success Criteria:**
- 计划在修改 SDSL/C# 前先完成 RenderDoc hot-replace gate。
- 计划不创建带外部产品名、产品缩写或来源标签含义的项目符号。
- 计划明确哪些文件可以改、哪些文件必须等 gate 通过后再改。

---

## What We Did

### 1. 使用 writing-plans skill

**Files Changed:**
- `docs/superpowers/plans/2026-06-18-river-surface-calcwater-hotreplace-gate.md`
- `docs/log/2026/06/18/2026-06-18-river-surface-calcwater-plan.md`

**Result:**
- 将实现拆成第一阶段 gate plan。
- 第一阶段只做 RenderDoc、资源清单、cbuffer/SRV 导出、current-like 控制 shader、target-style shader replacement 和结果日志。
- SDSL/C# 落地被明确推迟到 hot-replace gate 通过之后。

---

## Decisions Made

### Decision 1: 先写 hot-replace gate plan，不直接写 SDSL 落地计划

**Context:** 目标 water cbuffer 的完整数值、SRV 绑定和资源差异必须先从目标 capture 和外部 shader 目录导出；直接规划 SDSL/C# 会把未知数伪装成实现细节。

**Decision:** 本次只生成 `docs/superpowers/plans/2026-06-18-river-surface-calcwater-hotreplace-gate.md`。

**Rationale:** 这与用户要求一致：先在 RenderDoc 热修改确认有效，再修改 SDSL 代码。

---

## Next Session

### Immediate Next Steps

1. 用户选择执行方式。
2. 按 plan 执行 Task 1-7。
3. 若 gate 通过，再写第二阶段 SDSL/C# implementation plan。

### Docs to Read Before Next Session

- `docs/superpowers/specs/2026-06-18-river-surface-calcwater-strict-parity-design.md`
- `docs/superpowers/plans/2026-06-18-river-surface-calcwater-hotreplace-gate.md`

---

## Quick Notes For Future Codex

- 不要在 gate 通过前改 `Terrain.Editor/Effects/RiverSurface.sdsl`。
- 不要在项目代码、目录、资源 URL 或日志标签中引入外部产品名、产品缩写或来源标签类英文命名。
- 如果 hot-replace gate 失败，继续在 RenderDoc 中拆分变量，不进入 Stride asset rebuild。
