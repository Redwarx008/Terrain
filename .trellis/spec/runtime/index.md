# Runtime Development Guidelines

> Best practices for runtime/core development in this project.

---

## Overview

本目录包含运行时核心库的开发指南。运行时代码位于 `Terrain/` 目录，包括地形组件、渲染系统、流送系统。

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | 模块组织和文件布局 | Done |
| [Database Guidelines](./database-guidelines.md) | TOML 配置、二进制数据格式 | Done |
| [Terrain Runtime Persistence](./terrain-runtime-persistence.md) | `.terrain v6` 与 Runtime biome 重建合同 | Done |
| [Error Handling](./error-handling.md) | 异常模式、错误恢复 | Done |
| [Quality Guidelines](./quality-guidelines.md) | 代码标准、禁止模式 | Done |
| [Logging Guidelines](./logging-guidelines.md) | 调试日志、日志级别 | Done |

---

## 关键约定

- 所有 C# 文件使用 `#nullable enable`
- 使用 Stride `DataContract`/`DataMember` 进行序列化
- 不继承的类使用 `sealed` 修饰符
- 字段使用 `_camelCase` 前缀
- 使用 `System.Diagnostics.Debug.WriteLine` 进行诊断

---

## 禁止模式

- **不要**在核心库中引用编辑器代码
- **不要**在热路径中创建 GC 对象
- **不要**使用裸 `new Texture()`，使用 Stride 资源系统

---

**语言**: 所有文档使用简体中文。
