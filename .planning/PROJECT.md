# Terrain Slot Editor

## What This Is

独立地形编辑器应用（非 Stride Studio 插件），支持高度图编辑和材质槽绘制。通过笔刷系统实时编辑地形，导出为 .terrain 格式或 PNG 序列，供 Stride 游戏引擎运行时使用。

## Core Value

实时 3D 预览的笔刷式地形编辑 — 所见即所得的高度和材质编辑体验。

## Requirements

### Validated

- ✓ Terrain Mesh LOD 系统 — existing (Terrain/Rendering/)
- ✓ ImGui UI 框架 — existing (Terrain.Editor/UI/)
- ✓ 分页算法参考 — existing (TerrainPreProcessor/)
- ✓ Brush 参数系统（Size/Strength/Falloff）— Phase 2: Brush System Core
- ✓ Brush 预览光标（圆形 + 衰减显示）— Phase 2: Brush System Core

### Active

- [ ] 高度图编辑笔刷（升降、平滑、展平、噪声扰动）
- [ ] 材质槽绘制笔刷（圆形、方形、噪点 + 衰减羽化）
- [ ] R8 SplatMap 存储与渲染（最多 256 种材质）
- [ ] 双线性混合 + 噪声扰动的材质渲染
- [ ] 文件 I/O（Open File Dialog 打开 PNG，导出 PNG/.terrain）
- [ ] Undo/Redo 系统（用户可配置历史层数）
- [ ] 实时 3D 预览（复用 Terrain LOD）
- [ ] 材质管理（默认材质包、自定义导入、预览缩略图）

### Out of Scope

- 植被/物体刷 — GUI 已预留位置，未来实现
- 其他预留编辑器功能 — 待后续规划

## Context

**技术基础：**
- 基于 Stride Game Engine 4.3.0.2507 (.NET 10)
- 复用 Terrain 项目的 GPU 驱动 LOD 渲染管线
- 复用 TerrainPreProcessor 的分页算法思路
- ImGui.NET 已集成，UI 框架已搭建

**输入输出：**
- 输入：高度图 PNG + 可选 SplatMap PNG（无则创建默认）
- 输出：.terrain 文件 或 PNG 序列

**编辑模式：**
- 高度编辑修改时考虑 page 化（参考 TerrainPreProcessor）
- 编辑阶段不生成 mipmap（仅导出时生成）

## Constraints

- **Tech Stack**: Stride 4.3 + ImGui.NET + .NET 10
- **Memory**: 大高度图需分页处理，避免一次性加载
- **GPU**: 实时预览需复用现有 LOD 系统
- **File Format**: 兼容现有 .terrain 格式

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| R8 SplatMap | 支持 256 种材质，双线性混合效果好 | — Pending |
| 笔刷可导入 | 默认原型笔刷 + 用户自定义扩展 | — Pending |
| 导出双格式 | PNG 便于外部工具，.terrain 直接用于运行时 | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd:transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-03-30 after Phase 2: Brush System Core*
