# 虚拟资源系统收口：事务化作者态保存与 Runtime 失败门禁
**Date**: 2026-06-14
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 收口 subagent review 指出的两个实现缺口：作者态 `Save` 必须整组回滚，Runtime 缺资源失败后必须锁住同配置重试

**Secondary Objectives:**
- 同步 `docs/superpowers/plans/2026-06-13-virtual-resource-system.md` 的最终口径
- 用自动测试覆盖这两个收口行为

**Success Criteria:**
- `Save` 任一后续 writer 失败时，之前资源不落到最终目标
- Runtime 缺失 `.terrain` / `biome_mask.png` 时记录错误并保持失败门禁
- 测试和构建全部通过

---

## Context & Background

**Previous Work:**
- See: [virtual-resource-review-followup.md](./virtual-resource-review-followup.md)
- See: [material-id-stability-and-no-fallback-tests.md](./material-id-stability-and-no-fallback-tests.md)
- See: [runtime-heightmap-independence-alignment.md](./runtime-heightmap-independence-alignment.md)
- Related: [2026-06-13-editor-virtual-resource-system-design.md](../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md)

**Current State:**
- Runtime 已经不再依赖 `heightmap`
- Editor/Runtime 资源根与 resolver 逻辑已经统一
- reviewer 仍指出 `SaveAuthoringResources()` 顺序直写、plan 正文残留旧口径、processor 级失败门禁缺测试

**Why Now:**
- 这是 merge 前最后一批高风险收口项，不补齐就仍然存在“半写入作者态资源”和“运行时失败逐帧重试”两个回归面

---

## What We Did

### 1. 把作者态保存抽成事务化编排
**Files Changed:** `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`, `Terrain.Editor/Services/Resources/AtomicResourceWriteTransaction.cs`, `Terrain.Editor/Services/Resources/*Writer.cs`

**Implementation:**
- 新增 `EditorResourceSaveService.Save(...)`，把 `map definition`、`heightmap`、`biome mask`、`material descriptor`、`biome settings` 的保存编排从 `TerrainManager` 中抽离
- 新增 `AtomicResourceWriteTransaction`，先给每个目标生成同目录 staging 文件，全部 writer 成功后再统一 `Commit()`
- `Commit()` 对已存在目标使用 `File.Replace(..., backup)`，对新目标使用 `File.Move(...)`；任一步失败则回滚已替换/已创建的最终文件
- `TerrainManager.SaveAuthoringResources()` 现在只负责组装 snapshot，再委托给 `EditorResourceSaveService`
- 各 writer 新增 `internal Write(string outputPath, ...)` 重载，保留原有 session 级写法用于单文件测试

**Rationale:**
- 事务边界应该在“整组作者态资源保存”这一层，而不是散落在各个单文件 writer 中
- 这样既保留原有 writer 的单一职责，也让回滚行为可独立测试

**Architecture Compliance:**
- ✅ 符合虚拟资源系统设计中“最终写回当前命中的实体文件，失败不 fallback”的约束
- ✅ 补齐了 reviewer 要求的“失败时整组回滚”

### 2. 给 Runtime 缺资源失败加统一门禁入口
**Files Changed:** `Terrain/Core/TerrainProcessor.cs`, `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs`, `Terrain.Editor.Tests/VirtualResources/FakeTerrainFileReader.cs`

**Implementation:**
- 新增 `TerrainProcessor.TryLoadRuntimeData(...)`
- 该入口负责：
  - 执行 bundle 加载与 `CreateLoadedTerrainData(...)`
  - 成功时 `MarkRuntimeLoadSuccess(...)`
  - 失败时 `MarkRuntimeLoadFailure(...)`、保持 `IsInitialized = false`
  - 统一通过 `Terrain runtime resources could not be read:` 前缀记录 error
  - 对 `FileNotFoundException` 额外把 `FileName` 拼进日志内容
- `Initialize(...)` 现在通过该入口驱动失败门禁，而不是只在外层简单地 `TryLoadTerrainData -> return false`

**Rationale:**
- 失败门禁是 processor 行为，不应该只靠 bootstrap 单元测试间接证明
- 需要一个不依赖 GPU 资源的可测试入口，把“失败后组件状态怎么变化”单独钉住

**Architecture Compliance:**
- ✅ 符合当前 Runtime 口径：缺 `terrain.terrain` / `biome_mask.png` 时报错并保持未初始化
- ✅ 符合“不在同配置下逐帧重试”的约束

### 3. 补自动测试并同步文档口径
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs`, `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`, `docs/superpowers/plans/2026-06-13-virtual-resource-system.md`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- 新增红灯测试 `authoring save rolls back earlier files when a later writer fails`
  - 使用非法 `MaterialDescriptor` 贴图路径触发后续 writer 失败
  - 断言此前的 `default.toml` / `heightmap.png` / `biome_mask.png` / `biome_settings.toml` 都保留原值
- 新增 transaction 级测试：
  - `transaction dispose preserves backups for uncommitted work`
  - `transaction restores earlier replaced files when a later commit step fails`
- 新增 processor 级红灯测试：
  - `runtime load failure marks component when terrain data is missing`
  - `runtime load failure marks component when biome mask is missing`
- 放宽文本测试里对 `Log.Error(...)` 内部变量名的硬编码，只要求错误级别和固定前缀
- 在 plan 中给旧红/绿阶段片段补充 superseded 注记，明确最终口径以状态更新/spec/源码为准
- 在总览文档中补充“作者态保存是事务化写回”的当前事实
- 修复 reviewer 二次复核指出的 backup 生命周期问题，并在复核后确认 `Critical/Important/Minor` 均为 `无`

**Rationale:**
- 这批问题本质上都是 orchestration 层行为，不靠文本级检查很难长期稳定
- plan 保留历史实施轨迹没问题，但必须明确哪些片段已经被最终实现覆盖

**Architecture Compliance:**
- ✅ 文档和当前实现口径重新对齐

---

## Decisions Made

### Decision 1: 事务边界放在保存编排层，而不是各 writer 内部
**Context:** reviewer 指出 `SaveAuthoringResources()` 顺序直写会留下半成品
**Options Considered:**
1. 在每个 writer 内部单独做备份/恢复
2. 在 `TerrainManager.SaveAuthoringResources()` 外层直接硬写回滚逻辑
3. 抽出独立保存服务 + 原子文件事务

**Decision:** Chose Option 3
**Rationale:** writer 继续只关心单文件格式，事务边界与回滚策略集中在一处，测试粒度也更合理
**Trade-offs:** 新增了一层服务和事务工具类
**Documentation Impact:** 已更新 `ARCHITECTURE_OVERVIEW.md` 与 `CURRENT_FEATURES.md`

### Decision 2: Runtime 失败日志保留稳定前缀，但补充文件路径
**Context:** 既要维持已有“错误而不是 warning”的迁移约束，也要让 processor 级测试能断言缺的是哪一个资源
**Options Considered:**
1. 只记 `exception.Message`
2. 只记固定前缀，不带资源路径
3. 保留固定前缀，对 `FileNotFoundException` 拼入 `FileName`

**Decision:** Chose Option 3
**Rationale:** 同时满足文本级迁移检查和行为级诊断需要
**Trade-offs:** 文本测试不能再绑死内部变量名
**Documentation Impact:** 无需新 ADR

---

## What Worked ✅

1. **先写红灯测试再抽服务**
   - What: 先加 `EditorResourceSaveServiceTests` 和 processor 级失败门禁测试，再落地新服务和新入口
   - Why it worked: 红灯直接把改动边界限制在两个行为缺口，避免“顺手重构”扩散
   - Reusable pattern: Yes

2. **用非法相对贴图路径制造后续 writer 失败**
   - What: 通过 `nested/grass.png` 触发 `MaterialDescriptorWriter` 的路径校验失败
   - Impact: 能稳定证明前面 staged 的资源不会落到最终目标

---

## Problems Encountered & Solutions

### Problem 1: Runtime 错误日志的文本测试过于耦合内部变量名
**Symptom:** 行为测试已经通过，但 `RuntimeMigrationTextTests` 仍失败
**Root Cause:** 旧文本断言要求源码里必须出现 `Log.Error($"Terrain runtime resources could not be read: {exception.Message}")`
**Investigation:**
- Tried: 保持新 helper 负责完整日志格式
- Found: 这样会把文本测试绑到 helper 内部变量名而不是行为本身

**Solution:**
- 让 `TerrainProcessor` 保留固定 `Log.Error($"Terrain runtime resources could not be read: ...")` 前缀
- 文本测试只检查 error 前缀，不再检查内部插值变量名

**Why This Works:** 约束真正重要的是日志级别和错误前缀，而不是局部变量名
**Pattern for Future:** 文本迁移测试要约束语义，不要过度约束实现细节

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/superpowers/plans/2026-06-13-virtual-resource-system.md` - 标注已被最终实现覆盖的历史片段
- [x] Update `ARCHITECTURE_OVERVIEW.md` - 补充作者态保存事务化写回
- [x] Update `CURRENT_FEATURES.md` - 补充 `Save` 的事务性与当前 runtime 失败门禁

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 保存编排层事务化写回
- When to use: 多个作者态资源必须作为一个逻辑单元落盘时
- Benefits: 避免半写入状态污染工作区
- Add to: 后续相关设计/实现文档

---

## Code Quality Notes

### Testing
- **Tests Written:** 5 条关键行为测试
- **Coverage:** 保存前置失败回滚、transaction dispose backup 保留、commit-phase rollback、缺 `terrain.terrain` 的 runtime 失败门禁、缺 `biome_mask.png` 的 runtime 失败门禁
- **Manual Tests:** 未执行 Editor GUI 冒烟

### Technical Debt
- **Paid Down:** 去掉 `SaveAuthoringResources()` 顺序直写风险
- **TODOs:** 若继续收口 reviewer 建议，可补 `TerrainExporter` 真实只读目标失败路径的集成测试

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 做一次 Editor 手工冒烟，确认无 `.terrain` / 无 `biome_mask.png` 的启动体验
2. 如果还要继续抬高保证，再考虑补 `TerrainExporter` 真实只读目标集成测试
3. 评估是否需要给 runtime failure gate 增加显式 retry/invalidate hook

### Blocked Items
- **Blocker:** 无代码级 blocker

### Questions to Resolve
1. 是否需要把 `.terrain` export 的失败路径再上升到更靠近 UI 的集成测试

### Docs to Read Before Next Session
- [2026-06-13-editor-virtual-resource-system-design.md](../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md) - 当前最终口径
- [virtual-resource-review-followup.md](./virtual-resource-review-followup.md) - reviewer 上一轮问题背景

---

## Session Statistics

**Files Changed:** 17（含 4 个新增文件）
**Lines Added/Removed:** 约 +500 / -600
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`, `Terrain.Editor/Services/Resources/AtomicResourceWriteTransaction.cs`
- Critical decision: 事务边界放在保存编排层，而不是各 writer
- Active pattern: Runtime 缺资源失败统一走 `TerrainProcessor.TryLoadRuntimeData(...)`
- Current status: 代码、测试、plan 口径已经收口；二次 subagent review 已确认可 merge

**What Changed Since Last Doc Read:**
- Architecture: 作者态 `Save` 现在是事务化写回
- Implementation: Runtime 缺资源失败会锁住同配置重试
- Constraints: 文本迁移测试不再绑死错误日志内部变量名

**Gotchas for Next Session:**
- Watch out for: 新增资源 writer 时要接入 `EditorResourceSaveService`，不要重新走顺序直写
- Don't forget: Runtime 仍然忽略 `heightmap`，但 Editor 作者态仍然依赖它
- Remember: `terrain.terrain` 和 `biome_mask.png` 在 Editor 启动时允许缺失，在 Runtime 不允许缺失

---

## Links & References

### Related Documentation
- [Editor / Runtime 共用虚拟资源系统设计](../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md)
- [架构总览](../../../ARCHITECTURE_OVERVIEW.md)
- [功能清单](../../../CURRENT_FEATURES.md)

### Related Sessions
- [virtual-resource-review-followup.md](./virtual-resource-review-followup.md)
- [material-id-stability-and-no-fallback-tests.md](./material-id-stability-and-no-fallback-tests.md)
- [runtime-heightmap-independence-alignment.md](./runtime-heightmap-independence-alignment.md)

### Code References
- Transaction save orchestration: `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`
- Atomic rollback helper: `Terrain.Editor/Services/Resources/AtomicResourceWriteTransaction.cs`
- Runtime failure gate: `Terrain/Core/TerrainProcessor.cs`

---

## Notes & Observations

- 这次 reviewer 的 critical 是真实问题，不是测试噪音；事务化写回必须单独作为一层存在。
- plan 保留历史红/绿阶段片段可以接受，但必须明确哪些片段已经被最终实现覆盖。
