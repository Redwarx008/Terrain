# ADR-011: Avalonia + SDL 原生视口宿主

**Date**: 2026-04-23
**Status**: ✅ Accepted
**Decision ID**: ADR-011

---

## Context

编辑器需要从 ImGui 迁移到 Avalonia UI 框架。核心挑战是如何将 Stride 渲染视口嵌入 Avalonia 窗口。

## Decision

采用 Avalonia `NativeControlHost` + SDL `GameFormSDL` + Win32 `SetParent` 原生子窗口方案：

- SDL 创建独立窗口（`GameFormSDL`），通过 `SetParent` 重挂接到 Avalonia 宿主窗口
- 放弃共享纹理（SharedTexture）路线
- 使用 CommunityToolkit.Mvvm 的 `ObservableObject` + `ObservableProperty` + `RelayCommand` 构建 ViewModel 层

## Options Considered

### Option 1: SharedTexture 共享纹理
在 Stride 端渲染到纹理，通过共享句柄传递给 Avalonia/Image 控件。

**Pros:** 纯 Avalonia 布局，无窗口边界问题
**Cons:** 输入处理复杂，DPI 缩放问题，性能开销

### Option 2: NativeControlHost + SDL（选中）
SDL 创建原生窗口，嵌入 Avalonia 容器。

**Pros:** 直接渲染路径，输入自然处理，性能最佳
**Cons:** 窗口生命周期管理复杂

### Option 3: CreateWindowFrom(existing HWND)
复用 Avalonia 创建的 HWND，让 SDL 绑定。

**Pros:** 代码最简单
**Cons:** `Window.ClientBounds` 退化为 1x1，导致黑屏（已验证失败）

## Rationale

共享纹理路线有严重的输入和 DPI 问题。`CreateWindowFrom` 路线会导致视口黑屏。NativeControlHost + SetParent 是唯一稳定工作的方案。

## Trade-offs

**What we gain:**
- 稳定的渲染输出
- 输入直接通过 SDL 窗口处理
- 最佳性能（零拷贝）

**What we give up:**
- SDL 窗口焦点抢占导致 Avalonia KeyBindings 失效
- 需要手动转发 Ctrl+Z/Y 到 ViewModel 命令
- 跨平台兼容性需要额外工作

## Implementation Notes

- 焦点问题通过 Win32 `SetFocus` + WndProc 按键转发解决
- 调试方法：先做 presenter-only 对照实验，分层隔离宿主链/presenter链/场景链
- 详见 [learnings/debug-retrospective-sdl-viewport.md](../learnings/debug-retrospective-sdl-viewport.md)

## References

- [开发时间线](../development-timeline.md)
- [SDL 视口调试回顾](../learnings/debug-retrospective-sdl-viewport.md)

---

*ADR Version: 1.0*