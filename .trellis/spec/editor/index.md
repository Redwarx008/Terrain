# Editor Development Guidelines

> Best practices for editor UI development in this project.

---

## Overview

本目录包含编辑器 UI 的开发指南。编辑器迁移目标是 Avalonia 桌面 UI，位于 `Terrain.Editor/` 目录；旧 ImGui 代码只作为迁移前遗留实现参考。

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | UI 目录结构和模块组织 | Done |
| [Component Guidelines](./component-guidelines.md) | 面板、控件模式 | Done |
| [Hook Guidelines](./hook-guidelines.md) | 状态模式 (单例、命令、事件) | Done |
| [Native Viewport Hosting](./native-viewport-hosting.md) | Avalonia 中嵌入 SDL/Stride 视口的宿主约定 | Done |
| [State Management](./state-management.md) | 分层状态管理 | Done |
| [Quality Guidelines](./quality-guidelines.md) | 代码标准、ImGui 模式 | Done |
| [Type Safety](./type-safety.md) | 类型模式、事件参数 | Done |

---

## 关键约定

- 所有 C# 文件使用 `#nullable enable`
- Avalonia UI 使用 Simple 主题、XAML 布局和 MVVM
- 使用 `EditorState` 单例管理全局状态
- 使用 Avalonia 资源、样式和绑定进行样式控制
- 在 Dispose 中取消所有事件订阅

---

## 禁止模式

- **不要**新增 ImGui 代码
- **不要**在核心库中编写 ImGui 代码
- **不要**使用手算像素坐标进行 UI 布局
- **不要**使用硬编码颜色值

---

**语言**: 所有文档使用简体中文。
