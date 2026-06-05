# Viewport 输入焦点与相机移动修复
**Date**: 2026-06-05
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 诊断并修复从其他应用切回 Editor 后，viewport 仍可旋转但无法用 WASD/QE 移动相机的问题。

**Secondary Objectives:**
- 避免修复焦点时破坏 Avalonia UI 的正常焦点恢复。
- 保护活动笔触期间的 Undo/Redo 历史栈一致性。

**Success Criteria:**
- 右键进入相机控制时 SDL runtime window 会重新获得键盘焦点。
- Alt-Tab 后 Stride 键盘状态 stale 时，相机移动仍以真实物理键状态为准。
- 非用户点击/右键控制路径不会无条件抢回 viewport 焦点。
- `dotnet build Terrain.sln` 通过。

---

## Context & Background

**Previous Work:**
- Related: [ADR-011 Avalonia SDL viewport hosting](../../decisions/adr-011-avalonia-sdl-viewport-hosting.md)
- Current project status: [CURRENT_FEATURES](../../../CURRENT_FEATURES.md) 中原生 SDL 视口已完成。

**Current State:**
- Editor 使用 Avalonia `NativeControlHost` 承载 SDL/Stride viewport。
- 相机旋转依赖鼠标 delta，移动依赖 `Input.IsKeyDown(Keys.W/A/S/D/Q/E)`。
- 用户报告：有时候切回 Editor 后 viewport 不能移动摄像机，但可以旋转。

**Why Now:**
- 该问题直接影响 editor navigation，且症状符合嵌入式 SDL 窗口在 Alt-Tab / 焦点切换后键盘输入状态不同步的问题。

---

## What We Did

### 1. 收窄焦点恢复到明确用户导航路径
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`, `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`, `Terrain.Editor/Views/Controls/NativeStrideViewportControl.cs`

**Implementation:**
- `NativeStrideViewportHost` 将 `FocusRuntimeWindow` 注入给 `EmbeddedStrideViewportGame`。
- `EmbeddedStrideViewportGame.UpdateCamera` 在右键进入相机控制时调用 `FocusRuntimeWindow`，确保 SDL runtime window 重新获得键盘焦点。
- `NativeStrideViewportControl` 保留 `OnPointerPressed` 的显式焦点转发。
- 移除了非点击路径的焦点恢复尝试：不再监听 top-level `Activated`，也不再在 `OnGotFocus` 中无条件转发焦点。

**Rationale:**
- 用户点击 viewport 或右键开始导航是明确的 viewport 输入意图，适合把 Win32 焦点转给 SDL。
- Window Activated / Avalonia GotFocus 可能来自文件对话框、属性面板、Tab 导航或窗口恢复；这些路径无条件抢焦点会破坏 UI 输入。

### 2. Windows 右键导航期间使用物理键状态作为移动输入真值
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

**Implementation:**
- 增加 Windows `GetAsyncKeyState` fallback，用于 `W/A/S/D/Q/E/Shift`。
- 只在右键相机控制期间启用 `_preferPhysicalKeyboardState`。
- 启用时不与 Stride `Input.IsKeyDown` 做 OR，而是直接以 Win32 物理键状态为准。

**Rationale:**
- Alt-Tab 场景下 Stride/SDL 可能漏掉 key-up 或 key-down，导致键盘状态 stale。
- 如果用 OR 合并，只能修复 stale-false，不能修复更危险的 stale-true（相机持续误移动）。右键导航期间以物理键状态为准能同时处理 stale-false 和 stale-true。

### 3. 活动命令期间禁用 Undo/Redo
**Files Changed:** `Terrain.Editor/Services/Commands/HistoryManager.cs`

**Implementation:**
- `CanUndo` / `CanRedo` 增加 `activeCommand == null` 条件。
- `BeginCommand` 发出 `HistoryChangeType.CommandStarted`，让 UI 即时刷新 Undo/Redo 可执行状态。
- `CancelCommand` 与 no-op commit 发出 `CommandCanceled`，让 UI 恢复状态。

**Rationale:**
- code review 指出 viewport 快捷键进入后，活动笔触期间按 Ctrl+Z/Y 可能让历史栈与未提交 stroke 快照错位。
- 在 `HistoryManager` 层统一禁止活动命令期间 undo/redo，比只在 viewport shortcut 入口判断更安全。

---

## Decisions Made

### Decision 1: 不在 Window Activated / GotFocus 自动抢回 viewport 焦点
**Context:** 初始修复尝试考虑在窗口重新激活时强制 `SetFocus(SDL hwnd)`。

**Options Considered:**
1. Window Activated 时总是恢复 SDL 焦点。
2. Avalonia GotFocus 时总是恢复 SDL 焦点。
3. 只在用户点击 viewport 或右键进入相机控制时恢复 SDL 焦点。

**Decision:** 选择选项 3。
**Rationale:** 只有点击/右键导航表达了明确 viewport 输入意图；Activated/GotFocus 路径可能来自 UI 控件焦点恢复。
**Trade-offs:** 用户切回后若不点击或右键进入相机控制，不会主动抢 viewport 焦点；这是期望行为。
**Documentation Impact:** 本 session log 记录；架构状态未变化，无需更新 ARCHITECTURE_OVERVIEW。

### Decision 2: 右键导航期间用 Win32 物理键状态覆盖 Stride 键盘状态
**Context:** bug 症状表明鼠标相对输入恢复，但键盘状态没有可靠进入 Stride InputManager。

**Options Considered:**
1. 只额外调用 `SetFocus`。
2. 将 Stride 键盘状态与 `GetAsyncKeyState` OR 合并。
3. Windows 右键导航期间直接以 `GetAsyncKeyState` 为准。

**Decision:** 选择选项 3。
**Rationale:** 只 SetFocus 不一定修复已 stale 的 InputManager 状态；OR 合并无法修复 stale-true；物理键覆盖能处理两种 stale。
**Trade-offs:** 该 fallback 是 Windows-specific；项目当前目标为 `net10.0-windows` / `win-x64`，且代码有 OS guard。

---

## What Worked ✅

1. **从“能旋转但不能移动”拆分输入源**
   - What: 对比旋转依赖鼠标 delta、移动依赖键盘状态。
   - Why it worked: 直接把问题定位到键盘焦点/键盘状态，而非相机数学或渲染问题。
   - Reusable pattern: Yes

2. **代码审查驱动修复收敛**
   - What: 两轮 code-reviewer 发现 stale-true 与无条件抢焦点副作用。
   - Impact: 最终实现从“广泛抢焦点”收敛为“明确用户导航意图时恢复焦点”。

---

## What Didn't Work ❌

1. **窗口 Activated 自动恢复 SDL 焦点**
   - What we tried: 在 top-level Window Activated 时调用 viewport host 焦点恢复。
   - Why it failed: 会在从对话框/面板/其他 UI 控件返回时抢走 Avalonia 焦点。
   - Lesson learned: 嵌入式 viewport 的焦点恢复必须绑定明确 viewport 输入意图。
   - Don't try this again because: 会破坏 Editor 其他 UI 的键盘输入和快捷键链路。

2. **Stride 键状态与 Win32 键状态 OR 合并**
   - What we tried: `moveForward |= IsVirtualKeyDown(VkW)`。
   - Why it failed: 只能修复 stale-false，不能修复漏掉 key-up 导致的 stale-true。
   - Lesson learned: 当 fallback 目标是修复 stale input state 时，需要选择可信来源作为 truth source，而不是简单合并。
   - Don't try this again because: stale-true 会导致相机持续误移动。

---

## Problems Encountered & Solutions

### Problem 1: 切回 Editor 后鼠标输入恢复但键盘移动失效
**Symptom:** viewport 可右键旋转，但 WASD/QE 不移动。

**Root Cause:**
- 旋转来自 `Input.AbsoluteMouseDelta`；移动来自 `Input.IsKeyDown`。
- 嵌入式 SDL/Stride viewport 在焦点切换后可能恢复相对鼠标输入，但键盘焦点或 Stride 键盘状态没有可靠恢复。

**Solution:**
- 右键进入相机控制时恢复 SDL runtime window 焦点。
- Windows 右键导航期间用 `GetAsyncKeyState` 读取物理键盘状态。

**Why This Works:**
- 焦点恢复提高 SDL 收到键盘消息的概率。
- 物理键状态 fallback 避免 Stride InputManager stale 状态影响相机移动。

**Pattern for Future:**
- 对嵌入式 native viewport，鼠标相对输入和键盘输入要分开诊断；“鼠标可用”不代表“键盘焦点正确”。

### Problem 2: 活动 stroke 期间 viewport 快捷键可能破坏历史栈
**Symptom:** code review 发现 Ctrl+Z/Y 可从 SDL WndProc 转发到 ViewModel，而不检查是否存在 active stroke。

**Root Cause:**
- `HistoryManager` 原本只按 undo/redo 栈数量判断 `CanUndo/CanRedo`，没有考虑 `activeCommand`。

**Solution:**
- `CanUndo/CanRedo` 需要 `activeCommand == null`。
- command start/cancel/no-op commit 都发出 history changed，刷新 UI command state。

**Why This Works:**
- 活动笔触未提交前，任何 undo/redo 都会被统一拒绝，避免 before/after 快照跨历史边界。

---

## Architecture Impact

### Documentation Updates Required
- [x] Create this session log.
- [x] No update to `ARCHITECTURE_OVERVIEW.md` required: 系统状态未变化。
- [x] No ADR required: 这是现有 Avalonia/SDL viewport hosting 的 bug fix，不是新架构决策。

### New Patterns/Anti-Patterns Discovered
**New Pattern:** Explicit-input focus restoration for embedded native viewport
- When to use: Avalonia/WPF/WinForms host 中嵌入 SDL/Stride/其他 native viewport。
- Benefits: 避免窗口激活/控件焦点恢复时抢走普通 UI 焦点。
- Add to: 若后续 viewport 焦点问题重复出现，可沉淀到 `docs/log/learnings/`。

**New Anti-Pattern:** OR-merging stale input fallback
- What not to do: 用 `logicalInput |= physicalInput` 修复 stale 键盘状态。
- Why it's bad: 无法修复 stale-true，可能导致持续移动。
- Add warning to: 若再次遇到 input stale，可沉淀到 Common Mistakes。

---

## Code Quality Notes

### Testing
- **Tests Written:** 无自动测试。该 bug 依赖 Win32 focus / SDL / Stride runtime 交互，当前没有 headless seam。
- **Manual Tests:** 建议手动验证：
  1. 打开 Editor，在 viewport 右键 + WASD 移动。
  2. Alt-Tab 切出，再切回。
  3. 右键旋转同时按 WASD/QE，确认可移动。
  4. 在属性面板/文本输入/菜单等 UI 控件切回后，确认 viewport 不会自动抢键盘焦点。
  5. 绘制笔触期间按 Ctrl+Z/Y，确认不会执行 Undo/Redo；松开提交后恢复。

### Verification
- `dotnet build Terrain.sln` ✅ PASS
- 构建仍有既有 NuGet vulnerability warnings 与少量 nullable/unused/WFO warnings；本次未处理。
- code-reviewer 最终复查：无阻塞问题。

### Technical Debt
- **Created:** Win32 `GetAsyncKeyState` fallback 与 viewport 输入耦合；但范围限制在 Windows 右键导航期间。
- **Paid Down:** 活动 command 期间 undo/redo 现在有 HistoryManager 层保护。
- **TODOs:** 后续若要跨平台支持，需要为非 Windows viewport input stale 建立对应 fallback 或升级 SDL/Stride input integration。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 手动运行 Editor 验证 Alt-Tab 后 viewport camera movement 是否恢复。
2. 手动验证 UI 焦点：从文件对话框、右侧面板、文本输入返回时 viewport 不应抢焦点。
3. 若仍复现，增加临时 `[DEBUG-viewport-input]` 输出：`Window.Focused`、Stride key state、Win32 key state、右键控制状态。

### Questions to Resolve
1. 是否需要为 viewport camera/input 建立可人工驱动的 HITL 测试脚本？
2. 是否应将 HistoryManager 的 active command 状态暴露为只读属性，方便 UI/调试显示？

### Docs to Read Before Next Session
- [ADR-011 Avalonia SDL viewport hosting](../../decisions/adr-011-avalonia-sdl-viewport-hosting.md)

---

## Session Statistics

**Files Changed:** 4
**Lines Added/Removed:** +76/-17
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Root cause hypothesis: Alt-Tab/focus switching can leave Stride keyboard input stale while mouse relative input still works.
- Key implementation: `EmbeddedStrideViewportGame.UpdateCamera` restores runtime focus on right-drag control start and uses Win32 physical key state during Windows right-drag navigation.
- Focus guard: `NativeStrideViewportControl` should not restore SDL focus from `OnGotFocus` or Window Activated; keep focus transfer tied to explicit pointer press / camera control.
- History guard: `HistoryManager.CanUndo/CanRedo` are false while `activeCommand != null`.

**What Changed Since Last Doc Read:**
- Viewport camera movement now has Windows physical-key fallback during right mouse navigation.
- Undo/Redo is disabled while a brush/history command is active.

**Gotchas for Next Session:**
- Do not reintroduce Window Activated or `OnGotFocus` viewport focus transfer without a “viewport was last active” guard.
- Do not OR-merge stale key fallback; physical key state must override Stride state during fallback mode.
- Build warnings are pre-existing and not caused by this fix.

---
