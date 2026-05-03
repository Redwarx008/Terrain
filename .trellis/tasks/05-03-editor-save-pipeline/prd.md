# brainstorm: 修复 Editor 保存链路

## Goal

恢复 Terrain Editor 的项目保存、重开恢复、运行时导出整条持久化链路，确保编辑器中的关键地形数据修改能够被正确落盘，并在重新打开项目或导出运行时资源时按预期保留。

## What I already know

* 保存入口位于 `Terrain.Editor/ViewModels/EditorShellViewModel.cs` 的 `SaveProject()` / `SaveProjectAs()`。
* `SaveProjectAs()` 在未打开项目时会先调用 `ProjectManager.CreateProject(...)`，随后调用 `TerrainManager.SaveProject()` 写入实际内容。
* `TerrainManager.SaveProject()` 当前会写入 TOML 配置，并保存 `terrain_biome_mask.png`，还会序列化材质槽位、Biome 定义、Layer、Modifier。
* `TerrainManager.SaveProject()` 当前未发现任何把 `heightDataCache` / `EditorTerrainEntity.HeightDataCache` 写回高度图 PNG 的路径。
* 高度编辑链路位于 `Terrain.Editor/Services/HeightEditor.cs` 与 `Terrain.Editor/Services/Commands/HeightEditCommand.cs`，当前只会修改内存中的高度缓存并标记 GPU 脏区。
* `Terrain.Editor/Services/ProjectManager.cs` 暴露了 `HeightmapsPath`，但当前检索结果中未发现被实际用于保存高度图。
* `TerrainExporter` 直接从内存中的 `TerrainManager.HeightDataCache` 与 `MaterialIndices` 导出 `.terrain`，并不依赖项目保存后的磁盘副本。
* 仓库设计文档 `easysdd/architecture/project-persistence.md` 明确写了 `SaveProjectAs(path) → 复制资源 + 生成新 .toml`，但当前 `ProjectManager.SaveProjectAs()` 只把 `cachedConfig` 写到新路径，没有复制资源。
* 旧实现日志 `docs/log/2026/04/08/2026-04-08-2-toml-project-persistence.md` 记录过“Save → 写入 .toml + indexmap PNG”，说明这条链路之前就以“保存副文件资源”为目标设计过。

## Assumptions (temporary)

* “整条链路都有问题，包括导出”意味着问题不只在 Save 按钮本身，而是项目状态的磁盘真源定义已经失真。
* 最高风险断点是高度数据持久化缺失，以及 `SaveProjectAs` 没有生成自包含项目资源快照。
* 导出链路本身可能在“当前会话内”能导出内存状态，但与“保存后再打开再导出”的链路未对齐。

## Open Questions

* 项目保存的真源策略要收敛到哪一种？

## Requirements (evolving)

* 明确保存链路当前覆盖的数据范围：高度、BiomeMask、材质槽位、Biome 规则配置，以及 `.terrain` / `material_descriptor.toml` 导出依赖的输入数据。
* 找出保存后重开无法恢复的数据通道与具体断点。
* 找出导出链路与项目持久化链路之间不一致的数据真源。
* 收敛本次修复的持久化策略，避免继续出现“内存里一套、磁盘上一套、导出再一套”的状态。

## Decision (ADR-lite)

**Context**: 当前 `Save` / `Save As` / `Export` 对磁盘真源的定义不一致，导致保存后重开与导出之间状态漂移。

**Decision**: 采用混合策略。

* `Save` 保持当前项目中的外部资源引用语义，不强制把所有外部资源复制进项目目录。
* `Save` 必须确保当前项目引用的可编辑关键资源已经被更新到磁盘，至少包括高度图快照与 BiomeMask。
* `Save As` 必须生成新的项目快照，把当前编辑状态写入新项目目录中的资源文件，并让新 `.toml` 指向这些项目内资源。
* 导出链路必须建立在与保存/重开一致的有效数据源之上，避免“当前会话可导出，但保存重开后回退”的漂移。

**Consequences**:

* 相比全量项目快照，`Save` 行为更贴近当前用户习惯，但仍需要补齐关键资源写回。
* `Save As` 复杂度会上升，因为需要显式生成新项目资源副本并更新配置引用。
* 后续需要特别注意“当前项目引用的是外部源还是项目内快照”这一边界，避免再次出现路径和真实内容脱节。

## Acceptance Criteria (evolving)

* [ ] 能稳定复现当前保存故障，并确认受影响的数据通道。
* [ ] 确认修复后，受影响的数据在保存并重开项目后能够恢复。
* [ ] 确认修复后，导出链路基于与项目保存一致的有效数据源，不会因保存/重开而回退到旧状态。
* [ ] 保存链路与 dirty 状态行为一致，不会出现“已修改但无法保存”或“保存成功但数据未落盘”的情况。
* [ ] `Save As` 后生成的新项目不依赖旧项目目录中的高度图/蒙版快照也能独立重开。

## Definition of Done (team quality bar)

* Tests added/updated (unit/integration where appropriate)
* Lint / typecheck / CI green
* Docs/notes updated if behavior changes
* Rollout/rollback considered if risky

## Out of Scope (explicit)

* 与当前持久化断裂无关的 `.terrain` 二进制格式升级
* 与保存/导出链路无关的渲染或着色器问题

## Technical Notes

* 重点入口：
  * `Terrain.Editor/ViewModels/EditorShellViewModel.cs`
  * `Terrain.Editor/Services/ProjectManager.cs`
  * `Terrain.Editor/Services/TerrainManager.cs`
  * `Terrain.Editor/Services/HeightEditor.cs`
  * `Terrain.Editor/Services/Commands/HeightEditCommand.cs`
  * `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`
  * `Terrain.Editor/Services/Export/Exporters/MaterialDescriptorExporter.cs`
  * `Terrain.Editor/Services/TomlProjectConfig.cs`
* 相关设计/历史文档：
  * `easysdd/architecture/project-persistence.md`
  * `easysdd/architecture/export.md`
  * `docs/log/2026/04/08/2026-04-08-2-toml-project-persistence.md`
* 最近相关提交集中在 biome / editor 工作流调整：
  * `39f2046` `Fix material slot previews in biome sidebar`
  * `bf99da6` `fix(editor): stabilize biome painting and merge biome mode`
  * `7e4016a` `fix biome rule layer regressions`
  * `f1b6323` `feat: migrate climate paint flow to biome rule layers`
