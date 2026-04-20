---
doc_type: architecture
slug: editor-services
scope: 编辑器服务层：状态管理、高度/材质/气候编辑、项目持久化
summary: TerrainManager 是中心编排器，HeightEditor/PaintEditor/ClimateEditor 共享笔触生命周期，MarkDataDirty 统一 GPU 同步
status: current
last_reviewed: 2026-04-20
tags: [editor, services, state-management, data-sync]
depends_on: [brush-commands, climate-material, project-persistence]
---

## 1. 定位与受众

本文档描述编辑器核心服务层。读者是修改编辑器功能、添加新笔刷、或扩展数据同步逻辑的人。

## 2. 结构与交互

```
EditorState (singleton)
    ↓ 模式切换
TerrainManager (中心编排器)
    ├─ HeightEditor (高度雕刻)
    ├─ PaintEditor (材质绘制)
    ├─ ClimateEditor (气候蒙版笔刷)
    ├─ ClimateRuleService (规则栈管理, singleton)
    ├─ MaterialSlotManager (256 槽位, singleton)
    ├─ ProjectManager (项目生命周期, singleton)
    └─ HistoryManager (Undo/Redo)
```

### 统一数据同步

所有编辑操作通过 `MarkDataDirty(channel)` 驱动 GPU 同步：

```
HeightEditor.ApplyStroke()  → MarkDataDirty(Height)
PaintEditor.ApplyStroke()   → MarkDataDirty(MaterialIndex)
ClimateEditor.ApplyStroke() → MarkDataDirty(ClimateMask)
                                ↓
EditorTerrainEntity.OnDataDirty() → SyncDataToGpu (增量上传)
```

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| EditorState | singleton | 全局 | 内存 |
| HeightDataCache | ushort[] | TerrainManager | 内存 + .terrain 导出 |
| MaterialIndexMap | class | TerrainManager | 内存 + GPU Texture |
| ClimateMask | class | TerrainManager | 内存 + GPU Texture |
| BrushParameters | singleton | 全局 | 内存 |

## 4. 关键决策

- **统一 MarkDataDirty 接口** → `editor-services.md` 数据同步节
- **Chunk 事务 Undo/Redo** → `2026-04-20-decision-chunk-transaction-undo.md`
- **TOML 项目持久化** → `2026-04-20-decision-toml-project-persistence.md`

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| TerrainManager | `Terrain.Editor/Services/TerrainManager.cs:22` | 中心状态管理 |
| MarkDataDirty | `Terrain.Editor/Services/TerrainManager.cs:293` | 统一数据同步入口 |
| LoadTerrainAsync | `Terrain.Editor/Services/TerrainManager.cs:109` | 高度图加载 |
| RegenerateMaterialIndices | `Terrain.Editor/Services/TerrainManager.cs:400` | 重新生成材质索引 |
| HeightEditor | `Terrain.Editor/Services/HeightEditor.cs:17` | 高度雕刻服务 |
| BeginStroke | `Terrain.Editor/Services/HeightEditor.cs:38` | 笔触开始 |
| ApplyStroke | `Terrain.Editor/Services/HeightEditor.cs:70` | 笔触应用 |
| EndStroke | `Terrain.Editor/Services/HeightEditor.cs:117` | 笔触结束 |
| PaintEditor | `Terrain.Editor/Services/PaintEditor.cs:16` | 材质绘制服务 |
| ClimateRuleService | `Terrain.Editor/Services/ClimateRuleService.cs:34` | 规则栈 singleton |
| MaterialSlotManager | `Terrain.Editor/Services/MaterialSlotManager.cs:17` | 材质槽位 singleton |
| ProjectManager | `Terrain.Editor/Services/ProjectManager.cs:11` | 项目管理 singleton |
| EditorState | `Terrain.Editor/Services/EditorState.cs:43` | 编辑器状态 singleton |

## 6. 已知约束 / 边界情况

- EditorState 是全局 singleton，不支持多实例
- TerrainManager.LoadTerrainAsync 是唯一初始化入口，不支持热重载
- MaterialSlotManager 纹理数组重建（RebuildMaterialArrays）开销大，只在槽位变化时触发

## 7. 相关文档

- [brush-commands.md](brush-commands.md) — 笔触生命周期和 Undo/Redo
- [climate-material.md](climate-material.md) — 气候蒙版和材质系统
- [project-persistence.md](project-persistence.md) — TOML 配置