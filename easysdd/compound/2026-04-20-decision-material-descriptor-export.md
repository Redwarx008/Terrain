---
doc_type: decision
status: current
tags: [export, runtime, architecture, separation]
created: 2026-04-20
---

# 独立 material_descriptor.toml 导出

## 背景

运行时 TerrainComponent.MaterialConfigPath 指向编辑器项目的 TOML 文件，导致运行时依赖编辑器项目文件。运行时不应依赖编辑器。

## 决定

导出独立的 `material_descriptor.toml` 文件，Runtime 的 MaterialConfigPath 指向该文件。

## 备选方案

| 方案 | 优点 | 缺点 |
|---|---|---|
| **Runtime 直接读编辑器 TOML** | 零额外导出 | 运行时耦合编辑器、路径不可控 |
| **嵌入 .terrain 文件** | 单文件分发 | 关注点混合、.terrain 格式膨胀 |
| **独立 material_descriptor.toml（选用）** | 关注点分离、可独立分发 | 多一个文件需要管理 |

## 理由

1. 关注点分离：运行时和编辑器的配置格式不同、生命周期不同
2. 可独立分发：material_descriptor.toml 可以随 .terrain 文件一起交付
3. Runtime 零改动：复用已有 `RuntimeMaterialManager.InitializeFromToml()`

## 权衡

- 多一个导出步骤和文件
- 优势：Runtime 不再依赖编辑器项目文件

## 影响

- MaterialDescriptorExporter 实现 IExporter 接口
- 路径转换使用 TomlProjectConfig.MakeRelative()
- 导出格式：`[[material_slots]]` TOML 数组