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

## 相关 Skills

- `stride-shader-asset-workflow` - SDSL 着色器开发规范（系统已集成）

---

## 项目结构

| 项目 | 作用 |
|------|------|
| Terrain | 核心运行时库 |
| Terrain.Editor | 编辑器应用程序 |
| Terrain.Windows | 运行时启动器 |
| TerrainPreProcessor | 预处理工具 |
