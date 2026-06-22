# map_data 文本格式规格

**状态：** 当前实现  
**适用范围：**
- `map_data/default.toml`
- `map_data/materials/descriptor.toml`
- `map_data/biome_settings.toml`

---

## 目标

本文档记录 Terrain 当前实现实际接受和写回的三类作者态 TOML 规格，作为 Editor 作者态、Runtime bootstrap、以及后续资源迁移的统一参考。

这里的“规格”以当前读取器、写回器和自动测试为准，不以历史设计稿或旧导出格式为准。

---

## 总体约束

- 三个文件都要求根级 `version = 1`
- 三个读取器都会拒绝未知字段
- 路径字段一律使用 `/` 作为规范分隔符
- rooted 路径（如 `/foo`、`\\foo`、`C:\\foo`）一律非法
- `..` 父目录穿越一律非法
- 字段名大小写敏感，必须与本文档一致

---

## 1. `map_data/default.toml`

### 作用

地图入口文件，声明地图主资源路径与全局高度缩放。

- Editor 作者态会使用：
  - `heightmap`
  - `terrain_data`
  - `rivers`
  - `provinces`
  - `settings.height_scale`
  - `settings.river_min_width`
  - `settings.river_max_width`
  - `settings.river_max_visible_camera_height`
- Runtime 会忽略 `heightmap` 的消费，但字段仍保留在格式中

### 根级结构

只允许以下根字段：

- `version`
- `terrain`
- `settings`

### `[terrain]` 允许字段

- `heightmap`
- `terrain_data`
- `rivers`
- `provinces`

### `[settings]` 允许字段

- `height_scale`
- `river_min_width`
- `river_max_width`
- `river_max_visible_camera_height`

### 必填字段

- `version`
- `[terrain]`
- `[settings]`
- `terrain.heightmap`
  - 仅在默认读取模式下必填
  - Runtime 模式会以 `requireHeightmap = false` 读取，因此不会消费该字段
- `terrain.terrain_data`
- `settings.height_scale`

### 可选字段

- `terrain.rivers`
- `terrain.provinces`
- `settings.river_min_width`
- `settings.river_max_width`
- `settings.river_max_visible_camera_height`

### 数值与路径约束

- `version` 必须是整数，且只能为 `1`
- `settings.height_scale` 必须是数字（整数或浮点），且必须 `> 0`
- `settings.river_min_width` / `settings.river_max_width` 是河流 full-width（完整宽度）范围，省略时默认分别为 `1.0` / `4.0`
- `settings.river_min_width` 必须是有限数字，且必须 `> 0`
- `settings.river_max_width` 必须是有限数字，且必须 `>= settings.river_min_width`
- `settings.river_max_visible_camera_height` 是河流最大可见摄像机 world Y，高于或等于该值时 `RiverRenderFeature` 会跳过 river seed / bottom / surface 全链路；省略时默认 `3000.0`
- `settings.river_max_visible_camera_height` 必须是有限数字，且必须 `> 0`
- `rivers.png` 的河流宽度 palette index 会线性映射到 `[river_min_width, river_max_width]`，mesh 内部再转为 half-width 生成 ribbon
- 所有路径字段都必须是相对路径
- 路径会做如下规范化：
  - `\\` 转成 `/`
  - 去掉空段和 `.`
  - 拒绝 `..`
  - 规范化后不能为空

### 当前示例

```toml
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
rivers = "rivers.png"
provinces = "provinces.png"

[settings]
height_scale = 200.0
river_min_width = 1.0
river_max_width = 4.0
river_max_visible_camera_height = 3000.0
```

### 当前不再允许的旧格式

- 根级未知字段
- `[terrain]` 中的未知字段
- `[settings]` 中的未知字段
- 空路径
- rooted 路径
- 包含 `..` 的路径

---

## 2. `map_data/materials/descriptor.toml`

### 作用

材质描述文件，定义运行时可见的材质 ID、索引和贴图文件名。

### 根级结构

只允许以下根字段：

- `version`
- `materials`

### `[[materials]]` 允许字段

- `id`
- `index`
- `name`
- `albedo`
- `normal`
- `properties`

### 必填字段

每个 `[[materials]]` 条目都必须包含：

- `id`
- `index`
- `name`

### 可选字段

- `albedo`
- `normal`
- `properties`

### 数值与唯一性约束

- `version` 必须是整数，且只能为 `1`
- `materials` 必须是数组
- `id` 必须是非空字符串，且全文件唯一
- `index` 必须是整数，范围为 `0..254`
- `index = 255` 不允许使用
  - 当前实现把 `255` 视为 shader sentinel / 保留值
- `index` 必须全文件唯一

### 贴图路径约束

- `albedo` / `normal` / `properties` 出现时必须是非空字符串
- 路径必须是 **descriptor 文件所在目录下的单个文件名**
- 也就是说：
  - `plains_01_diffuse.dds` 合法
  - `./plains_01_diffuse.dds` 会被规范化成 `plains_01_diffuse.dds`
  - `textures/plains_01_diffuse.dds` 非法
  - `../plains_01_diffuse.dds` 非法
  - `C:\\foo.dds` 非法

### 当前示例

```toml
version = 1

[[materials]]
id = "plains"
index = 0
name = "Plains"
albedo = "plains_01_diffuse.dds"
normal = "plains_01_normal.dds"
properties = "plains_01_properties.dds"
```

### 当前不再允许的旧格式

- 根级旧字段或未知字段
- `[[materials]]` 中的未知字段
- 嵌套贴图路径
- 空 `id`
- 重复 `id`
- 重复 `index`
- 越界 `index`

---

## 3. `map_data/biome_settings.toml`

### 作用

定义 biome、layer 和可选 modifier。该文件当前不再声明 `biome_mask` 路径，也不再使用旧的 material slot 字段名。

### 根级结构

只允许以下根字段：

- `version`
- `biomes`
- `layers`
- `modifiers`

### `[[biomes]]` 允许字段

- `id`
- `name`

### `[[layers]]` 允许字段

- `id`
- `biome_id`
- `name`
- `material_id`
- `priority`
- `enabled`
- `visible`

### `[[modifiers]]` 允许字段

- `id`
- `layer_id`
- `name`
- `type`
- `blend_mode`
- `min`
- `max`
- `min_falloff`
- `max_falloff`
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
- `opacity`
- `enabled`
- `visible`

### 必填结构

- `version`
- `biomes`
- `layers`
- `modifiers` 可省略

### `[[biomes]]` 约束

- `id` 必须是 int32 范围内整数
- `name` 必须是非空字符串
- `id` 全文件唯一

### `[[layers]]` 约束

- `id` 必须是 int32 范围内整数，且全文件唯一
- `biome_id` 必须引用已存在的 biome
- `name` 必须是非空字符串
- `material_id` 必须是非空字符串
- `priority` 必须是整数
- `enabled` / `visible` 必须是布尔值
- 如果读取器提供了 `knownMaterialIds`，则 `material_id` 必须命中该集合

### `[[modifiers]]` 约束

- `id` 必须是 int32 范围内整数，且全文件唯一
- `layer_id` 必须引用已存在的 layer
- `type` / `blend_mode` 必须是非空字符串
- `min` / `max` / `min_falloff` / `max_falloff` / `opacity` 必须是数字
- `enabled` / `visible` 必须是布尔值
- `name` 可省略；省略时运行时默认值为 `Modifier {id}`
- 其他数值字段为可选，省略时使用默认值

### `[[modifiers]]` 默认值

- `radius = 1.0`
- `angle_degrees = 0.0`
- `angle_range_degrees = 180.0`
- `scale = 1.0`
- `offset_x = 0.0`
- `offset_y = 0.0`
- `seed = 0.0`
- `octaves = 4.0`
- `invert = 0.0`
- `texture_mask_channel = 0`

### `texture_mask` 路径约束

- 必须是相对路径
- 允许子目录
- 不允许 rooted 路径
- 不允许 `..`
- 当前读取器只做：
  - `trim`
  - `\\` 转 `/`
  - rooted / `..` 校验
- 当前读取器 **不会** 像 `default.toml` 那样去掉 `.` 或空段

### 当前示例

```toml
version = 1

[[biomes]]
id = 1
name = "Default"

[[layers]]
id = 1
biome_id = 1
name = "Base"
material_id = "plains"
priority = 0
enabled = true
visible = true

[[modifiers]]
id = 1
layer_id = 1
name = "Slope"
type = "slope"
blend_mode = "add"
min = 0.2
max = 0.8
min_falloff = 0.1
max_falloff = 0.1
opacity = 1.0
enabled = true
visible = true
```

### 当前不再允许的旧格式

- 根级 `biome_mask` 字段
- layer 使用旧的 material slot 字段名
- 未声明 biome 就引用 `biome_id`
- 未声明 layer 就引用 `layer_id`
- 空 `material_id`
- 空 modifier `type`

---

## 4. Editor 写回形态

当前 Editor writer 会稳定写出以下形态：

- `default.toml`
  - 总是写 `version = 1`
  - 总是写 `[terrain].heightmap`
  - 总是写 `[terrain].terrain_data`
  - `rivers` / `provinces` 仅在非空时写出
  - 总是写 `[settings].height_scale`
  - 总是写 `[settings].river_min_width`
  - 总是写 `[settings].river_max_width`
  - 总是写 `[settings].river_max_visible_camera_height`
  - 文件顶部会写入固定的注释模板示例；scaffold 自动生成和后续作者态写回都沿用同一组模板
  - 这些顶部注释属于规范化输出，不保证保留用户自定义注释

- `materials/descriptor.toml`
  - 总是写 `version = 1`
  - 总是写 `materials` 数组
  - 贴图字段只在非空时写出
  - 贴图路径只接受单个文件名
  - 文件顶部会写入固定的注释模板示例；scaffold 自动生成和后续作者态写回都沿用同一组模板
  - 这些顶部注释属于规范化输出，不保证保留用户自定义注释

- `biome_settings.toml`
  - 总是写 `version = 1`
  - 总是写 `biomes` 数组
  - 总是写 `layers` 数组
  - 总是写 `modifiers` 数组
    - 即使为空，也会写出空数组
  - `texture_mask` 仅在非空时写出
  - 文件顶部会写入固定的注释模板示例；scaffold 自动生成和后续作者态写回都沿用同一组模板
  - 这些顶部注释属于规范化输出，不保证保留用户自定义注释

---

## 5. Runtime / Editor 消费差异

- `default.toml.heightmap`
  - Editor 作者态读取并保存
  - Runtime 当前忽略其消费

- `default.toml.terrain_data`
  - Editor 用于导出目标定位
  - Runtime 用于加载 `.terrain`

- `biome_settings.toml`
  - Runtime 依赖其中的 `material_id` 与 modifier 定义构建 detail map

- `materials/descriptor.toml`
  - Runtime 依赖其中的 `id` / `index` / 贴图文件名建立材质映射

---

## 6. 代码基准

当前规格以以下实现为准：

- `Terrain/Resources/RuntimeMapDefinitionReader.cs`
- `Terrain/Resources/RuntimeMaterialDescriptorReader.cs`
- `Terrain/Resources/RuntimeBiomeSettingsReader.cs`
- `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`
- `Terrain.Editor/Services/Resources/MaterialDescriptorWriter.cs`
- `Terrain.Editor/Services/Resources/BiomeSettingsWriter.cs`
- `Terrain.Editor.Tests/VirtualResources/DescriptorReaderTests.cs`
- `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`

如果将来修改字段、默认值或路径规则，必须同步更新本文档。

## 作者态自动骨架

- `map_data/default.toml`、`map_data/materials/descriptor.toml`、`map_data/biome_settings.toml` 缺失时自动生成作者态骨架，且骨架不只是最小合法 TOML，还会带顶部注释模板示例
- `map_data/default.toml` 缺失时自动生成：
  - `heightmap = "heightmap.png"`
  - `terrain_data = "terrain.terrain"`
  - `height_scale = 100.0`
  - `river_min_width = 1.0`
  - `river_max_width = 4.0`
  - `river_max_visible_camera_height = 3000.0`
- `map_data/materials/descriptor.toml` 缺失时自动生成 `version = 1` 和空 `materials = []`
- `map_data/biome_settings.toml` 缺失时自动生成 `version = 1`、`biomes = []`、`layers = []`、`modifiers = []`
- `heightmap.png` 缺失时不会自动生成；Editor 以待补资源模式启动，并阻止 `Save` / `Export .terrain`
