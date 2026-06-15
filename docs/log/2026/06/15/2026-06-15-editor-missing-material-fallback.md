# Editor 作者态缺失材质降级加载
**Date**: 2026-06-15
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 让 Editor 作者态在 `descriptor.toml` 缺失/为空、贴图缺失、或 `biome_settings.toml` 引用缺失 `material_id` 时继续启动，并对缺失项做逐槽降级。

**Secondary Objectives:**
- 对缺失 `material_id` 阻止 `Save` / `Export`
- 对缺失贴图仅降级对应槽位，并使用洋红色缺失材质 diffuse
- 把系统状态同步到架构文档

**Success Criteria:**
- TerrainManager 在作者态加载链路中接入材质恢复
- MaterialSlotManager 能为缺失材质/贴图生成运行时 fallback
- Shell 能打印降级错误并正确执行门禁
- 测试覆盖并通过

---

## Context & Background

**Previous Work:**
- Related: `docs/superpowers/specs/2026-06-15-editor-missing-material-fallback-design.md`
- Related: `docs/superpowers/plans/2026-06-15-editor-missing-material-fallback.md`

**Current State:**
- Task 1 已引入 `EditorMaterialLoadState`
- Task 2 已完成 `EditorMaterialRecoveryService`，并修正为通过虚拟资源解析贴图
- 本次继续完成 Task 3/4 的接线、fallback 纹理和 Shell 门禁

**Why Now:**
- 仅恢复服务存在但未接入加载链路时，删除 `descriptor.toml` 或缺失材质贴图仍会让作者态行为不完整

---

## What We Did

### 1. 补充红测并完成缺失材质恢复接线
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/MaterialSlotManagerFallbackTextTests.cs`, `Terrain.Editor.Tests/VirtualResources/EditorMissingMaterialWorkflowTests.cs`, `Terrain.Editor.Tests/Program.cs`, `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`

**Implementation:**
- 先新增两组文本测试，锁定：
  - `MaterialSlotManager` 必须提供洋红缺失材质 fallback 切片
  - `TerrainManager` 必须在读取 biome settings 后执行 material recovery
  - Shell 必须打印材质降级问题并按 session 能力门禁 `Save` / `Export`
- `TerrainManager.LoadFromResourceSession(...)` 现在：
  - 先读 `descriptor.toml`
  - 再无 known-id 硬门槛地读取 `biome_settings.toml`
  - 用 `EditorMaterialRecoveryService` + layered resolver 恢复最终槽位与诊断
  - 把恢复结果应用到 `MaterialSlotManager` 和 `BiomeRuleService`
- `EditorShellViewModel` 新增 `ReportMaterialLoadIssues(session)`，逐条打印缺失材质/贴图错误，并在存在问题时输出汇总 warning
- `Save` / `Export` 统一基于 `CanSaveAuthoringResources` / `CanExportTerrainData` 门禁；缺失高度图与缺失 `material_id` 分别给出不同提示

### 2. 扩展 MaterialSlot/MaterialSlotManager 的运行时 fallback 语义
**Files Changed:** `Terrain.Editor/Services/MaterialSlot.cs`, `Terrain.Editor/Services/MaterialSlotManager.cs`, `Terrain/Utilities/TextureBlockEncoder.cs`, `Terrain.Editor/Services/Resources/EditorMaterialRecoveryService.cs`, `Terrain.Editor/Services/Resources/EditorAuthoringResourceMapper.cs`

**Implementation:**
- `MaterialSlot` 新增：
  - `IsRuntimeFallbackPlaceholder`
  - `UsesFallbackAlbedo`
  - `UsesFallbackNormal`
- `IsEmpty` 改为基于材质身份/路径/placeholder 综合判断，确保缺失贴图和运行时占位槽仍能出现在 UI 与运行时数组里
- `MaterialSlotManager` 新增 `ApplyRecoveredMaterials(EditorMaterialRecoveryResult result)`，按恢复结果装配真实槽位和运行时槽位
- `TextureBlockEncoder` 新增 `CreateSolidColorMipData(...)`，用于生成纯色 mip 数据；缺失材质 diffuse 使用 `255, 0, 255, 255`
- `MaterialSlotManager` 在构建 albedo array 时，若槽位标记 `UsesFallbackAlbedo`，就注入洋红色 missing-material 纹理；法线继续复用 flat normal fallback
- `EditorAuthoringResourceMapper` 导出 descriptor 时跳过 `IsRuntimeFallbackPlaceholder`，防止运行时补位写回真实资源
- `EditorMaterialRecoveryService` 仍通过 layered resolver 判断贴图是否真实存在，但在缺失时会保留 descriptor 侧预期路径，避免作者态后续直接丢失该字段

### 3. 根据 subagent review 补齐真实行为缺口
**Files Changed:** `Terrain.Editor/Services/MaterialSlotManager.cs`, `Terrain.Editor/Services/Resources/EditorMaterialRecoveryService.cs`, `Terrain.Editor.Tests/VirtualResources/MaterialSlotManagerFallbackTextTests.cs`, `Terrain.Editor.Tests/VirtualResources/EditorMissingMaterialWorkflowTests.cs`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- 为 normal array 增加独立的 fallback signature 解析，保证“只有运行时占位槽、没有任何真实纹理”时仍会生成 flat normal fallback 布局
- 修正 `properties` 缺失诊断死分支，确保它进入 `MaterialLoadState`
- 缺失贴图日志改为绝对路径，便于用户直接补资源
- 将两份原本基于源码字符串的测试改成行为测试，直接验证：
  - runtime fallback 槽不会导出回 descriptor
  - 洋红 diffuse mip 数据正确
  - normal fallback signature 在全缺图场景下仍存在
  - `properties` 缺失进入非阻断诊断
  - 缺失贴图 issue 报告绝对路径

---

## Decisions Made

### Decision 1: 缺失贴图 fallback 继续走 MaterialSlotManager，而不是 shader 里分支
**Context:** 当前 shader 只在材质数组整体为空时回退全局 `DefaultDiffuseTexture`，无法表达“单槽缺失贴图”。
**Decision:** 在 `MaterialSlotManager` 构建 array texture 阶段为单槽注入 fallback 切片。
**Rationale:** 最小化 shader 改动，同时能逐槽生效，符合现有 CPU 侧材质数组构建责任边界。

### Decision 2: 缺失 `material_id` 继续启动，但保存/导出阻断
**Context:** 用户明确要求继续启动，同时不把运行时补位混入真实作者态资源。
**Decision:** TerrainManager 在作者态加载时恢复丢失材质映射；Shell 基于 session capability 阻止 `Save` / `Export`。
**Rationale:** 把“可诊断地继续工作”和“禁止固化错误作者态真相”同时满足。

---

## Problems Encountered & Solutions

### Problem 1: Task 2 的恢复服务虽然存在，但没有实际接入作者态加载链路
**Symptom:** `EditorMaterialRecoveryService` 只有测试覆盖，没有实际调用点。
**Root Cause:** 之前的提交只修正了恢复语义和 layered resolver 边界，没有完成 TerrainManager 接线。
**Solution:**
- 在 `TerrainManager.LoadFromResourceSession(...)` 中加入恢复调用
- 使用 `GameResourceResolverBootstrap.CreateForAppDirectory(AppContext.BaseDirectory)` 提供 layered resolver 回调

### Problem 2: 运行时占位槽位会被当成空槽，导致不可见且不会进入材质数组
**Symptom:** 旧 `MaterialSlot.IsEmpty` 只看 `AlbedoTexturePath`，placeholder 或缺失 albedo 的真实槽位都会被错判为空。
**Root Cause:** 旧实现默认“有 albedo 才算材质”，不适用于恢复模式。
**Solution:**
- 把 `IsEmpty` 改为基于 `MaterialId`/路径/placeholder 的综合判断
- 增加 runtime fallback 标志位

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`

### Architectural Decisions That Changed
- **Changed:** Editor 作者态启动链路现在包含材质恢复与降级状态传播
- **From:** descriptor/biome settings 直接进入 `BiomeRuleService`
- **To:** descriptor + biome settings 先经 `EditorMaterialRecoveryService` 恢复，再进入 `MaterialSlotManager` / `BiomeRuleService`
- **Scope:** Editor 作者态加载、材质数组构建、Shell 门禁

---

## Code Quality Notes

### Testing
- 新增测试：
  - `authoring mapper skips runtime fallback slots during export`
  - `material slot manager builds magenta missing-material fallback slices`
  - `material slot tracks runtime fallback flags`
  - `terrain manager recovers materials before applying biome settings`
  - `editor shell reports degraded materials after session load`
  - `editor shell blocks save and export on blocking material issues`
- 验证命令：
  - `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build`

### Technical Debt
- `TerrainManager` 目前使用 `AppContext.BaseDirectory` 重新构建 layered resolver；后续若需要更强可测试性，可考虑把 resolver 工厂下沉为可注入依赖

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 如果需要，把本轮作者态缺失材质行为补成更接近端到端的运行时/视口测试
2. 评估 `session.MaterialDescriptor` 命中 lower-priority 资源时的写回策略是否需要进一步梳理
3. 如果继续清理流程，可补 PR/分支级 review 记录

### Docs to Read Before Next Session
- `docs/superpowers/specs/2026-06-15-editor-missing-material-fallback-design.md`
- `docs/design/map-data-toml-formats.md`

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 材质恢复入口：`Terrain.Editor/Services/TerrainManager.cs`
- 缺失材质运行时槽位与洋红 fallback：`Terrain.Editor/Services/MaterialSlotManager.cs`
- 纯色 mip 编码：`Terrain/Utilities/TextureBlockEncoder.cs`
- Shell 汇报与门禁：`Terrain.Editor/ViewModels/EditorShellViewModel.cs`

**What Changed Since Last Doc Read:**
- Editor 作者态启动现在会恢复缺失 `material_id`
- 缺失贴图时不再整体验证失败，而是逐槽 fallback
- `Save` / `Export` 在缺失 `material_id` 时会被阻断

**Gotchas for Next Session:**
- 不要覆盖用户手动修改的 `docs/superpowers/specs/2026-06-15-editor-missing-material-fallback-design.md`
- `docs/log/2026/06/15/` 下已有多份未跟踪 review/plan 文件，除非用户要求，不要清理

---

## Verification

- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build`
