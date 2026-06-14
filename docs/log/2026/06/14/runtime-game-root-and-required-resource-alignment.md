# 虚拟资源系统收口：`game/` 根定位与 Runtime 必需资源
**Date**: 2026-06-14
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 收口 `GameResourceRootLocator` 的工作区优先定位与当前实现口径
- 明确 Editor / Runtime 对 `.terrain` 与 `biome_mask.png` 的不同要求
- 补关键集成测试并同步 spec / 架构文档

**Secondary Objectives:**
- 校验真实仓库 `game/` scaffold 是否符合当前实现
- 把这次行为边界沉淀成 ADR

**Success Criteria:**
- Editor 在缺失 `.terrain` / `biome_mask.png` 时仍能从仓库 `game/` 启动
- Runtime 缺失 `.terrain` / `biome_mask.png` 时会记录错误日志并拒绝初始化
- 文档不再描述旧的资源根规则或不存在的诊断态

---

## Context & Background

**Previous Work:**
- See: [game-root-resource-bootstrap-and-scaffold.md](./game-root-resource-bootstrap-and-scaffold.md)
- Related: [2026-06-13-editor-virtual-resource-system-design.md](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md)

**Current State:**
- 资源系统主链路已切到 `game/LaunchSetting.json` + `map_data/default.toml`
- 根定位需要同时说明“工作区优先”与“直接命中的完整 `game/` 根仍可接受”
- Runtime bootstrap 失败仍用 warning 级别记录

**Why Now:**
- 当前仓库 `game/` scaffold 故意不提交 `.terrain` 与 `biome_mask.png`
- 如果不把 Editor / Runtime 边界写清并测住，后续改动很容易把旧路径或错误必需性带回来

---

## What We Did

### 1. 收口 `game/` 根定位口径
**Files Changed:** `Terrain/Resources/GameResourceRootLocator.cs`

**Implementation:**
- 资源根要求目录名为 `game` 且同时包含 `LaunchSetting.json` 与 `map_data/`
- 向上搜索时优先选择工作区根下的 `game/`
- 如果起点本身已位于一个完整 `game/` 根，也会直接接受该根
- 新增测试覆盖从普通二进制目录启动时仍优先命中工作区 `game/`

**Rationale:**
- 用户要求默认资源根应指向工作区 `Terrain/game`；同时当前实现仍保留对直接命中的完整 `game/` 根的兼容

**Architecture Compliance:**
- ✅ 与当前代码实现一致

### 2. 明确 Runtime 缺失关键资源时的失败行为
**Files Changed:** `Terrain/Core/TerrainProcessor.cs`, `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs`, `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`

**Implementation:**
- `TerrainRuntimeBootstrap` 测试新增缺失 `.terrain` 与缺失 `biome_mask.png` 的失败用例
- `TerrainProcessor` 在 bootstrap 失败时从 `Log.Warning(...)` 改为 `Log.Error(...)`
- 文本测试钉住日志级别，避免回退

**Rationale:**
- `.terrain` 与 `biome_mask.png` 是 Runtime 消费链路的硬依赖，缺失时不应再默默降级

**Architecture Compliance:**
- ✅ 与 Runtime 资源严格消费边界一致

### 3. 补真实仓库 `game/` scaffold 集成测试并同步文档
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/GameResourceScaffoldTextTests.cs`, `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/superpowers/plans/2026-06-13-virtual-resource-system.md`, `docs/log/decisions/adr-015-workspace-game-root-and-runtime-requirements.md`

**Implementation:**
- 测试仓库 `game/` scaffold 必备文本资源与 CK3 材质贴图存在
- 测试真实 `game/` scaffold 在缺失 `.terrain` / `biome_mask.png` 时仍可被 `EditorBootstrapService` 加载
- 更新 spec，明确：
  - 资源定位优先命中工作区 `game/`
  - 起点本身若已在完整 `game/` 根内，也会直接接受该根
  - Editor 启动不要求 `.terrain` / `biome_mask.png`
  - Runtime 要求这两个文件并在缺失时记 error
  - 当前 `Save` 不写回 `rivers.png` 和材质贴图文件

**Rationale:**
- 避免文档继续承诺当前并不存在的“诊断态”或错误的资源必需性

**Architecture Compliance:**
- ✅ 文档口径与当前代码一致

---

## Decisions Made

### Decision 1: 工作区 `game/` 优先，但直接命中的完整 `game/` 根仍可接受
**Context:** 工作区 `game/` 是作者态资源主入口，但当前实现仍保留对直接命中的完整 `game/` 根的接受行为
**Decision:** 向上搜索时优先命中工作区 `game/`；如果起点本身已落在完整合法的 `game/` 根内，也直接接受该根
**Rationale:** 这与当前实现一致，同时仍把默认工作流约束在工作区 `game/`
**Trade-offs:** 文档必须明确说明这不是“绝对拒绝所有 `Bin/.../game`”的严格策略
**Documentation Impact:** 已提取 ADR-015，并更新 spec / 架构文档

### Decision 2: Editor / Runtime 对 `.terrain` 与 `biome_mask.png` 的要求分离
**Context:** Editor 负责作者态，Runtime 负责消费运行时二进制
**Decision:** Editor 允许缺失并保留写回目标；Runtime 缺失即错误日志并拒绝初始化
**Rationale:** 这与当前仓库 scaffold 和工作流一致
**Trade-offs:** 同一资源在两个宿主中的“必需性”不同，需要文档明确说明
**Documentation Impact:** 已更新 spec、ARCHITECTURE_OVERVIEW、CURRENT_FEATURES

---

## What Worked ✅

1. **测试先行收口边界**
   - What: 先补 Bin 假阳性、Runtime 缺失必需资源、真实 scaffold 启动链路测试
   - Why it worked: 失败点非常集中，直接锁定实现需要动的两个入口
   - Reusable pattern: Yes

2. **用真实仓库 scaffold 做 Editor 集成校验**
   - What: 直接调用 `EditorBootstrapService` 验证仓库 `game/` 当前状态
   - Impact: 把“仓库默认不提交 `.terrain` / `biome_mask.png`”这个约束正式测住

---

## What Didn't Work ❌

1. **把“最近的 `game/`”当成唯一规则**
   - What we tried: 早期定位逻辑只靠最近的 `map_data/` / `game/`
   - Why it failed: 这会忽略工作区优先的约束，也会让文档和实现难以对齐
   - Lesson learned: 必须同时写清“工作区优先”和“直接命中的完整 `game/` 根可接受”
   - Don't try this again because: 单一一句“最近命中”无法准确描述当前行为

---

## Architecture Impact

### Documentation Updates Required
- [x] Update [2026-06-13-editor-virtual-resource-system-design.md](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md) - 收紧资源根与 Editor/Runtime 必需资源口径
- [x] Update ARCHITECTURE_OVERVIEW.md - 记录工作区 `game/` 根与 Runtime error 行为
- [x] Update CURRENT_FEATURES.md - 标注 Save / Runtime 当前边界

### Architectural Decisions That Changed
- **Changed:** 资源根定位约束与 `.terrain` / `biome_mask.png` 必需性定义
- **From:** “最近的资源目录”与“Editor/Runtime 基本同口径”
- **To:** “工作区 `game/` 优先，直接命中的完整 `game/` 根仍可接受”与“Editor 容错、Runtime 严格”
- **Scope:** `Terrain`, `Terrain.Editor`, 测试与设计文档
- **Reason:** 与真实 repo 资源布局及当前工作流对齐

---

## Code Quality Notes

### Testing
- **Tests Written:** 5 个关键回归点
- **Coverage:** 资源根定位、Runtime 缺失关键资源、Runtime 错误日志级别、仓库 scaffold 文件完整性、仓库 scaffold 的 Editor 启动链路
- **Manual Tests:** 未执行 Editor 手工冒烟

### Technical Debt
- **Created:** 无新增实现债务
- **Paid Down:** 文档口径与实现脱节、根定位过宽、Runtime 错误日志级别过低
- **TODOs:** 后续若支持打包部署，需要扩展 `GameResourceRootLocator` 策略

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 做一次真实 Editor 冒烟启动，确认控制台提示与加载体验
2. 如开始支持 rivers 作者态编辑，再补 `rivers.png` 写回链路
3. 若进入发布/打包场景，设计非工作区环境下的资源根定位策略

### Blocked Items
- 无

### Questions to Resolve
1. 未来是否需要支持“工作区外部署目录”的 `game/` 根定位
2. `biome_mask.png` 已存在但内容无效时，Editor 是否应升级为显式错误提示

### Docs to Read Before Next Session
- [2026-06-13-editor-virtual-resource-system-design.md](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md) - 当前资源系统主口径
- [adr-015-workspace-game-root-and-runtime-requirements.md](../../../decisions/adr-015-workspace-game-root-and-runtime-requirements.md) - 本次边界收口决策

---

## Session Statistics

**Files Changed:** 10
**Lines Added/Removed:** 以本次 diff 为准
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain/Resources/GameResourceRootLocator.cs`, `Terrain/Core/TerrainProcessor.cs`
- Critical decision: 工作区 `game/` 是默认优先根，但直接命中的完整 `game/` 根仍可接受；Editor/Runtime 对 `.terrain` / `biome_mask.png` 的必需性不同
- Active pattern: 先用真实 scaffold + 关键行为测试钉住，再收口实现与文档
- Current status: 自动测试通过，手工 Editor 冒烟未做

**What Changed Since Last Doc Read:**
- Architecture: 根定位文档已改为“工作区优先 + 直接命中的完整 `game/` 根可接受”
- Implementation: Runtime bootstrap 失败现在记 error
- Constraints: `Save` 仍不写回 `rivers.png` 与材质贴图文件

**Gotchas for Next Session:**
- Watch out for: 不要把“最近的 `game/`”重新引回定位逻辑
- Don't forget: 仓库默认不提交 `.terrain` / `biome_mask.png`
- Remember: Runtime 缺失关键资源必须报错，Editor 则要继续可用

---

## Links & References

### Related Documentation
- [Editor / Runtime 共用虚拟资源系统设计](../../../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md)
- [ADR-015](../../../decisions/adr-015-workspace-game-root-and-runtime-requirements.md)

### Related Sessions
- [game-root-resource-bootstrap-and-scaffold.md](./game-root-resource-bootstrap-and-scaffold.md)

### Code References
- `Terrain/Resources/GameResourceRootLocator.cs`
- `Terrain/Core/TerrainProcessor.cs`
- `Terrain.Editor.Tests/VirtualResources/GameResourceRootLocatorTests.cs`
- `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs`
- `Terrain.Editor.Tests/VirtualResources/GameResourceScaffoldTextTests.cs`

---

## Notes & Observations

- 当前 repo `game/` scaffold 已足够支撑 Editor 作者态启动，但仍故意缺少 Runtime 消费所需的 `.terrain` / `biome_mask.png`
- 这次收口后，spec、ADR、自动测试、当前实现终于指向同一套行为

---

*Template Version: 1.0 - Based on Archon-Engine template*
