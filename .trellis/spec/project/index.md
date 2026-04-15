# Project Guidelines - Terrain Editor

> Stride 引擎地形编辑器项目的核心规则

---

## 核心规则

1. **沟通语言**：始终使用简体中文与用户对话
2. **引擎源码**：Stride 引擎源码位于 `E:\WorkSpace\stride`
3. **不要直接修改引擎源码** - 如需修改，在项目中扩展或向官方提交 PR

---

## 规范索引

| 规范 | 描述 |
|------|------|
| [Communication](./communication.md) | 沟通语言规范 |
| [Engine Dependencies](./engine-dependencies.md) | 引擎源码路径配置 |

---

## 运行时指南

| 指南 | 描述 |
|------|------|
| [Directory Structure](../runtime/directory-structure.md) | 运行时项目目录布局与着色器组织 |
| [Error Handling](../runtime/error-handling.md) | 错误处理模式与异常使用规范 |
| [Logging Guidelines](../runtime/logging-guidelines.md) | Logger 初始化、分类与日志级别 |
| [Quality Guidelines](../runtime/quality-guidelines.md) | 构建验证、nullable、IDisposable、包管理 |

---

## 编辑器指南

| 指南 | 描述 |
|------|------|
| [Directory Structure](../editor/directory-structure.md) | 编辑器项目目录布局与命名前缀 |
| [Component Guidelines](../editor/component-guidelines.md) | UI 控件层次与 ImGui 渲染模式 |
| [State Management](../editor/state-management.md) | 单例服务、事件通信、撤销/重做 |
| [Type Safety](../editor/type-safety.md) | C# 命名约定、序列化模式、泛型约束 |
| [Quality Guidelines](../editor/quality-guidelines.md) | DPI 缩放、事件卫生、导出回滚 |

---

## 相关 Skills

- `stride-shader-asset-workflow` - SDSL 着色器开发规范（系统已集成）

---

## 项目结构

| 项目 | 作用 |
|------|------|
| Terrain | 核心运行时库 |
| Terrain.Editor | 编辑器应用程序 |
| Terrain.Windows | 运行时启动器 |
