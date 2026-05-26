## Why

Biome 笔刷（Paint 模式）绘制后按 Ctrl+Z 没有任何反应——undo/redo 系统完全没有接入 BiomeEditor。高度编辑（Sculpt 模式）的 undo/redo 工作正常，但 biome 绘制在迁移到规则驱动体系时遗漏了 HistoryManager 集成。用户每次误刷只能手动用其他 biome ID 覆盖，无法撤销。

## What Changes

- **新增 `BiomeEditCommand`** — 继承 `TerrainEditCommand`，对 `BiomeMask` 的 `byte[]` 数据做 chunk 级 before/after 快照
- **修改 `BiomeEditor`** — 添加 `BeginStroke`/`EndStroke` 三阶段生命周期，集成 `HistoryManager`
- **修改 `TerrainDataChannel`** — 添加 `Biome` 枚举成员
- **修改 `EmbeddedStrideViewportGame`** — 在 Paint 模式的笔触生命周期中接入 `BiomeEditor.BeginStroke`/`EndStroke`

## Capabilities

### New Capabilities

- `biome-undo-redo`: Biome 笔刷操作的撤销/重做，基于 chunk 快照的事务模型，与现有 Height undo/redo 共用 HistoryManager

### Modified Capabilities

<!-- 无现有 spec 需要修改 -->

## Impact

- `Terrain.Editor/Services/Commands/` — 新增 `BiomeEditCommand.cs`
- `Terrain.Editor/Services/BiomeEditor.cs` — 重构为三阶段生命周期
- `Terrain.Editor/Rendering/EditorTerrainEntity.cs` — `TerrainDataChannel` 枚举加 `Biome`
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs` — Paint 模式 stroke 生命周期接入
