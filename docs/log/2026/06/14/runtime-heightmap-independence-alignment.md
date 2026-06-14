# Runtime 不再依赖 `heightmap` 声明
**Date**: 2026-06-14
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 收口 `Runtime 不依赖 heightmap.png` 的实现、测试与文档口径

**Secondary Objectives:**
- 保持 `heightmap` 继续留在 `default.toml` 和 Editor 作者态链路中
- 避免后续 review 再把 Runtime `heightmap` 依赖误判为需求

**Success Criteria:**
- Runtime bootstrap 不再解析或校验 `heightmap` 路径值
- Runtime bundle 不再携带 `HeightmapPath`
- 自动测试覆盖“缺失/非法 `heightmap` 仍可启动 Runtime”

---

## Context & Background

**Previous Work:**
- See: [runtime-game-root-and-required-resource-alignment.md](./runtime-game-root-and-required-resource-alignment.md)
- Related: [2026-06-13-editor-virtual-resource-system-design.md](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md)

**Current State:**
- Reviewer 指出 Runtime 仍把 `heightmap` 当成必需输入，但实际 detail map 高度来自 `.terrain`
- 用户明确确认：`heightmap` 继续保留在 `default.toml`，仅供 Editor 作者态加载/保存；Runtime 不依赖它

**Why Now:**
- 如果不收口，spec 与实现会继续分叉，且 Runtime 会保留一个“必需但未消费”的假依赖

---

## What We Did

### 1. 移除 Runtime 对 `heightmap` 的显式依赖
**Files Changed:** `Terrain/Resources/RuntimeMapDefinitionReader.cs`, `Terrain/Resources/TerrainRuntimeBootstrap.cs`, `Terrain/Resources/TerrainRuntimeResourceBundle.cs`

**Implementation:**
- `RuntimeMapDefinitionReader.ReadFrom(...)` 新增 `requireHeightmap` 开关
- Runtime bootstrap 改为 `requireHeightmap: false`
- Runtime bootstrap 不再解析 `mapDefinition.HeightmapPath`
- `TerrainRuntimeResourceBundle` 移除 `HeightmapPath` 与 `MapDefinition`

**Rationale:**
- Runtime 真实消费的是 `.terrain`、`biome_mask.png`、`biome_settings.toml` 和 `materials/descriptor.toml`
- `heightmap` 只保留给 Editor 作者态链路

**Architecture Compliance:**
- ✅ 与用户确认后的最终口径一致

### 2. 补 Runtime 忽略 `heightmap` 的回归测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs`

**Implementation:**
- 新增缺失 `heightmap.png` 仍能 bootstrap 的测试
- 新增缺失 `heightmap` 声明仍能 bootstrap 的测试
- 新增非法绝对 `heightmap` 声明仍能 bootstrap 的测试
- 移除对 `bundle.HeightmapPath` 的旧断言

**Rationale:**
- 需要把“Runtime 不解析、不校验 `heightmap`”正式钉成行为测试

**Architecture Compliance:**
- ✅ 测试直接体现新的 Runtime 边界

### 3. 同步 spec / 架构 / ADR / 计划状态说明
**Files Changed:** `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/decisions/adr-015-workspace-game-root-and-runtime-requirements.md`, `docs/superpowers/plans/2026-06-13-virtual-resource-system.md`

**Implementation:**
- spec 中移除 Runtime 必填资源里的 `heightmap`
- spec 明确 Runtime 忽略 `default.toml` 里的 `heightmap` 声明
- 架构/功能总览明确 Runtime detail map 高度来自 `.terrain`
- ADR-015 补充 Runtime 会忽略 `heightmap` 声明
- plan 顶部状态说明记录最终实现相对原计划的这个偏差

**Rationale:**
- 让 review、spec、当前实现指向同一套口径

**Architecture Compliance:**
- ✅ 文档与实现统一

---

## Decisions Made

### Decision 1: `heightmap` 继续留在 `default.toml`，但 Runtime 完全忽略
**Context:** Editor 仍需要 `heightmap` 作为作者态高度真相源，但 Runtime 实际只消费 `.terrain`
**Decision:** Runtime 只读取 `terrain_data` / fixed companions；`heightmap` 字段仅保留给 Editor
**Rationale:** 这最符合当前真实链路，也避免“必需但未消费”的伪依赖
**Trade-offs:** `RuntimeMapDefinition` 这个共享模型名义上仍含 `HeightmapPath`，但 Runtime 模式下会忽略它
**Documentation Impact:** 已更新 spec、ARCHITECTURE_OVERVIEW、CURRENT_FEATURES、ADR-015

---

## What Worked ✅

1. **用行为测试钉住“忽略输入”边界**
   - What: 分别测缺文件、缺声明、非法声明三种情况
   - Why it worked: 能直接覆盖“非依赖”而不是只覆盖 happy path
   - Reusable pattern: Yes

2. **先收实现，再同步文档**
   - What: 先移除 bundle / bootstrap 依赖，再改 spec 和 ADR
   - Impact: 文档更新更直接，不需要反向猜实现

---

## What Didn't Work ❌

1. **第一次整仓 `dotnet build Terrain.sln`**
   - What we tried: 与测试并行后直接跑 solution build
   - Why it failed: `Terrain.dll` 一度被本机进程占用，出现临时锁文件错误
   - Lesson learned: 这一仓库在连续验证时更适合串行 build
   - Don't try this again because: 容易被前一轮 `dotnet`/编译进程残留影响

---

## Problems Encountered & Solutions

### Problem 1: Runtime 仍通过共享 reader 间接要求 `heightmap`
**Symptom:** 即使 Runtime 不使用 `heightmap.png`，reader 仍把 `heightmap` 当必填字段
**Root Cause:** `RuntimeMapDefinitionReader` 同时服务 Editor 和 Runtime，原实现没有区分宿主边界
**Investigation:**
- Tried: 直接删掉 `RuntimeMapDefinition.HeightmapPath`
- Found: Editor 作者态链路仍依赖该字段

**Solution:**
- 给 reader 加 `requireHeightmap` 开关，让 Runtime 模式完全忽略该字段

**Why This Works:** Editor 继续保有强约束，Runtime 获得独立消费边界
**Pattern for Future:** 共享配置读取器若服务多个宿主，应显式建“必需字段策略”

### Problem 2: 首次 solution build 被锁文件打断
**Symptom:** `CS2012`，无法写入 `Terrain.dll`
**Root Cause:** 连续 `dotnet` 验证后有临时进程持有输出
**Investigation:**
- Tried: 查询 `dotnet` / `VBCSCompiler` 进程
- Tried: 改为串行 build，关闭共享编译
- Found: `dotnet build Terrain.sln -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false` 可通过

**Solution:**
- 使用串行构建参数重新验证

**Why This Works:** 降低并发编译与输出竞争
**Pattern for Future:** 遇到临时 DLL 锁，先重试串行 `dotnet build`

---

## Architecture Impact

### Documentation Updates Required
- [x] Update [2026-06-13-editor-virtual-resource-system-design.md](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md) - 移除 Runtime 对 `heightmap` 的依赖描述
- [x] Update ARCHITECTURE_OVERVIEW.md - 记录 Runtime detail map 高度来源于 `.terrain`
- [x] Update CURRENT_FEATURES.md - 标注 Runtime 忽略 `heightmap` 声明

### Architectural Decisions That Changed
- **Changed:** Runtime 对 `default.toml.heightmap` 的消费边界
- **From:** Runtime 形式上依赖 `heightmap`，但实际未消费
- **To:** Runtime 显式忽略 `heightmap`；它是 Editor-only 作者态字段
- **Scope:** `Terrain`, `Terrain.Editor.Tests`, spec / ADR / 架构文档
- **Reason:** 与真实运行链路和用户确认口径对齐

---

## Code Quality Notes

### Testing
- **Tests Written:** 3 个新的 Runtime bootstrap 回归点
- **Coverage:** 缺失 `heightmap.png`、缺失 `heightmap` 声明、非法 `heightmap` 声明
- **Manual Tests:** 未执行 Editor 手工冒烟

### Technical Debt
- **Created:** 无
- **Paid Down:** Runtime 对 `heightmap` 的伪依赖
- **TODOs:** reviewer 提到的稳定 `material.id` 问题仍未处理

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 修 `material.id` 稳定性问题 - 这是当前 review 剩余的主要实现缺口
2. 补“只读目标且不 fallback”的 Save / Export 集成测试 - 防止写回语义回退
3. 做一次真实 Editor 冒烟启动 - 验证当前固定入口工作流

### Blocked Items
- 无

### Questions to Resolve
1. `MaterialSlot` 的稳定 `MaterialId` 放在槽位层还是独立 authoring metadata 层

### Docs to Read Before Next Session
- [2026-06-13-editor-virtual-resource-system-design.md](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md) - 最新资源系统主口径
- [adr-015-workspace-game-root-and-runtime-requirements.md](../../../decisions/adr-015-workspace-game-root-and-runtime-requirements.md) - 当前根定位与 Runtime 边界

---

## Session Statistics

**Files Changed:** 10
**Lines Added/Removed:** 以本次 diff 为准
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain/Resources/RuntimeMapDefinitionReader.cs`, `Terrain/Resources/TerrainRuntimeBootstrap.cs`, `Terrain/Resources/TerrainRuntimeResourceBundle.cs`
- Critical decision: Runtime 完全忽略 `default.toml.heightmap`；它只属于 Editor 作者态链路
- Active pattern: 共享 reader 用策略参数区分 Editor / Runtime 的必填字段
- Current status: 自动测试与串行 solution build 均通过

**What Changed Since Last Doc Read:**
- Architecture: Runtime detail map 高度源明确改为 `.terrain`
- Implementation: Runtime bundle 移除了 `HeightmapPath`
- Constraints: `heightmap` 仍保留在 `default.toml`，但 Runtime 不读它

**Gotchas for Next Session:**
- Watch out for: 不要把 `heightmap` 重新加回 Runtime 必填资源
- Don't forget: Editor 仍然需要 `heightmap` 字段和文件
- Remember: 当前 review 还剩 `material.id` 稳定性问题未修

---

## Links & References

### Related Documentation
- [Editor / Runtime 共用虚拟资源系统设计](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md)
- [ADR-015](../../../decisions/adr-015-workspace-game-root-and-runtime-requirements.md)

### Related Sessions
- [runtime-game-root-and-required-resource-alignment.md](./runtime-game-root-and-required-resource-alignment.md)

### Code References
- `Terrain/Resources/RuntimeMapDefinitionReader.cs`
- `Terrain/Resources/TerrainRuntimeBootstrap.cs`
- `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs`

---

## Notes & Observations

- 这次收口后，`heightmap` 的角色已经明确分裂为 Editor 作者态输入，而不是 Runtime 消费输入
- 首次 solution build 的锁文件属于本地验证时序问题，不是本次改动引入的编译错误

---

*Template Version: 1.0 - Based on Archon-Engine template*
