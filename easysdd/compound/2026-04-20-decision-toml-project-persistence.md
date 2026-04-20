---
doc_type: decision
status: current
tags: [persistence, config, architecture]
created: 2026-04-20
---

# TOML 项目持久化

## 背景

编辑器没有 Open/Save 流程，用户无法保存和恢复工作状态。需要一种人类可编辑的持久化格式。

## 决定

使用 `.toml` 文件（Tommy 库）存储项目配置：heightmap/climate_mask 路径、材质槽位纹理路径、气候定义和规则。

## 备选方案

| 方案 | 优点 | 缺点 |
|---|---|---|
| **JSON** | 通用、工具链成熟 | 不易手写编辑、逗号陷阱 |
| **XML** | Stride 原生支持 | 冗长、不可读 |
| **TOML（选用）** | 人类可编辑、可版本控制 | 额外 NuGet 依赖（Tommy） |

## 理由

1. TOML 比 JSON 更适合人类手写编辑（无逗号、无引号需求）
2. 可版本控制（文本格式、diff 友好）
3. `[[section]]` 语法天然适合数组元素（如 material_slots、climate_rules）
4. Tommy 库 .NET 生态可用

## 权衡

- 额外 NuGet 依赖
- Tommy 库 API 有陷阱（见 learning-tommy-toml）

## 影响

- TomlProjectConfig 成为项目 I/O 核心
- 所有路径使用相对路径（相对于 .toml 所在目录），确保项目可移植
- ProjectManager 管理 dirty 状态和 save/load 生命周期