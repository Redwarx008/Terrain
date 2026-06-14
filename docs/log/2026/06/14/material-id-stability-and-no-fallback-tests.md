# 材质稳定 ID 与 no-fallback 写回测试
**Date**: 2026-06-14
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复 reviewer 指出的 `material.id` 不稳定问题

**Secondary Objectives:**
- 补 `Save` 写回与 `Export` 回滚的 no-fallback 回归测试
- 保持现有虚拟资源系统口径不变

**Success Criteria:**
- 已加载 descriptor 的材质槽即使改显示名，保存后 `material.id` 不变化
- 新增自动测试覆盖只读目标失败且不写 fallback
- 测试与 solution build 都通过

---

## Context & Background

**Previous Work:**
- See: [runtime-heightmap-independence-alignment.md](./runtime-heightmap-independence-alignment.md)
- Related: [2026-06-13-editor-virtual-resource-system-design.md](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md)

**Current State:**
- Runtime `heightmap` 口径已收口
- reviewer 剩余主要问题变成材质 `material.id` 稳定性

**Why Now:**
- 如果不修，Editor 每次改材质显示名都可能重写 descriptor / biome settings 的 `material_id`

---

## What We Did

### 1. 为材质槽加入稳定 `MaterialId`
**Files Changed:** `Terrain.Editor/Services/MaterialSlot.cs`, `Terrain.Editor/Services/MaterialSlotManager.cs`, `Terrain.Editor/Services/Resources/EditorAuthoringResourceMapper.cs`

**Implementation:**
- `MaterialSlot` 新增 `MaterialId`
- `Clear()` 时清掉 `MaterialId`
- `ApplyDescriptor()` 时把 `RuntimeMaterialEntry.Id` 回填到槽位
- 导出 descriptor 时优先使用现有 `MaterialId`
- 对新建槽位首次生成 `id` 后，立即回写到槽位本身，避免后续因显示名变化而漂移

**Rationale:**
- `material.id` 应该是稳定身份，不应跟随显示名变化

**Architecture Compliance:**
- ✅ 符合 spec 中“稳定材质标识，供 biome 规则引用”的要求

### 2. 用 TDD 补稳定 ID 回归测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorAuthoringResourceMapperTests.cs`

**Implementation:**
- 先新增 failing test：加载已有 descriptor 后改显示名，导出 `id` 仍应保持原值
- 观察测试失败，确认当前行为确实会把 `soft_grass` 改成 `renamed_grass`
- 写最小实现后重跑转绿

**Rationale:**
- 这个缺口必须通过真实 round-trip 行为测试钉住

**Architecture Compliance:**
- ✅ 直接覆盖 reviewer 关注的核心行为

### 3. 补 no-fallback 写回 / 导出测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`, `Terrain.Editor.Tests/VirtualResources/ExportWorkflowTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 新增 `heightmap writer fails on read-only target without touching fallback`
- 新增 `material descriptor writer fails on read-only target without touching fallback`
- 新增 `export manager rolls back failed export without touching fallback`
- 使用 `ResolvedGameResource.IsWritable = false` + `HasLowerPriorityFallback = true` 验证只读命中目标直接失败，且低优先级文件不变
- 使用抛错 exporter 验证导出失败时只回滚命中目标文件，不会污染低优先级 fallback

**Rationale:**
- 需要把“命中的目标不可写时失败且不 fallback”以及“导出失败时只回滚命中目标”从文档约束变成可执行测试

**Architecture Compliance:**
- ✅ 与虚拟资源系统写回规则一致

---

## Decisions Made

### Decision 1: 稳定 ID 存放在 `MaterialSlot`
**Context:** 保存 descriptor 时需要保住材质稳定身份，且这个身份必须在 Editor 内存状态中持续存在
**Decision:** 直接在 `MaterialSlot` 上增加 `MaterialId`
**Rationale:** 这是最小改动路径，能自然跟随槽位加载、清空、导出生命周期
**Trade-offs:** `MaterialSlot` 承担了少量作者态元数据职责
**Documentation Impact:** 无需新增 ADR

---

## What Worked ✅

1. **最小 failing test 先行**
   - What: 只测“改显示名后 id 不变”
   - Why it worked: 精准命中 bug，不需要先改大块实现
   - Reusable pattern: Yes

2. **把生成的新 ID 回写到槽位**
   - What: 不只保留已加载 id，也让新建槽位首次生成后变成稳定值
   - Impact: 避免同一会话里二次保存继续漂移

---

## What Didn't Work ❌

1. **无**
   - 这轮没有遇到新的实现阻塞

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update ARCHITECTURE_OVERVIEW.md - 不需要，系统结构未变
- [ ] Update CURRENT_FEATURES.md - 不需要，功能边界未变

### Architectural Decisions That Changed
- **Changed:** 无新增架构决策
- **Scope:** Editor authoring metadata 与测试
- **Reason:** 属于既有设计的实现补全，不是新架构方向

---

## Code Quality Notes

### Testing
- **Tests Written:** 4 个
- **Coverage:** 稳定 `material.id`、heightmap no-fallback、descriptor no-fallback、export rollback no-fallback
- **Manual Tests:** 未执行 Editor 手工冒烟

### Technical Debt
- **Created:** 无
- **Paid Down:** reviewer 关于稳定 `material.id` 的主要缺口
- **TODOs:** 若继续收紧，可再给 Export `.terrain` 增加只读目标失败测试

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 如需完全消化 reviewer 反馈，可考虑再启动一次 review
2. 若开始冒烟验证，重点看 Save / Export 在只读文件上的 UI 提示
3. 继续处理与当前资源系统无关的遗留 warning / vulnerability 时，注意不要混入本 feature

### Blocked Items
- 无

### Questions to Resolve
1. 是否要为 `TerrainExporter` 本体再补一条显式只读目标失败测试（当前已覆盖 ExportManager 回滚层）

### Docs to Read Before Next Session
- [2026-06-13-editor-virtual-resource-system-design.md](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md) - 当前资源系统主口径

---

## Session Statistics

**Files Changed:** 4 个代码/测试文件 + 1 个日志文件
**Lines Added/Removed:** 以本次 diff 为准
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `MaterialSlot.MaterialId` 现在是稳定作者态材质身份
- Critical decision: 已加载 descriptor 的 `id` 必须保留，不能按显示名重生成
- Active pattern: descriptor -> slot metadata -> export round-trip
- Current status: 测试和 solution build 均通过，ExportManager rollback 也已测住

**What Changed Since Last Doc Read:**
- Implementation: `MaterialSlot` 现在持有稳定 `MaterialId`
- Tests: 新增稳定 ID、writer no-fallback、export rollback no-fallback 覆盖
- Constraints: 只读命中目标仍按失败处理，不自动 fallback

**Gotchas for Next Session:**
- Watch out for: 不要在导出 descriptor 时再绕过 `MaterialId` 去重新按名字生成
- Don't forget: 新建槽位首次生成 `id` 后需要继续复用
- Remember: 当前 export 侧已覆盖 rollback/no-fallback 管理层，但还没直接实例化 `TerrainExporter` 做只读目标测试

---

## Links & References

### Related Sessions
- [runtime-heightmap-independence-alignment.md](./runtime-heightmap-independence-alignment.md)

### Code References
- `Terrain.Editor/Services/MaterialSlot.cs`
- `Terrain.Editor/Services/MaterialSlotManager.cs`
- `Terrain.Editor/Services/Resources/EditorAuthoringResourceMapper.cs`
- `Terrain.Editor.Tests/VirtualResources/EditorAuthoringResourceMapperTests.cs`

---

## Notes & Observations

- 这次 fix 之后，`material.id` 的稳定性终于从“推测成立”变成了测试钉住的行为
- no-fallback 规则目前至少已经在作者态 writer 层有可执行回归覆盖

---

*Template Version: 1.0 - Based on Archon-Engine template*
