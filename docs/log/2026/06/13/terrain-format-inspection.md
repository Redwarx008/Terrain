# Terrain Format Inspection
**Date**: 2026-06-13
**Session**: 1
**Status**: ✅ Complete
**Priority**: Low

---

## Session Goal

**Primary Objective:**
- 核查当前 `.terrain` 文件实际包含哪些数据块，确认是否包含 `rivers.png` 与 `provinces.png`

**Secondary Objectives:**
- 区分“运行时格式支持什么”与“Editor 当前实际导出了什么”

**Success Criteria:**
- 给出基于代码的明确结论，并标出关键实现位置

---

## Context & Background

**Previous Work:**
- Related: 虚拟资源系统与资源组织方式重构讨论

**Current State:**
- 当前工作区内未发现可直接检视的 `.terrain` 样例文件，本次结论来自 reader / exporter / runtime 使用路径的代码核查

**Why Now:**
- 需要在继续调整 `map_data/default.toml` 与运行时资源解析前，先确认 `.terrain` 的真实职责边界

---

## What We Did

### 1. 核查 `.terrain` 运行时格式定义
**Files Changed:** `无`

**Implementation:**
- 阅读 `Terrain/Streaming/TerrainStreaming.cs`
- 确认 `TerrainFileHeader` 包含高度图、splat/biome mask，以及 v7 可选 river map 的头字段
- 确认 `TerrainFileReader` 暴露 `ReadAllHeightData()`、`ReadAllBiomeMaskData()`、`ReadAllRiverMaskData()`

**Rationale:**
- 先确定格式“理论上可承载什么”，再看当前写入与消费路径

### 2. 核查 Editor 当前实际导出内容
**Files Changed:** `无`

**Implementation:**
- 阅读 `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`
- 确认当前仅写入：
- 文件头
- MinMaxErrorMap
- HeightMap VT
- BiomeMask VT
- 未写入 RiverMap VT
- 未写入 provinces 相关数据

**Rationale:**
- 直接回答当前 Editor 产出的 `.terrain` 实际内容

### 3. 核查 Runtime 当前实际消费内容
**Files Changed:** `无`

**Implementation:**
- 阅读 `Terrain/Core/TerrainProcessor.cs`
- 确认当前运行时使用 `ReadAllHeightData()` 与 `ReadAllBiomeMaskData()` 生成 detail map
- 未发现当前路径消费 `ReadAllRiverMaskData()`

**Rationale:**
- 避免把“格式支持 river”误判为“当前运行时已经在用 river”

---

## Decisions Made

### Decision 1: 以代码实现为准界定 `.terrain` 当前职责
**Context:** 工作区内没有现成 `.terrain` 样例可直接 dump
**Options Considered:**
1. 仅凭记忆判断 - 风险高
2. 以 reader/exporter/runtime 三条代码路径交叉核对 - 最可靠

**Decision:** 选择 Option 2
**Rationale:** 能同时回答格式支持、当前导出、当前消费三件事
**Trade-offs:** 结论针对当前代码实现，不代表外部其他生成器写出的自定义 `.terrain`
**Documentation Impact:** 仅记录会话日志，无需更新架构总览

---

## What Worked ✅

1. **三路径交叉核对**
   - What: 同时看 reader、exporter、runtime
   - Why it worked: 能快速分清“支持”和“实际在用”
   - Reusable pattern: Yes

2. **先确认样例是否存在**
   - What: 搜索工作区中的 `.terrain` 文件
   - Impact: 及时发现无法做二进制样例核查，需要转为实现核查

---

## Problems Encountered & Solutions

### Problem 1: 工作区没有可直接检查的 `.terrain` 样例
**Symptom:** `rg --files -g '*.terrain'` 无结果
**Root Cause:** 当前仓库未提交运行时 `.terrain` 二进制产物
**Investigation:**
- Tried: 搜索工作区内 `.terrain`
- Found: 没有样例文件

**Solution:**
- 改为核查格式 reader、Editor exporter 与 Runtime 消费路径

**Why This Works:** 三处代码足以界定当前实现行为
**Pattern for Future:** 二进制产物缺失时，优先用读写实现反推真实格式

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update design doc - 本次无需
- [ ] Update ARCHITECTURE_OVERVIEW.md - 本次无需

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 用 reader/exporter/runtime 三点核查资源格式边界
- When to use: 资源格式职责不清、二进制样例缺失时
- Benefits: 结论更稳，不容易把历史记忆当成现状

---

## Code Quality Notes

### Testing
- **Manual Tests:** 无，本次为静态实现核查

### Technical Debt
- **TODOs:** 若后续需要 river 进入 `.terrain` 主流程，还需补齐 exporter 写入与 runtime 消费链路

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 根据本次结论继续收敛 `map_data/default.toml` 的字段边界
2. 在运行时资源解析改造时明确 `.terrain` 仅承载二进制 VT 数据
3. 如需要 river 进入 `.terrain`，单独补齐写入与读取使用链路

### Questions to Resolve
1. `rivers.png` 是否继续作为独立源文件参与导入，再导出为 `.terrain` 的可选 river block
2. `provinces.png` 在新资源组织中是否始终保持为独立资源，不进入 `.terrain`

---

## Session Statistics

**Files Changed:** 1
**Lines Added/Removed:** +133/-0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 当前 `.terrain` 格式定义见 `Terrain/Streaming/TerrainStreaming.cs`
- 当前 Editor 导出只写 height + biome mask，未写 river/provinces
- 当前 Runtime 使用 height + biome mask，未消费 river

**What Changed Since Last Doc Read:**
- Architecture: 无变化
- Implementation: 无变化
- Constraints: 对 `.terrain` 职责边界有了更明确结论

**Gotchas for Next Session:**
- Watch out for: 不要把 `SplatMap` 名称误解为老的 detail index map；v6 以后这里持久化的是 `BiomeMask`
- Remember: `RiverMap` 是格式支持项，不等于当前导出链路已经启用

---

## Links & References

### Code References
- Format header and reader: `Terrain/Streaming/TerrainStreaming.cs:103-269`
- Export path: `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs:92-145`
- Runtime consumption: `Terrain/Core/TerrainProcessor.cs:457-470`

---

## Notes & Observations

- 当前仓库里没有可直接检查的 `.terrain` 样例文件
- 用户关于 “`.terrain` 不该包含 provinces.png” 的记忆与当前实现一致

---

*Template Version: 1.0 - Based on Archon-Engine template*
