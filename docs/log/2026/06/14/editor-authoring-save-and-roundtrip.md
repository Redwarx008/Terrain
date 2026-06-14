# Editor Authoring Save And Roundtrip
**Date**: 2026-06-14
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续虚拟资源系统计划，补齐 Editor 作者态 `Save` 链路，让当前虚拟资源会话背后的实体资源可以被稳定写回。

**Secondary Objectives:**
- 补全 biome settings advanced modifier 字段的 round-trip。
- 修正 biome 作者态修改后的 dirty 触发。
- 回填 superpower 计划与系统文档，避免状态漂移。

**Success Criteria:**
- `Save` 直接写回当前命中的 `default.toml`、`heightmap.png`、`biome_mask.png`、`biome_settings.toml`、`materials/descriptor.toml`。
- `.terrain` 继续只通过 Export Terrain 输出，不被 Save 隐式重建。
- advanced biome modifier 字段保存后不丢失。
- 自动化验证通过。

---

## Context & Background

**Previous Work:**
- Related: [editor-virtual-resource-bootstrap-and-writers.md](./editor-virtual-resource-bootstrap-and-writers.md)
- Related: [editor-authoring-resource-session-followup.md](./editor-authoring-resource-session-followup.md)
- Related: [2026-06-13-virtual-resource-system.md](../../../superpowers/plans/2026-06-13-virtual-resource-system.md)

**Current State:**
- Editor 已经改成启动即按 `LaunchSetting.json` 构建虚拟资源会话。
- Runtime 已切到统一 resolver/bootstrap。
- 但上个会话结束时，明确的 Save 作者态写回动作还没有接完。

**Why Now:**
- 用户明确要求“之前的 plan 还没有做完”，而 Save/round-trip 正是剩余主缺口。

---

## What We Did

### 1. 接上真正的 Save 作者态写回链路
**Files Changed:** `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`, `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Views/MainWindow.axaml`

**Implementation:**
- 新增 `MapDefinitionWriter`，把当前 `RuntimeMapDefinition` 回写到 `session.MapDefinition.ResolvedPath`。
- 在 `TerrainManager.SaveAuthoringResources()` 中统一调度：
  - `default.toml`
  - `heightmap.png`
  - `biome_mask.png`
  - `biome_settings.toml`
  - `materials/descriptor.toml`
- `EditorShellViewModel` 新增 `SaveCommand`，绑定 `Ctrl+S` 和菜单 `Save`。
- 保存成功后重新加载当前 `EditorResourceSession`，保证内存中的 map definition 与写回内容一致。

**Rationale:**
- “最终加载哪个，就写回哪个”需要一个明确的保存入口，而不是只有零散 writer 存在。

### 2. 补齐 biome settings 的 advanced modifier round-trip
**Files Changed:** `Terrain.Editor/Services/Resources/BiomeSettingsWriter.cs`, `Terrain.Editor/Services/Resources/EditorAuthoringResourceMapper.cs`, `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`, `Terrain.Editor.Tests/VirtualResources/EditorAuthoringResourceMapperTests.cs`

**Implementation:**
- `EditorBiomeModifierDefinition` 扩展保存：
  - `radius`
  - `angle_degrees`
  - `angle_range_degrees`
  - `scale`
  - `offset_x`
  - `offset_y`
  - `seed`
  - `octaves`
  - `invert`
  - `texture_mask`
  - `texture_mask_channel`
- `EditorAuthoringResourceMapper.CreateBiomeSettingsSnapshot()` 同步从当前 `BiomeModifier` 提取这些字段。
- 新增测试确认 advanced 字段在保存链路中不会丢失。

**Rationale:**
- 读取器早已支持这些字段；如果写回器缺字段，会导致用户 Save 一次就静默降级配置。

### 3. 修正 biome 作者态修改后的 dirty 触发
**Files Changed:** `Terrain.Editor/Services/BiomeRuleService.cs`

**Implementation:**
- 在公开的 biome/layer/modifier 变更入口统一补上 `EditorDirtyState.Instance.MarkDirty()`。
- 保持加载链路 `ApplyRuntimeSettings()` 不标脏，避免启动即 dirty。

**Rationale:**
- Save 入口接好之后，dirty 状态必须能准确反映“是否有作者态变更待保存”。

### 4. 回填计划与系统文档
**Files Changed:** `docs/superpowers/plans/2026-06-13-virtual-resource-system.md`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- 将虚拟资源 superpower 计划所有实施/验证步骤回填为已完成。
- 对所有“提交”步骤补注：按用户要求未执行提交。
- 架构总览和功能清单明确写入：
  - Save 写回作者态资源
  - `.terrain` 仍走 Export Terrain
  - `rivers.png` 当前仅可选读取

---

## Decisions Made

### Decision 1: Save 负责作者态资源，不负责 `.terrain`
**Context:** 用户之前已明确 `.terrain` 主要存放二进制 VT 数据，Editor 可导出 `.terrain`，但不应把它混入普通 Save。
**Decision:** `Save` 只写 `default.toml` + companion 作者态资源；`.terrain` 继续由 Export Terrain 写回当前命中的目标。
**Rationale:** 这样更符合 CK3 风格作者态资源与运行时二进制分层，也避免 Save 做重型导出。

### Decision 2: round-trip 必须保留 advanced modifier 字段
**Context:** 读取器与 Runtime 已消费这些字段，丢失写回会带来无提示的行为退化。
**Decision:** 补齐 writer DTO 与 mapper，把 advanced 字段纳入正式保存链路。
**Rationale:** 避免“能读不能写”的单向配置格式。

---

## Problems Encountered & Solutions

### Problem 1: 只有 writer，没有统一 Save 编排
**Symptom:** 上个会话虽然有 heightmap / biome mask / descriptor / settings writer，但没有单点保存动作。
**Root Cause:** 作者态资源模型先落地，最后一公里的保存编排尚未接到 ViewModel 命令。
**Solution:**
- 新增 `MapDefinitionWriter`
- 在 `TerrainManager.SaveAuthoringResources()` 中集中写回
- 在 `EditorShellViewModel.Save()` 中接入 UI 命令

**Why This Works:** 保存语义从“散落的工具类”升级成“编辑器显式工作流”。

### Problem 2: Save 会丢 advanced biome modifier 参数
**Symptom:** 读入后再保存，部分 modifier 参数会丢失。
**Root Cause:** `BiomeSettingsWriter` DTO 不完整，`EditorAuthoringResourceMapper` 也未提取全部字段。
**Solution:**
- 扩展 DTO
- 扩展 mapper
- 增加文本/写回测试覆盖

**Why This Works:** 读写模型重新对齐，避免单向能力。

### Problem 3: biome 结构修改不一定触发 dirty
**Symptom:** 某些 biome/layer/modifier 操作后，编辑器未必进入 dirty 状态。
**Root Cause:** 旧逻辑更关注运行时应用和 UI 通知，没有把所有作者态变更都统一标脏。
**Solution:**
- 在公开的 mutation API 上统一补 `MarkDirty()`

**Why This Works:** dirty 触发从“调用者自觉”变成“服务层保证”。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 明确 Save / Export / optional rivers 的当前语义
- [x] Update `docs/CURRENT_FEATURES.md` - 增加 Save 作者态资源完成状态

### Architectural Decisions That Changed
- **Changed:** Editor 作者态保存链路
- **From:** 虚拟资源 writer 已存在，但没有完整 Save 工作流
- **To:** `EditorShellViewModel.SaveCommand` -> `TerrainManager.SaveAuthoringResources()` -> 各 writer 写回当前命中的实体文件
- **Scope:** Editor 资源保存、dirty 状态、round-trip 完整性
- **Reason:** 补齐虚拟资源系统计划的最后主缺口

---

## Code Quality Notes

### Testing
- **Tests Written:** 新增 Save 工作流文本测试、`default.toml` writer 测试、advanced biome modifier round-trip 测试
- **Coverage:** Save 命令绑定、map definition 写回、biome settings advanced 字段写回、mapper snapshot 保真
- **Manual Tests:** 未执行手工 Editor 冒烟验证

### Technical Debt
- **Paid Down:** Save 最后一公里缺口、advanced modifier round-trip 缺口、biome dirty 触发缺口
- **Remaining:** `provinces` 仍未实现链路；`rivers.png` 当前仅读取不写回

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 如果要继续收尾体验层，可做一次真正的 Editor 冒烟验证，确认启动即加载和 Save/Export 行为符合预期。
2. 若后续实现 rivers/provinces authoring，先定义编辑语义，再决定是否新增 writer。
3. 如需减少误用，可继续清理与旧计划/旧 spec 不一致的历史描述文件。

### Docs to Read Before Next Session
- [2026-06-13-virtual-resource-system.md](../../../superpowers/plans/2026-06-13-virtual-resource-system.md)
- [ARCHITECTURE_OVERVIEW.md](../../../ARCHITECTURE_OVERVIEW.md)
- [CURRENT_FEATURES.md](../../../CURRENT_FEATURES.md)

---

## Session Statistics

**Files Changed:** 约 11 个代码/测试文件 + 3 个文档文件
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `Save` 已经是真正的作者态保存入口，不再只是计划项。
- `.terrain` 仍然只通过 Export Terrain 写回当前命中的目标。
- `BiomeSettingsWriter` 现在必须保留 advanced modifier 字段，不能再回退到简化模型。
- `BiomeRuleService` 的公开 mutation API 已负责标脏；加载链路不标脏。

**What Changed Since Last Doc Read:**
- Architecture: Editor 作者态保存链路正式闭环
- Implementation: 新增 `MapDefinitionWriter` 与 `SaveCommand`
- Constraints: 手工 Editor 冒烟还没做，不要把它说成已验证

**Gotchas for Next Session:**
- 不要恢复旧 `ProjectManager` / `TomlProjectConfig` / Open/New/SaveAs 工作流。
- 不要让 Save 隐式生成 `.terrain`。
- 不要因为 `rivers.png` / `provinces.png` 出现在 `default.toml` 里，就默认它们必须有 writer。

---

*Template Version: 1.0 - Based on Archon-Engine template*
