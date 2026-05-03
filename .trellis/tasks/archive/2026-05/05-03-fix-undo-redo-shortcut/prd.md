# Fix Undo/Redo Shortcut Activation

## Goal

修复 Terrain Editor 中 Undo/Redo 快捷键需要先点击 toolbar 图标后才生效的问题，让 Ctrl+Z、Ctrl+Y、Ctrl+Shift+Z 在编辑器启动后和视口交互期间都能可靠触发对应命令。

## What I already know

* 用户报告：undo/redo 快捷键逻辑有问题，只有第一次点击 toolbar 图标后才起作用。
* `Terrain.Editor/Views/MainWindow.axaml` 已有 `Window.KeyBindings`：Ctrl+Z 绑定 `UndoCommand`，Ctrl+Y / Ctrl+Shift+Z 绑定 `RedoCommand`。
* Toolbar 的 Undo/Redo 按钮直接绑定同一组 ViewModel 命令，点击按钮可用，说明历史事务和命令本体大概率正常。
* `NativeStrideViewportControl` 在获得焦点时调用 `ViewportHost.FocusRuntimeWindow()`。
* `NativeStrideViewportHost` 会通过 Win32 `SetFocus` 把焦点交给 SDL/Stride 原生子窗口。
* 症状符合 Avalonia `Window.KeyBindings` 在启动时或原生子 HWND 持有焦点时收不到键盘输入，点击 toolbar 后焦点回到 Avalonia 才恢复。

## Assumptions

* 本次修复应限制在窗口/输入桥接层，不改 `HistoryManager` 的 undo/redo 事务语义。
* 快捷键应避免在文本输入控件中误触发，至少不能破坏 TextBox 的正常编辑体验。
* 视口仍需要保留现有原生焦点能力，用于 Stride 相机/鼠标输入。

## Requirements

* Ctrl+Z 触发 Undo。
* Ctrl+Y 和 Ctrl+Shift+Z 触发 Redo。
* 快捷键不依赖用户先点击 toolbar 或菜单。
* 修复应兼容 Avalonia + 原生 SDL 子窗口焦点边界。
* 不改变 toolbar/menu 命令绑定和历史栈行为。

## Acceptance Criteria

* [x] 启动编辑器后，不点击 toolbar，执行一次可撤销编辑后 Ctrl+Z 可以撤销。（代码路径已覆盖 SDL 视口焦点；仍建议人工冒烟）
* [x] 撤销后 Ctrl+Y 可以重做。（代码路径已覆盖 SDL 视口焦点；仍建议人工冒烟）
* [x] 撤销后 Ctrl+Shift+Z 可以重做。（代码路径已覆盖 SDL 视口焦点；仍建议人工冒烟）
* [x] 点击/聚焦视口后快捷键仍可触发。（通过 SDL HWND WndProc 桥接）
* [x] toolbar 的 Undo/Redo 按钮仍可触发。（仍使用原 ViewModel 命令）
* [x] `dotnet build Terrain.Editor/Terrain.Editor.csproj` 通过。

## Definition of Done

* Tests added/updated where practical, or documented manual verification when UI/native focus cannot be unit-tested cheaply.
* Build green.
* Specs/notes updated if a reusable focus/input convention is learned.

## Out of Scope

* 重写 Undo/Redo 历史事务模型。
* 改动笔刷事务捕获、chunk replay 或 dirty tracking。
* 重新设计全局快捷键系统。
* 改动 Stride 相机控制快捷键。

## Technical Approach

先保留 XAML `Window.KeyBindings` 作为 Avalonia 焦点路径；在窗口 code-behind 中补一个轻量级按键预处理/桥接逻辑，专门处理 undo/redo 的 Ctrl 组合键，并通过 ViewModel 命令执行。若焦点在 Avalonia 文本输入控件中，则不抢占文本编辑快捷键。若后续确认 SDL 子 HWND 完全吞掉键盘消息，再升级为 Win32 层面的消息桥接，但本次 MVP 先做最小窗口层修复。

## Technical Notes

* Relevant files:
  * `Terrain.Editor/Views/MainWindow.axaml`
  * `Terrain.Editor/Views/MainWindow.axaml.cs`
  * `Terrain.Editor/Views/Controls/NativeStrideViewportControl.cs`
  * `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`
  * `Terrain.Editor/ViewModels/EditorShellViewModel.cs`
* Relevant specs:
  * `.trellis/spec/editor/native-viewport-hosting.md`
  * `.trellis/spec/editor/state-management.md`
  * `.trellis/spec/editor/quality-guidelines.md`

## Bug Analysis

### 1. Root Cause Category

* **Category**: B - Cross-Layer Contract
* **Specific Cause**: Avalonia `Window.KeyBindings` only covers the Avalonia focus/input path. The embedded SDL/Stride viewport owns a separate native child HWND and explicitly takes Win32 focus, so viewport-focused keyboard messages bypass Avalonia bindings.

### 2. Why Previous Fix Was Incomplete

* Adding XAML `Window.KeyBindings` fixed the missing toolbar/menu shortcut path, but did not cover the native viewport focus path.

### 3. Prevention Mechanisms

| Priority | Mechanism | Specific Action | Status |
|----------|-----------|-----------------|--------|
| P0 | Documentation | Record native viewport shortcut focus convention in `.trellis/spec/editor/native-viewport-hosting.md` | DONE |
| P1 | Acceptance Criteria | Require viewport-focused shortcut validation for editor-level shortcuts | DONE |

### 4. Systematic Expansion

* **Similar Issues**: New editor-level shortcuts such as Save/Open/New may also need the same bridge if they must work while the SDL viewport owns focus.
* **Design Improvement**: Keep Avalonia command bindings as the source of behavior; use native input bridging only to forward viewport-focused shortcuts back to existing commands.
