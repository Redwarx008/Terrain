---
doc_type: architecture
slug: project-persistence
scope: TOML 项目配置持久化系统
summary: ProjectManager 管理项目生命周期，TomlProjectConfig 是数据模型 + TOML 读写器，所有路径使用相对路径确保可移植
status: current
last_reviewed: 2026-04-20
tags: [persistence, toml, config]
depends_on: [editor-services]
---

## 1. 定位与受众

本文档描述项目持久化系统。读者是修改项目配置格式、添加新 TOML 字段、或调试路径问题时需要理解 TOML I/O 流程的人。

## 2. 结构与交互

```
ProjectManager (singleton)
    ├─ CreateProject(path, name) → 创建 .toml + 默认配置
    ├─ OpenProject(path) → 加载 .toml → 初始化 TerrainManager
    ├─ SaveConfig(config) → 写入 .toml
    ├─ SaveProjectAs(path) → 复制资源 + 生成新 .toml
    └─ MarkDirty() / IsDirty → 脏追踪

TomlProjectConfig
    ├─ 字段: name, heightmap_path, climate_mask_path, height_scale
    ├─ 字段: [[material_slots]] (index, name, albedo, normal)
    ├─ 字段: [[climates]] (id, name, debug_color)
    └─ 字段: [[climate_rules]] (climate_id, name, enabled, altitude, slope, material_slot_index)
```

### 路径转换

所有路径使用**相对路径**（相对于 .toml 所在目录）：
- 导入时 `MakeAbsolute()` 转换相对→绝对
- 保存时 `MakeRelative()` 转换绝对→相对

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| ProjectFilePath | string | ProjectManager | 内存 |
| IsDirty | bool | ProjectManager | 内存 |
| TomlProjectConfig | class | ProjectManager | .toml 文件 |

## 4. 关键决策

- **TOML 格式** → `2026-04-20-decision-toml-project-persistence.md`
- **相对路径确保可移植** → 所有路径相对于 .toml 所在目录

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| ProjectManager | `Terrain.Editor/Services/ProjectManager.cs:11` | 项目管理 singleton |
| CreateProject | `Terrain.Editor/Services/ProjectManager.cs:59` | 创建新项目 |
| OpenProject | `Terrain.Editor/Services/ProjectManager.cs:81` | 打开项目 |
| SaveConfig | `Terrain.Editor/Services/ProjectManager.cs:134` | 保存配置 |
| SaveProjectAs | `Terrain.Editor/Services/ProjectManager.cs:147` | 另存为 |
| TomlProjectConfig | `Terrain.Editor/Services/TomlProjectConfig.cs` | TOML 数据模型 |
| MakeRelative | `Terrain.Editor/Services/TomlProjectConfig.cs` | 绝对→相对路径 |
| NewProjectWizard | `Terrain.Editor/UI/Dialogs/NewProjectWizard.cs` | 新建项目向导 |

## 6. 已知约束 / 边界情况

- Tommy 3.1.2 API 陷阱 → `2026-04-20-learning-tommy-toml.md`
- 路径分隔符：Windows 上使用反斜杠，Tommy 输出正斜杠，需要统一
- SaveProjectAs 复制资源文件到新目录，大文件时耗时

## 7. 相关文档

- [editor-services.md](editor-services.md) — TerrainManager 协调加载/保存
- `2026-04-20-learning-tommy-toml.md` — Tommy API 陷阱
- `2026-04-20-decision-toml-project-persistence.md` — TOML 选型决策