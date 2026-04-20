---
doc_type: architecture
slug: export
scope: 导出系统：.terrain 二进制 + material_descriptor.toml
summary: IExporter 接口 + ExportManager 注册/执行，TerrainExporter 流式分层导出 .terrain，MaterialDescriptorExporter 导出独立 TOML
status: current
last_reviewed: 2026-04-20
tags: [export, io, toml]
depends_on: [editor-services, project-persistence]
---

## 1. 定位与受众

本文档描述地形和材质导出系统。读者是添加新导出格式、修改 .terrain 文件结构、或调试导出性能时需要理解导出管线的人。

## 2. 结构与交互

```
IExporter (interface)
    ├─ TerrainExporter        → .terrain 二进制
    └─ MaterialDescriptorExporter → material_descriptor.toml

ExportManager (singleton)
    ├─ Register<T>() 注册导出器
    ├─ ExecuteAsync(name, path, progress) 执行导出
    └─ 错误回滚（失败时清理部分输出）
```

### TerrainExporter 导出流程

1. 写文件头（Magic, Version=3, 尺寸, TileSize, Padding, Mip 层数）
2. 写 MinMaxErrorMap 数组
3. 逐层 mip → 流式 + 分层并行计算 tiles → 顺序写入
4. HeightMap padding=2, SplatMap padding=1

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| IExporter | interface | ExportManager | 代码 |
| ExportProgress | struct | UI | 内存 |
| .terrain 文件 | binary | 文件系统 | 磁盘 |
| material_descriptor.toml | TOML | 文件系统 | 磁盘 |

## 4. 关键决策

- **独立 material_descriptor.toml 导出** → `2026-04-20-decision-material-descriptor-export.md`
- **流式 + 分层并行**：避免一次性分配整块内存，逐层 mipmap 计算

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| IExporter | `Terrain.Editor/Services/Export/IExporter.cs:10` | 导出器接口 |
| ExportManager | `Terrain.Editor/Services/Export/ExportManager.cs:12` | 注册/执行管理器 |
| ExportAsync | `Terrain.Editor/Services/Export/ExportManager.cs:28` | 异步导出入口 |
| TerrainExporter | `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs:15` | .terrain 导出器 |
| ExportAsync | `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs:31` | 导出主流程 |
| WriteTerrainFile | `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs:75` | 写二进制文件 |
| StreamMipLevels | `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs:149` | 流式 mip 层 |
| MaterialDescriptorExporter | `Terrain.Editor/Services/Export/Exporters/MaterialDescriptorExporter.cs:15` | TOML 导出器 |
| ExportProgressDialog | `Terrain.Editor/UI/Dialogs/ExportProgressDialog.cs` | 进度弹窗 |

## 6. 已知约束 / 边界情况

- .terrain 文件版本固定为 v3（SplatMap 1/2 分辨率）
- HeightMap padding=2, SplatMap padding=1，不可配置
- 导出失败时 ExportManager 尝试清理部分输出文件

## 7. 相关文档

- [project-persistence.md](project-persistence.md) — TOML 配置格式