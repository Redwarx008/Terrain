# Editor / Runtime 共用虚拟资源系统设计

**Date**: 2026-06-13  
**Status**: Approved Draft  
**Author**: Codex

---

## 1. 背景

当前 Terrain Runtime 和 Terrain Editor 仍然围绕旧的物理路径模型工作：

- Runtime 依赖 `TerrainDataPath`、`BiomeConfigPath`
- Editor 依赖 `ProjectManager`、`TomlProjectConfig`、Open/New/Load Workspace 工作流
- 旧 `project.toml` / `biome_config.toml` 把入口路径、材质槽、biome 规则混在一起
- Runtime 与 Editor 没有共享一套规范的资源解析逻辑

这与目标中的 P 社式资源组织和覆盖模型不一致。

本设计将 Terrain 项目改造成：

- **单一虚拟资源身份**
- **单一覆盖顺序来源**
- **Runtime 定义规范解析逻辑**
- **Editor 直接复用 Runtime 解析逻辑**
- **作者态资源与运行时二进制资源职责分离**

---

## 2. 目标

1. Runtime 与 Editor 共享同一套虚拟资源解析逻辑。
2. Editor 启动后直接从固定入口加载，不再打开“工作区”或“项目”。
3. 资源身份统一为虚拟路径，不再以绝对路径作为主模型。
4. `LaunchSetting.json` 成为唯一的显式 mod 顺序配置。
5. 用新的地图入口和领域文件替代旧 `project.toml` / `biome_config.toml`。
6. `heightmap.png` 作为作者态高度真相源，`.terrain` 作为运行时消费物。
7. 保存始终写回当前最终命中的实体文件，不自动 fallback。

---

## 3. 非目标

- 不保留旧 `TerrainDataPath` / `BiomeConfigPath` 兼容链路
- 不保留旧 `project.toml` / `biome_config.toml` 兼容链路
- 不自动创建 override 文件
- 不自动把 base 资源复制到 mod 层
- 不支持 `replace_path`、tombstone、虚拟删除、目录级替换
- 不在本轮实现 climate / season
- 不在本轮实现 provinces 编辑链路

---

## 4. 核心决策

| 主题 | 决策 |
|------|------|
| 资源覆盖配置 | 使用 `LaunchSetting.json` |
| base | 隐式存在，不写进配置 |
| 资源根 | 优先从 `AppContext.BaseDirectory` 向上定位工作区 `game/`；若起点本身已在完整合法的 `game/` 根内，也直接接受该根 |
| Editor 启动 | 直接自动加载，不再 Open/New/Load Workspace |
| 资源身份 | 统一为虚拟路径 |
| 地图入口 | `map_data/default.toml` |
| 材质描述文件 | `map_data/materials/descriptor.toml` |
| biome 规则文件 | `map_data/biome_settings.toml` |
| biome mask | 固定为 `map_data/biome_mask.png` |
| 旧路径字段 | 完全退场，不保留兼容 |
| 高度真相源 | `heightmap.png` |
| `.terrain` 角色 | 运行时 VT 二进制消费物 |
| Save 语义 | 只写作者态资源 |
| `.terrain` 更新 | 只能显式 `Export .terrain` |
| Export 目标 | 固定写回当前命中的 `map_data/terrain.terrain` |
| provinces | 保留资源位，v1 不实现链路 |

---

## 5. 资源目录结构

`v1` 地图资源结构如下：

```text
map_data/
  default.toml
  heightmap.png
  terrain.terrain
  biome_mask.png
  biome_settings.toml
  rivers.png              # optional
  provinces.png           # optional, v1 不接入
  materials/
    descriptor.toml
    <material textures...>
```

约束如下：

- `map_data/default.toml` 是唯一地图入口
- `map_data/materials/descriptor.toml` 固定存在，且只此一个
- `map_data/biome_settings.toml` 固定存在，且只此一个
- `map_data/biome_mask.png` 是固定约定路径
- `map_data/materials/` 内平铺材质贴图
- 不使用 `common/`
- 不使用 `map_data/regions`
- `climate` / `season` 暂不进入模型

---

## 6. TOML 与资源文件模型

### 6.1 `map_data/default.toml`

```toml
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
rivers = "rivers.png"
provinces = "provinces.png"

[settings]
height_scale = 100.0
```

语义如下：

- `heightmap`：必填，作者态高度图
- `terrain_data`：必填，运行时 `.terrain`
- `rivers`：可选，缺失则不生成或接入河流相关结果
- `provinces`：可选，保留资源位，v1 不接入 provinces 链路
- `height_scale`：唯一真相源
- `heightmap` / `terrain_data` / `rivers` / `provinces` 的值都解释为相对于 `map_data/` 当前目录的相对路径

`default.toml` **不再列出**以下路径：

- `biome_mask.png`
- `biome_settings.toml`
- `materials/descriptor.toml`

这些都改为固定约定路径。

### 6.2 `map_data/materials/descriptor.toml`

```toml
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
albedo = "grass_a.png"
normal = "grass_n.png"
properties = "grass_p.png"
```

语义如下：

- `id`：稳定材质标识，供 biome 规则引用
- `index`：运行时材质槽位索引
- `name`：编辑器显示名
- `albedo` / `normal` / `properties`：相对于 `map_data/materials/` 的短相对路径

约束如下：

- 不为单个材质建立子目录
- 不钉死文件命名规则
- descriptor 中写什么路径就使用什么路径
- 贴图平铺放在 `map_data/materials/`

### 6.3 `map_data/biome_settings.toml`

```toml
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true

[[modifiers]]
id = 1
layer_id = 1
type = "HeightRange"
blend_mode = "Multiply"
min = 0.0
max = 200.0
min_falloff = 1.0
max_falloff = 1.0
opacity = 1.0
enabled = true
visible = true
```

语义如下：

- `biomes`：定义 biome 集合
- `layers`：定义层，并通过 `material_id` 引用材质
- `modifiers`：定义 layer 上的规则栈

明确约束：

- biome 规则通过稳定 `material_id` 引用材质
- 不直接在规则层写数字 `index`
- `biome_mask` 不写进该文件
- 全局 biome mask 固定使用 `map_data/biome_mask.png`

### 6.4 `map_data/biome_mask.png`

这是单张全局图：

- 固定路径：`map_data/biome_mask.png`
- 不在 `default.toml` 中显式声明
- 不在 `biome_settings.toml` 中显式声明
- 由固定约定路径自动加载

---

## 7. `LaunchSetting.json`

### 7.1 文件职责

`LaunchSetting.json` 只做一件事：

- 描述 mod 层顺序

它不负责：

- 声明 base
- 指定地图入口
- 声明单个资源映射
- 处理依赖关系

### 7.2 推荐结构

```json
{
  "version": 1,
  "mods": [
    {
      "id": "mod_a",
      "root": "E:/some/path/mod_a",
      "enabled": true
    },
    {
      "id": "mod_b",
      "root": "E:/some/path/mod_b",
      "enabled": true
    }
  ]
}
```

语义如下：

- `version`：配置版本
- `mods`：mod 层列表
- `id`：层标识，仅用于日志、诊断、UI
- `root`：这一层的实体根目录
- `enabled`：是否参与当前层栈

### 7.3 覆盖顺序

- `mods` 数组顺序就是覆盖顺序
- 越靠后优先级越高
- 不引入单独的 `loadOrder` 字段

---

## 8. 隐式 base 与虚拟资源解析

### 8.1 隐式 base

`base` 永远隐式存在：

- 不出现在 `LaunchSetting.json`
- 根目录固定为定位到的工作区 `game/`

最终层栈为：

1. 隐式 `base`
2. `LaunchSetting.json` 中所有 `enabled = true` 的 mod，按数组顺序追加

### 8.2 虚拟路径

资源的唯一身份统一为虚拟路径，例如：

- `map_data/default.toml`
- `map_data/heightmap.png`
- `map_data/terrain.terrain`
- `map_data/biome_mask.png`
- `map_data/biome_settings.toml`
- `map_data/materials/descriptor.toml`

### 8.3 解析规则

给定任意虚拟路径：

1. 从最高优先级层开始查找
2. 找到第一个存在的实体文件
3. 该实体文件就是当前生效版本

例如：

- `base/map_data/heightmap.png` 存在
- `mod_a/map_data/heightmap.png` 不存在
- `mod_b/map_data/heightmap.png` 存在

则最终命中：

- `mod_b/map_data/heightmap.png`

### 8.4 解析器返回信息

共享解析器至少返回：

- `virtualPath`
- `resolvedPath`
- `sourceLayerId`
- `isWritable`
- `hasLowerPriorityFallback`

这些信息用于：

- Runtime 加载
- Editor 诊断
- 保存目标确认
- 资源来源展示

---

## 9. 启动与自动加载

### 9.1 Editor 启动

Editor 启动后固定执行：

1. 优先从 `AppContext.BaseDirectory` 向上定位工作区 `game/`；如果起点本身已处于一个带 `LaunchSetting.json` + `map_data/` 的完整 `game/` 根，则直接使用该根
2. 读取 `game/LaunchSetting.json`
3. 构建 `base + enabled mods` 层栈
4. 解析 `map_data/default.toml`
5. 解析 `default.toml` 中声明的：
   - `heightmap`
   - `terrain_data`
   - `rivers`（可选）
   - `provinces`（可选）
   - `height_scale`
6. 按固定约定解析：
   - `map_data/biome_settings.toml`
   - `map_data/materials/descriptor.toml`
7. 对 `map_data/biome_mask.png` 只保留固定写回目标；文件存在则加载，缺失则使用内存中的默认空 mask

其中：

- Editor 启动时不要求 `terrain.terrain` 已存在
- Editor 启动时不要求 `biome_mask.png` 已存在
- `terrain.terrain` 的缺失只影响 Runtime 消费，不阻塞 Editor 作者态工作流

### 9.2 Runtime 启动

Runtime 也固定使用同一条资源链：

1. 优先从 `AppContext.BaseDirectory` 向上定位工作区 `game/`；如果起点本身已处于一个带 `LaunchSetting.json` + `map_data/` 的完整 `game/` 根，则直接使用该根
2. 读取 `game/LaunchSetting.json`
3. 构建 `base + enabled mods` 层栈
4. 解析 `map_data/default.toml`
5. 忽略 `default.toml` 中的 `heightmap` 声明，只加载 Runtime 必需与固定约定资源
6. 若 `terrain.terrain` 或 `biome_mask.png` 缺失，则记录错误日志并保持 terrain 未初始化

### 9.3 必填与可选

Editor 启动必填资源：

- `LaunchSetting.json`
- `map_data/default.toml`
- `heightmap`
- `map_data/biome_settings.toml`
- `map_data/materials/descriptor.toml`

Editor 启动可选资源：

- `terrain_data`
- `map_data/biome_mask.png`
- `rivers.png`
- `provinces.png`

Runtime 必填资源：

- `LaunchSetting.json`
- `map_data/default.toml`
- `terrain_data`
- `map_data/biome_mask.png`
- `map_data/biome_settings.toml`
- `map_data/materials/descriptor.toml`

Runtime 可选资源：

- `rivers.png`
- `provinces.png`

Editor 缺少可选资源时：

- 不阻塞启动
- `terrain_data` 仅保留导出目标
- `biome_mask.png` 仅保留写回目标并使用默认空 mask
- 跳过可选资源的接入

Runtime 缺少可选资源时：

- 不阻塞启动
- 跳过对应结果生成或接入

Runtime 对 `heightmap` 的口径：

- `default.toml` 中仍保留 `heightmap` 字段，供 Editor 作者态加载和保存使用
- Runtime 不解析 `heightmap` 的路径值
- Runtime 不校验 `heightmap` 文件是否存在
- Runtime 不把 `heightmap` 放进 runtime bundle

Runtime 缺少必填资源时：

- `TerrainProcessor` 记录错误日志
- 当前地形保持未初始化
- 同配置下不重复逐帧重试

---

## 10. Save 与 Export 语义

### 10.1 Save

`Save` 只写作者态资源，不自动更新 `.terrain`。

作者态资源包括：

- `map_data/heightmap.png`
- `map_data/biome_mask.png`
- `map_data/biome_settings.toml`
- `map_data/default.toml`
- `map_data/materials/descriptor.toml`

当前 `v1` 不通过 `Save` 写回：

- `map_data/rivers.png`
- `map_data/materials/*.png`

### 10.2 `.terrain`

`.terrain` 的职责明确为：

- 保存运行时虚拟纹理和相关二进制数据
- 供 Runtime 直接消费
- 不是作者态真相源

### 10.3 Export `.terrain`

`.terrain` 只能通过显式命令更新：

- `Export .terrain`

并且导出目标固定为当前解析命中的：

- `map_data/terrain.terrain`

不允许：

- 另选导出路径
- Export As
- Save 时自动重建 `.terrain`

### 10.4 写回规则

保存与导出的统一规则是：

> 最终加载哪个，就写回哪个。

具体而言：

1. 先根据虚拟路径解析当前命中的实体文件
2. 直接写回该实体文件
3. 如果该文件不可写，则操作失败

明确禁止：

- 自动换层
- 自动 fallback
- 自动创建 override
- 自动复制 base 到 mod

---

## 11. Runtime / Editor 组件边界

### 11.1 Runtime

`TerrainComponent` 不再保留任何资源路径字段：

- 不保留 `TerrainDataPath`
- 不保留 `BiomeConfigPath`
- 不新增 `MapDefinitionPath`

Runtime 资源入口固定为应用约定：

- `LaunchSetting.json`
- `map_data/default.toml`

`TerrainProcessor` 的固定加载链为：

1. 读取 `LaunchSetting.json`
2. 创建 resolver
3. 读取 `map_data/default.toml`
4. 加载 `terrain_data`
5. 固定加载 `biome_mask.png`
6. 固定加载 `biome_settings.toml`
7. 固定加载 `materials/descriptor.toml`
8. 构建 `material_id -> index` 映射
9. 用 `.terrain` 中的高度数据 + `biome_mask` + `biome settings` 构建运行时 detail maps
10. 用 `.terrain` 作为运行时 VT 数据输入

### 11.2 Editor

Editor 不再保留项目工作流：

- 删除 Open Project
- 删除 New Project
- 删除 Save Project As
- 删除 Load Workspace

Editor 只保留：

- 自动启动加载
- Save
- Export `.terrain`

### 11.3 新增运行时资源基础设施

共享基础设施直接放在 `Terrain` 项目中，建议包括：

- `Terrain/Resources/LaunchSettingsService.cs`
- `Terrain/Resources/GameResourceResolver.cs`
- `Terrain/Resources/ResolvedGameResource.cs`
- `Terrain/Resources/RuntimeMapDefinitionReader.cs`
- `Terrain/Resources/RuntimeMaterialDescriptorReader.cs`
- `Terrain/Resources/RuntimeBiomeSettingsReader.cs`

### 11.4 旧类退场

以下旧主模型需要退出：

- `TerrainDataPath`
- `BiomeConfigPath`
- `RuntimeBiomeConfig`
- `ProjectManager` 的项目打开/保存职责
- `TomlProjectConfig` 的资源身份职责
- 旧 `project.toml`
- 旧 `biome_config.toml`

强调：

- 不保留兼容路径
- 不保留旧读取 fallback
- 不做自动迁移桥接

---

## 12. v1 范围约束

### 12.1 已纳入主链路

- `heightmap.png`
- `terrain.terrain`
- `biome_mask.png`
- `biome_settings.toml`
- `materials/descriptor.toml`
- `rivers.png`（存在时接入）

### 12.2 明确保留资源位但不实现

- `provinces.png`

`provinces.png` 在 `default.toml` 中仍然允许声明，但 `v1`：

- 不实现加载链路
- 不实现编辑链路
- 不实现保存链路
- 不阻塞本轮资源系统改造

可以在诊断区提示：

- `provinces` 已声明
- 当前版本尚未接入该链路

---

## 13. 校验与错误处理

### 13.1 `default.toml`

需要校验：

- `heightmap` 非空
- `terrain_data` 非空
- `height_scale > 0`

### 13.2 `materials/descriptor.toml`

需要校验：

- `material.id` 唯一
- `material.index` 唯一
- 材质贴图路径合法
- 如果贴图路径被声明但文件不存在，给出清晰诊断

### 13.3 `biome_settings.toml`

需要校验：

- `biome.id` 唯一
- `layer.id` 唯一
- `modifier.id` 唯一
- `layer.biome_id` 必须引用已存在 biome
- `layer.material_id` 必须引用已存在 material
- `modifier.layer_id` 必须引用已存在 layer

### 13.4 诊断行为

Editor 资源 bootstrap 失败时：

- Editor 主窗口仍然启动
- 当前实现通过控制台消息报告错误
- 不提供单独的“诊断态”界面

Runtime 必填资源缺失或配置错误时：

- `TerrainProcessor` 记录错误日志
- 当前 terrain 保持未初始化

保存失败时：

- 明确显示目标文件不可写或写入失败
- 不做 fallback
- 不改写其它层

---

## 14. 迁移顺序

推荐按以下顺序实施：

1. 在 `Terrain` 中引入 `LaunchSettingsService` 与 `GameResourceResolver`
2. 在 `Terrain` 中引入 `default.toml`、`materials/descriptor.toml`、`biome_settings.toml` 读取器
3. 将 Runtime 的 `TerrainProcessor` 与材质装配流程切到新入口
4. 删除 Runtime 对旧 `TerrainDataPath` / `BiomeConfigPath` / `RuntimeBiomeConfig` 的主链路依赖
5. 将 Editor 启动改造成自动 bootstrap 到 Runtime 解析逻辑
6. 删除 Editor 的 Open/New/Load Workspace/Save As 工作流与旧项目职责

---

## 15. 实现后验收标准

完成后应满足：

1. Editor 打开后无需手动选项目，直接加载约定资源
2. Runtime 与 Editor 对同一虚拟路径解析到同一个实体文件
3. 调整 `mods` 顺序后，最终加载资源随之变化
4. `Save` 只写作者态资源，不自动重建 `.terrain`
5. `Export .terrain` 只写当前命中的 `map_data/terrain.terrain`
6. 命中的目标不可写时，操作失败且不 fallback
7. 旧路径字段和旧 TOML 主链路不再参与系统行为

---

## 16. 备注

- 本设计已经明确放弃旧路径兼容层
- 本设计将 `.terrain` 视为运行时派生产物，而不是作者态真相源
- 本设计将 provinces 明确降为非阻塞资源位，避免拖慢当前改造主线
