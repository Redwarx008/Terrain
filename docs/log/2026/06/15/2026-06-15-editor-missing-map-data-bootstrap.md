# Editor Missing Map Data Bootstrap
**Date**: 2026-06-15
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 完成 Editor 作者态对缺失 `map_data` 关键资源的自举与 pending heightmap 启动链路。

**Secondary Objectives:**
- 记录 `default.toml`、`materials/descriptor.toml`、`biome_settings.toml` 的当前实现规格。
- 确保 `Save` / `ExportTerrain` 在 pending 模式下被显式拦截。

**Success Criteria:**
- 缺失三个关键 TOML 时自动补齐。
- 缺失 `heightmap.png` 时继续启动，但不允许保存或导出。
- 测试与文档同步更新。

---

## Context & Background

**Previous Work:**
- Related: [map-data bootstrap design](../../../superpowers/specs/2026-06-15-editor-missing-map-data-bootstrap-design.md)

**Current State:**
- Editor/Runtime 已切换到基于 `game/` 工作区与 `LaunchSetting.json` 的资源解析。
- 用户要求把 `map_data` 的文本规格补全文档，并在作者态缺失关键文件时自动补齐。

**Why Now:**
- 当前作者态冷启动对缺失资源过于脆弱，阻断了 SVN 管理工作区下的新工程搭建与恢复流程。

---

## What We Did

### 1. 自动补齐缺失的作者态 TOML
**Files Changed:** `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`, `Terrain.Editor.Tests/VirtualResources/EditorMapDataScaffoldTests.cs`

**Implementation:**
- 新增 `EditorMapDataScaffoldService`。
- 缺失时自动生成：
  - `map_data/default.toml`
  - `map_data/materials/descriptor.toml`
  - `map_data/biome_settings.toml`
- 明确不生成 `heightmap.png`。

**Rationale:**
- TOML 有稳定 writer，可无损生成最小合法骨架；`heightmap.png` 属于真实作者资源，不能伪造。

### 2. Bootstrap 标记 pending heightmap
**Files Changed:** `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`, `Terrain.Editor/Services/Resources/EditorResourceSession.cs`, `Terrain.Editor.Tests/VirtualResources/LocalLaunchSettingsBootstrapTests.cs`

**Implementation:**
- Bootstrap 先补齐 TOML，再解析当前会话。
- `heightmap` 改为 writable target；文件缺失时把 session 标记为 pending。
- session 暴露 `HasPendingHeightmap`、`HasPendingResources`、`PendingHeightmapPath`、`CanSaveAuthoringResources`、`CanExportTerrainData`。

**Rationale:**
- 作者态需要在“资源目录已建立但核心高度图尚未到位”时继续进入工作区。

### 3. TerrainManager 区分 pending 与普通失败
**Files Changed:** `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor.Tests/VirtualResources/EditorPendingResourceWorkflowTests.cs`

**Implementation:**
- pending 分支下仍会应用材质 descriptor 与 biome settings。
- pending 时清理旧 terrain、保留必要 river/material side effects，然后返回空 terrain 列表。
- 普通 terrain load 失败会清理陈旧 terrain / river 状态，避免残留。

**Rationale:**
- pending 模式是“待补作者资源”，不是“读取失败”；两者必须分开建模。

### 4. Shell 保留 pending session 并阻止保存/导出
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor.Tests/VirtualResources/EditorPendingResourceWorkflowTests.cs`

**Implementation:**
- `LoadEditorResourceSessionAsync()` 在 pending 时保留 `_resourceSession`，刷新状态并输出 error/warning。
- 普通 `entities.Count == 0` 失败路径不再伪装成已加载 session。
- `Save()` 与 `ExportTerrain()` 在 pending 时直接 warning + return。
- 测试补强到方法体/分支级别，锁住普通失败、pending 分支、以及 save/export 副作用门禁顺序。

**Rationale:**
- pending 模式允许用户继续编辑配置性资源，但不能落地作者态 heightmap 相关写回。

### 5. 更新规格与状态文档
**Files Changed:** `docs/design/map-data-toml-formats.md`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- 新增 `map_data` 三份 TOML 的实现规格文档。
- 更新架构总览与功能清单，记录：
  - 自动骨架补齐
  - pending heightmap 启动
  - save/export 门禁

---

## Decisions Made

### Decision 1: 缺失 heightmap 进入 pending，而不是自动生成图片
**Context:** 用户要求作者态链路在缺失关键路径文件时尽量自恢复，但明确不生成 `heightmap.png`。
**Options Considered:**
1. 自动生成空白 heightmap - 启动最平滑，但会伪造真实作者资源。
2. 缺失时直接失败 - 语义简单，但无法满足作者态冷启动诉求。
3. 标记 pending 并继续启动 - 保留真实语义，同时让用户补资源。

**Decision:** Chose Option 3
**Rationale:** 既满足可启动性，也不污染真实地图数据。
**Trade-offs:** Shell、TerrainManager、测试都要额外建模 pending 语义。
**Documentation Impact:** 已更新 `docs/design/map-data-toml-formats.md`、`docs/ARCHITECTURE_OVERVIEW.md`、`docs/CURRENT_FEATURES.md`

### Decision 2: Save / Export 在命令入口硬拦截
**Context:** pending 模式下即使 UI 状态正确，也需要拦住快捷键或命令路径。
**Options Considered:**
1. 仅靠 UI 禁用
2. 仅靠底层 writer/exporter 失败
3. 在 ViewModel 命令入口显式 gate

**Decision:** Chose Option 3
**Rationale:** 命令入口是最可靠的统一防线。
**Trade-offs:** 后续若补 UI disable，还需要双层维护，但正确性更强。

---

## Problems Encountered & Solutions

### Problem 1: Shell 把普通加载失败误保留成已加载 session
**Symptom:** 初版 Task 4 在 `_resourceSession = session;` 与 UI 刷新后才判断 `entities.Count == 0`。
**Root Cause:** 为支持 pending 分支保留 session 时，把普通失败路径也一起放宽了。
**Solution:**
- 只在 `session.HasPendingHeightmap` 或真正成功加载时保留 session。
- 普通失败分支仅输出错误并返回。

**Why This Works:** pending 与普通失败的行为边界重新对齐需求。

### Problem 2: Shell 测试初版只做全文件字符串存在性断言
**Symptom:** review 指出测试无法真正锁住“普通失败不保留 session”和“pending gate 早于副作用”。
**Root Cause:** 测试粒度过粗，只验证字符串存在，不验证所在分支与顺序。
**Solution:**
- 升级为方法体/分支块级别断言。
- 单独检查 pending 分支、failed branch、Save gate、Export gate。

**Why This Works:** 测试直接绑定到控制流边界，而不是文件级偶然文本。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/design/map-data-toml-formats.md`
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`

### Architectural Decisions That Changed
- **Changed:** Editor 作者态冷启动对缺失地图资源的处理方式
- **From:** 缺失关键资源时容易直接失败
- **To:** 缺失 TOML 自动补齐；缺失 heightmap 时进入 pending 模式
- **Scope:** `Terrain.Editor` bootstrap、session、manager、shell、tests、docs
- **Reason:** 适配 SVN 管理的工作区和更稳健的冷启动体验

---

## Code Quality Notes

### Testing
- **Tests Written:** 新增/补强 bootstrap、scaffold、pending workflow、shell gate 相关测试
- **Coverage:** TOML 骨架生成、pending session、TerrainManager 控制流、Shell save/export 门禁
- **Manual Tests:** 未执行

### Technical Debt
- **Created:** 无新的功能性 debt
- **TODOs:** 后续可补一层更接近 VM 行为级的测试，以及 UI 层禁用态

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 决定该分支是直接本地合并、推 PR，还是暂存保留
2. 如继续强化，可增加更接近行为级的 shell 测试
3. 如做 UX 打磨，可为 pending 模式补 `Save` / `Export` 禁用态

### Blocked Items
- 无

### Docs to Read Before Next Session
- [map-data bootstrap design](../../../superpowers/specs/2026-06-15-editor-missing-map-data-bootstrap-design.md)
- [map_data TOML 规格](../../design/map-data-toml-formats.md)

---

## Session Statistics

**Files Changed:** 10+
**Commits:** 9

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 关键实现：`EditorBootstrapService` 负责 scaffold + pending session 建模
- 关键实现：`TerrainManager` 区分 pending 与普通 terrain load failure
- 关键实现：`EditorShellViewModel` 在 pending 下保留 session，但阻止 save/export
- 当前状态：实现完成，测试与总审查通过，等待决定如何整合分支

**What Changed Since Last Doc Read:**
- Architecture: 作者态冷启动支持 pending heightmap
- Implementation: 新增 `EditorMapDataScaffoldService` 与 shell pending gate
- Constraints: `heightmap.png` 仍必须人工补齐

**Gotchas for Next Session:**
- 不要把普通 `entities.Count == 0` 失败再次误归类为 pending
- 不要让 `Save` / `ExportTerrain` 的副作用跑到 pending gate 之前
- `docs/log/2026/06/15/` 下还有 review 过程文件，是否提交需要单独决定

---

## Notes & Observations

- 这次 review 循环的主要价值在于把“pending 语义”与“普通失败语义”重新收紧。
- 规格文档是必要的新文档，不应被误判为无意义过程产物。

---
