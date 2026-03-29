# Phase 2: Brush System Core - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-29
**Phase:** 02-brush-system-core
**Areas discussed:** 参数存储, 视口预览, Size范围, Strength映射, Falloff逻辑

---

## 参数存储方式

| Option | Description | Selected |
|--------|-------------|----------|
| 全局单一状态 | 所有工具共享同一组笔刷参数，简单直接 | ✓ |
| 每工具独立状态 | 每个工具有独立参数，切换工具时保留各自设置 | |
| 预设系统 | 支持保存和加载笔刷预设 | |

**User's choice:** 全局单一状态
**Notes:** 简单直接，适合当前阶段需求

---

## 视口预览方式

| Option | Description | Selected |
|--------|-------------|----------|
| 显示圆形光标指示器 | 鼠标在视口中移动时显示圆形轮廓，表示笔刷大小和衰减区域 | ✓ |
| 仅显示默认光标 | 使用标准鼠标光标，不显示笔刷范围预览 | |

**User's choice:** 显示圆形光标指示器
**Notes:** 用户需要在编辑前看到笔刷作用范围

---

## 笔刷大小范围

| Option | Description | Selected |
|--------|-------------|----------|
| 当前 UI 值 | 默认 50，范围 1-500 | |
| 更大范围 | 默认 100，范围 10-1000 | |
| 更小范围 | 默认 30，范围 1-200，适合精细编辑 | ✓ |

**User's choice:** 更小范围 → 默认 30，范围 1-200
**Notes:** 适合精细编辑，避免误操作大范围修改

---

## 强度映射方式

| Option | Description | Selected |
|--------|-------------|----------|
| 0-1 线性 | 线性映射到实际编辑强度，直观 | ✓ |
| 0-1 指数 | 指数映射，在低强度区域更精细控制 | |

**User's choice:** 0-1 线性
**Notes:** 直观易懂，符合用户预期

---

## Falloff 逻辑方向

| Option | Description | Selected |
|--------|-------------|----------|
| 0=硬边，1=软边 | 当前 UI 逻辑，值越大越软 | |
| 1=硬边，0=软边 | 符合 Photoshop 等工具习惯 | ✓ |

**User's choice:** 1=硬边，0=软边（需反转当前逻辑）
**Notes:** 符合主流图像编辑软件的习惯，用户更熟悉

---

## Claude's Discretion

- 笔刷预览圆形的具体渲染样式
- 预览圆形是否跟随地形高度起伏

## Deferred Ideas

- Square 和 Noise 笔刷形状 — Phase 5
- 笔刷预设保存/加载 — 未来功能
- 每工具独立参数 — 可后续添加
