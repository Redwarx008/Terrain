# GameRuntimeResourceBootstrap 命名收口
**Date**: 2026-06-14
**Session**: 5
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 将 `TerrainRuntimeBootstrap` 重命名为更准确的 `GameRuntimeResourceBootstrap`

**Secondary Objectives:**
- 用测试卡住新命名，避免只改一半
- 更新当前有效文档中的实现路径

**Success Criteria:**
- 生产代码与测试不再引用 `TerrainRuntimeBootstrap`
- 当前总览文档和功能清单改用新名称
- 自动测试通过

---

## Context & Background

**Previous Work:**
- Related: [adr-015-workspace-game-root-and-runtime-requirements.md](../../../log/decisions/adr-015-workspace-game-root-and-runtime-requirements.md)
- Related: [runtime-heightmap-independence-alignment.md](./runtime-heightmap-independence-alignment.md)

**Current State:**
- `Terrain/Resources/TerrainRuntimeBootstrap.cs` 实际负责的不只是 terrain，还包括 `default.toml`、material descriptor、biome settings 与可选 `rivers.png` 的解析

**Why Now:**
- 当前类型名把“资源 bundle 编排器”误导成了“只服务 terrain 的 bootstrap”，已经和实际职责不匹配

---

## What We Did

### 1. 先加文本回归测试卡住命名目标
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`

**Implementation:**
```csharp
TestHarness.Run("runtime resource bootstrap uses the game-scoped name", RuntimeResourceBootstrapUsesGameScopedName);
```

**Rationale:**
- 这次是命名收口，不是行为修改；文本级测试最适合先把“文件名 + 调用点”锁死

### 2. 统一重命名生产类型与测试引用
**Files Changed:** `Terrain/Resources/GameRuntimeResourceBootstrap.cs`, `Terrain/Core/TerrainProcessor.cs`, `Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
```csharp
return new GameRuntimeResourceBootstrap(resolver).Load();
```

**Rationale:**
- 新名称准确表达“这是 game 级资源 bundle bootstrap，而不是 terrain 专用加载器”

### 3. 更新当前有效文档口径
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- 将当前说明中的 `TerrainRuntimeBootstrap` 路径与名称更新为 `GameRuntimeResourceBootstrap`

**Rationale:**
- 这类更名如果不改总览文档，下一次会话恢复上下文时会继续被旧名字误导

---

## Decisions Made

### Decision 1: 保留 `Bootstrap`，只把作用域从 `Terrain` 改成 `Game`
**Context:** 当前类型仍然负责 resolver + map definition + companion resources 的编排，`Bootstrap` 一词仍成立

**Options Considered:**
1. `TerrainRuntimeBootstrap` - 旧名，职责范围过窄
2. `GameRuntimeResourceBootstrap` - 直接体现 game 级资源入口
3. `RuntimeResourceBundleLoader` - 也准确，但和现有 bootstrap 口径割裂

**Decision:** Chose Option 2
**Rationale:** 最小改动下即可把职责边界说清楚
**Trade-offs:** 仍保留 `Bootstrap` 命名，不刻意引入新的术语体系
**Documentation Impact:** 已更新 `ARCHITECTURE_OVERVIEW.md` 与 `CURRENT_FEATURES.md`

---

## What Worked ✅

1. **先写文本红灯再更名**
   - What: 先用文本测试断言新文件名和新调用点
   - Why it worked: 能精确证明这次改名没有留下半截旧引用
   - Reusable pattern: Yes

2. **只改当前有效文档**
   - What: 更新总览和功能清单，不回写历史日志
   - Impact: 当前口径正确，同时保留历史实现轨迹

---

## Problems Encountered & Solutions

### Problem 1: 单纯靠编译无法保证旧命名完全清除
**Symptom:** 编译通过并不代表旧文件名、旧调用点、旧文档引用都已经消失
**Root Cause:** 类型改名后，历史残留可能仍存在于文本级调用点或文档里
**Investigation:**
- Tried: 直接重命名生产代码
- Found: 需要文本级断言才能卡住文件名和 `new ...Bootstrap(...)` 调用点

**Solution:**
```csharp
TestHarness.Assert(processor.Contains("new GameRuntimeResourceBootstrap(", StringComparison.Ordinal), "TerrainProcessor should use GameRuntimeResourceBootstrap");
```

**Why This Works:** 命名收口本质上是源码和文档的一致性问题，文本测试比行为测试更直接
**Pattern for Future:** 对纯命名/迁移收口，优先用文本测试锁定目标口径

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `ARCHITECTURE_OVERVIEW.md` - runtime resource bootstrap 名称更新
- [x] Update `CURRENT_FEATURES.md` - 当前实现路径更新

### Architectural Decisions That Changed
- **Changed:** 无架构行为变化
- **Scope:** 命名与文档收口
- **Reason:** 当前类型名与职责边界不匹配

---

## Code Quality Notes

### Testing
- **Tests Written:** 1 条文本回归测试
- **Coverage:** bootstrap 文件名、`TerrainProcessor` 调用点
- **Manual Tests:** 未执行 GUI/runtime 手工验证；本次无行为改动

### Technical Debt
- **Paid Down:** 去掉误导性的 runtime bootstrap 命名

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 继续补 runtime river 加载链路 - 这是当前真正缺失的功能，不是本次命名问题
2. 评估是否需要把 editor/runtime 共用 river 类型下沉到 `Terrain` 程序集

### Questions to Resolve
1. runtime river 应该复用 editor 现有 river 组件体系，还是先抽共享子系统

### Docs to Read Before Next Session
- [adr-014-river-rendering-architecture.md](../../../log/decisions/adr-014-river-rendering-architecture.md) - river 独立 processor/render feature 架构约束
- [adr-015-workspace-game-root-and-runtime-requirements.md](../../../log/decisions/adr-015-workspace-game-root-and-runtime-requirements.md) - runtime 资源入口边界

---

## Session Statistics

**Files Changed:** 7 个已跟踪代码/文档文件 + 2 个重命名后的新跟踪目标文件 + 1 个新日志
**Lines Added/Removed:** `git diff --stat` 显示本轮以 rename 形式收口；若只看已跟踪 diff 会表现成少量改动加 2 个旧文件删除，因此提交前必须同时纳入新的重命名目标文件
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 生产类型已从 `TerrainRuntimeBootstrap` 更名为 `GameRuntimeResourceBootstrap`
- `TerrainProcessor` 已切到新名称
- 当前总览文档已同步到新路径

**What Changed Since Last Doc Read:**
- Implementation: runtime 资源 bundle 编排器改名为 `GameRuntimeResourceBootstrap`
- Constraints: 这次不涉及 runtime 行为变化，只是职责命名收口

**Gotchas for Next Session:**
- Watch out for: 历史日志和旧计划仍会提到 `TerrainRuntimeBootstrap`，那是历史名称，不要误当成当前实现
- Remember: runtime river 仍未接入，这次没有处理功能缺口

---

## Links & References

### Related Documentation
- [ARCHITECTURE_OVERVIEW.md](../../../ARCHITECTURE_OVERVIEW.md)
- [CURRENT_FEATURES.md](../../../CURRENT_FEATURES.md)

### Related Sessions
- [runtime-heightmap-independence-alignment.md](./runtime-heightmap-independence-alignment.md)

### Code References
- Runtime bootstrap implementation: `Terrain/Resources/GameRuntimeResourceBootstrap.cs`
- Runtime consumer: `Terrain/Core/TerrainProcessor.cs`
- Rename guard test: `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`

---

## Notes & Observations

- `TerrainRuntimeBootstrap` 真正的问题不是 `Bootstrap`，而是 `Terrain` 这个前缀已经不匹配职责边界。
- 对这种“类型名已经背离职责”的情况，先补文本回归测试再更名，收口会更稳。

---
