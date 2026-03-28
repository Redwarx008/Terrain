# Phase 1: Project Foundation - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-29
**Phase:** 01-project-foundation
**Areas discussed:** 地形数据来源, 相机导航模式, 渲染集成方式, LOD 策略

---

## 地形数据来源

| Option | Description | Selected |
|--------|-------------|----------|
| 默认高度图（嵌入式资源） | 内置一个小的测试高度图，直接开始编辑 | |
| 运行时生成平面 | 启动时创建平坦地形，用户稍后导入真实数据 | |
| 从文件加载 | 打开已有项目文件 | ✓ |

**User's choice:** 从文件加载

### 启动行为

| Option | Description | Selected |
|--------|-------------|----------|
| 空场景 + File Open | 先显示空场景，File→Open 导入高度图 | ✓ |
| 自动弹出文件对话框 | 启动时弹出文件选择对话框 | |
| 最近文件记忆 | 如最近有打开文件则自动加载，否则空场景 | |

**User's choice:** 空场景 + File Open

---

## 相机导航模式

| Option | Description | Selected |
|--------|-------------|----------|
| 轨道相机（Orbit） | 围绕一个目标点（地形中心）旋转，适合预览和编辑 | |
| 自由飞行相机 | 自由 WASD 移动 + 鼠标视角，适合大场景漫游 | |
| 混合模式 | 默认轨道，按住键切换自由飞行 | ✓ |

**User's choice:** 混合模式

### 轨道目标

| Option | Description | Selected |
|--------|-------------|----------|
| 自动锁定地形中心 | 轨道中心 = 当前加载的地形边界中心 | |
| 可交互选择焦点 | 轨道中心 = 鼠标点击位置，可 Shift+点击重设 | |
| 用户可调整焦点 | 用户可平移轨道中心，双击重置到地形中心 | ✓ |

**User's choice:** 用户可调整焦点

---

## 渲染集成方式

| Option | Description | Selected |
|--------|-------------|----------|
| ImGui 窗口内嵌 Stride | SceneViewPanel 区域显示 Stride 渲染结果，与 ImGui UI 共存 | ✓ |
| 分离窗口 | 3D 视图在独立窗口，ImGui 作为工具面板 | |

**User's choice:** ImGui 窗口内嵌 Stride

---

## LOD 策略

| Option | Description | Selected |
|--------|-------------|----------|
| 复用现有 LOD 系统 | 复用 Terrain/Rendering/ 的完整 LOD 系统，包括 QuadTree、Streaming、Compute Shaders | ✓ |
| 简化预览（先不启用 LOD） | Phase 1 用简化网格，后续阶段再集成完整 LOD | |

**User's choice:** 复用现有 LOD 系统

---

## Claude's Discretion

- 默认高度图尺寸（如用户未加载文件时的占位）— 可选用小的测试高度图或完全不显示地形
- 相机初始位置和视角 — 自动适配加载的地形边界
- 自由飞行模式切换键 — 选择合适的按键

## Deferred Ideas

None — discussion stayed within phase scope.
