# 回归：修复缩略图后地形光影丢失

## Goal

修复编辑器中“纹理缩略图修复”之后出现的地形光影回归问题，恢复地形正常受光与阴影表现，同时不破坏缩略图功能。

## What I already know

* 用户反馈：自上次修复缩略图后，地形没有了光影。
* 最近相关提交为 `b495bad`（`fix: show texture thumbnails in editor panels`）。
* 该提交除了缩略图逻辑，还改动了若干地形 shader 生成文件和编辑器地形渲染代码。
* `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl` 中 `HeightmapSliceBounds*` 在 shader 里声明为 `int4`。
* `b495bad` 将 `EditorTerrainHeightParameters.sdsl.cs` 与相关调用改成了 `Vector4`，与 shader 原始声明不一致。

## Assumptions (temporary)

* 地形光影丢失是由编辑器地形 shader 参数类型错配导致的回归。
* 问题范围主要在编辑器地形渲染链路，不涉及运行时 `Terrain/` 项目行为。

## Open Questions

* 修复 `HeightmapSliceBounds` 类型错配后，是否还存在其他由自动生成 shader key 文件带来的参数绑定问题。

## Requirements (evolving)

* 恢复编辑器地形的正常光照/阴影表现。
* 保持地形高度、材质索引和切片采样逻辑与 shader 声明一致。
* 不回退或破坏缩略图修复本身。

## Acceptance Criteria (evolving)

* [ ] 编辑器地形渲染参数类型与 `.sdsl` 声明一致，不再存在 `int4`/`Vector4` 错配。
* [ ] 项目能成功编译至少编辑器相关代码路径。
* [ ] 修复后未引入新的明显地形渲染错误。

## Definition of Done (team quality bar)

* 编译检查通过
* 仅修改与本次回归直接相关的文件
* 如有必要，记录本次回归的经验到 spec

## Out of Scope (explicit)

* 重构整个地形材质系统
* 调整缩略图 UI/缓存实现
* 修改运行时导出地形渲染表现

## Technical Notes

* 重点排查提交：`b495bad`
* 重点文件：
  * `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl`
  * `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl.cs`
  * `Terrain.Editor/Rendering/EditorTerrainProcessor.cs`
  * `Terrain.Editor/Rendering/EditorTerrainSplatMapComputeDispatcher.cs`
