# MapData 顶部注释模板常驻写回设计

**Date**: 2026-06-15  
**Status**: Draft  
**Author**: Codex

---

## 1. 背景

当前 `map_data` 作者态骨架已经支持在缺失时自动生成：

- `map_data/default.toml`
- `map_data/materials/descriptor.toml`
- `map_data/biome_settings.toml`

但这些自动生成文件只包含“最小合法 TOML”，没有给用户可直接参考的模板示例。

用户要求：

- 三份自动生成的文件都应带有**被注释掉的模板示例**
- 这些注释**不只存在于首次 scaffold**
- 后续作者态 `Save` 写回后，模板注释也必须继续保留

因此，这个需求不是单纯的“scaffold 文案增强”，而是 writer 输出契约的变化：

- `scaffold` 生成要带模板注释
- writer 正常写回也要继续带模板注释

---

## 2. 目标

1. 三份 `map_data` 文本文件在 scaffold 时都带顶部注释模板
2. 三份文件在后续 writer 写回后，顶部注释模板仍然存在
3. 注释模板不影响当前 runtime reader 解析
4. 注释模板对用户来说是“可复制、可取消注释、可改值”的最小工作示例
5. 保持当前真实数据读写语义不变，不因为模板注释引入伪数据

---

## 3. 非目标

- 不保留用户手工新增的任意自由注释
- 不实现 comment-aware round-trip
- 不尝试在写回时保留用户修改过的模板注释文本
- 不修改 runtime reader 去感知模板注释
- 不改动 TOML 字段定义、默认值或路径规则

---

## 4. 核心决策

| 主题 | 决策 |
|------|------|
| 注释位置 | 固定在文件顶部 |
| 真实数据位置 | 注释模板之后 |
| 注释保留方式 | 每次写回重新生成同一套规范模板 |
| 用户自定义注释 | 不保证保留 |
| scaffold 与 save | 统一复用同一 writer 输出契约 |
| reader | 保持不变，仅消费真实 TOML |

---

## 5. 写回语义

本轮把三份文件的输出语义定义为：

> **规范化模板写回**

具体含义：

- writer 每次输出文件时，都生成固定的顶部注释模板
- 注释模板下方再写入当前真实生效的数据
- 下次 `Save` 时，文件会再次被规范化成同样风格

这意味着：

- 我们保证“官方模板注释一直存在”
- 但不保证“用户自定义注释被原样保留”

这是有意的边界，因为当前代码并没有 comment-preserving round-trip 基础，硬做只会引入脆弱实现。

---

## 6. 文件布局

三份文件统一采用：

1. 顶部注释模板
2. 一个空行分隔
3. 当前真实 TOML 数据

这样有两个目的：

- 用户一打开文件就能先看到示例
- reader 只需要继续读取真实 TOML 数据，不需要适配新结构

---

## 7. 各文件模板设计

### 7.1 `map_data/default.toml`

顶部注释模板应覆盖可选资源和最常见路径形态，例如：

```toml
# Optional terrain companion resources:
# rivers = "rivers.png"
# provinces = "provinces.png"
```

真实数据部分继续写：

- `version`
- `[terrain].heightmap`
- `[terrain].terrain_data`
- 可选 `rivers`
- 可选 `provinces`
- `[settings].height_scale`

设计原则：

- 模板只提示用户最容易补齐的可选键
- 不重复把整份 `default.toml` 完整抄成注释版，避免顶部噪声太大

### 7.2 `map_data/materials/descriptor.toml`

顶部注释模板提供一组可直接照抄的材质条目：

```toml
# Example material:
# [[materials]]
# id = "plains"
# index = 0
# name = "Plains"
# albedo = "plains_01_diffuse.dds"
# normal = "plains_01_normal.dds"
# properties = "plains_01_properties.dds"
```

真实数据部分继续写：

- `version = 1`
- 当前真实 `materials`

设计原则：

- 示例必须体现单文件名贴图路径约束
- 示例只放一条代表性材质，避免文件头过长

### 7.3 `map_data/biome_settings.toml`

顶部注释模板提供三段最小工作示例：

```toml
# Example biome:
# [[biomes]]
# id = 1
# name = "Default"
#
# Example layer:
# [[layers]]
# id = 1
# biome_id = 1
# name = "Base"
# material_id = "plains"
# priority = 0
# enabled = true
# visible = true
#
# Example modifier:
# [[modifiers]]
# id = 1
# layer_id = 1
# name = "Slope"
# type = "slope"
# blend_mode = "add"
# min = 0.2
# max = 0.8
# min_falloff = 0.1
# max_falloff = 0.1
# opacity = 1.0
# enabled = true
# visible = true
```

真实数据部分继续写：

- `version = 1`
- 当前真实 `biomes`
- 当前真实 `layers`
- 当前真实 `modifiers`

设计原则：

- modifier 示例只放一组最核心字段
- 不把所有可选高级参数都写进模板，避免模板压过真实数据

---

## 8. 实现策略

### 8.1 不改 reader

reader 当前已经能天然忽略注释，因此：

- `RuntimeMapDefinitionReader`
- `RuntimeMaterialDescriptorReader`
- `RuntimeBiomeSettingsReader`

都不需要改。

### 8.2 改 writer 契约

需要修改：

- `MapDefinitionWriter`
- `MaterialDescriptorWriter`
- `BiomeSettingsWriter`

核心变化：

- 不再只依赖 `TomlTable.WriteTo(writer)` 直接落整份最终文件
- writer 改成受控输出：
  - 先写固定顶部注释模板
  - 再写真实 TOML 数据

可以仍然局部复用现有 `TomlTable` / `TomlArray` 组装真实数据，但最终文件落盘必须由 writer 控制完整布局。

### 8.3 scaffold 继续复用 writer

`EditorMapDataScaffoldService` 不需要单独维护一套模板文本。

它应继续调用这三个 writer，让：

- 首次 scaffold
- 后续 `Save`

都共享同一输出规则，避免双份模板定义漂移。

---

## 9. 测试策略

至少新增或补强以下测试：

### 9.1 scaffold 模板存在性

验证 scaffold 后：

- `default.toml` 顶部存在注释模板
- `descriptor.toml` 顶部存在 `# [[materials]]` 示例
- `biome_settings.toml` 顶部存在 `# [[biomes]]` / `# [[layers]]` / `# [[modifiers]]` 示例

### 9.2 scaffold 后仍可解析

对 scaffold 生成的三份文件继续执行：

- `RuntimeMapDefinitionReader.ReadFrom(...)`
- `RuntimeMaterialDescriptorReader.ReadFrom(...)`
- `RuntimeBiomeSettingsReader.ReadFrom(...)`

并确认默认值与现状一致。

### 9.3 writer 写回后模板仍在

分别测试：

- `MapDefinitionWriter`
- `MaterialDescriptorWriter`
- `BiomeSettingsWriter`

断言写回后文件顶部仍保留预期注释模板。

### 9.4 writer 写回后真实数据仍正确

重新读取写回结果并断言：

- 路径字段正确
- 数组条目正确
- material / biome / layer / modifier 数据正确

避免出现“注释保住了，但真实 TOML 被写坏”的假绿。

---

## 10. 风险与取舍

### 风险 1：格式从“纯 Tommy 输出”变成“受控文本输出”

影响：

- writer 代码会更手工
- 测试需要更明确锁定格式边界

接受理由：

- 这是“常驻注释模板”的必要代价
- 比后处理拼接或 comment-aware round-trip 更可控

### 风险 2：用户修改模板注释会在下次 Save 被覆盖

影响：

- 模板注释不是用户可持续编辑区

接受理由：

- 需求是“模板一直保留”，不是“保留任意注释编辑”
- 规范化输出比半保留、半覆盖更可预期

---

## 11. 验收标准

完成后应满足：

1. 首次 scaffold 生成的三份文件都带顶部注释模板
2. scaffold 生成后的文件仍可被现有 reader 正常读取
3. 用户修改真实数据后触发 `Save`，顶部注释模板仍然存在
4. `Save` 后真实 TOML 数据仍与当前内存状态一致
5. `docs/design/map-data-toml-formats.md` 明确记录“自动生成与作者态写回都会保留顶部注释模板示例”

---

## 12. 文档影响

实现完成后需要同步更新：

- `docs/design/map-data-toml-formats.md`

建议补充：

- 三份文件的顶部模板示例策略
- 注释模板会在 scaffold 与后续写回时持续保留
- 注释模板是规范化输出，不保证保留用户自定义注释

