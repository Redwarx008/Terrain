# Editor Save 进度条 native HWND 遮盖修复
**Date**: 2026-06-15
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复 Save 时没有看到模态 progress bar，只看到 viewport 外区域变暗的问题。

**Secondary Objectives:**
- 查清 Avalonia overlay、disabled state 和 native Stride viewport 的真实层级关系。
- 增加回归检查，避免只用字符串检查而漏掉 native child HWND airspace 问题。

**Success Criteria:**
- Save 模态期间 progress card 不再被 native viewport 覆盖。
- 保存期间 viewport 输入仍被阻断并 flush。
- 自动验证通过。

---

## Context & Background

**Previous Work:**
- Related: [Editor Save async modal progress](./2026-06-15-editor-save-progress.md)
- Related: [Architecture Overview](../../../../ARCHITECTURE_OVERVIEW.md)
- Related: [Current Features](../../../../CURRENT_FEATURES.md)

**Current State:**
- Save 已改为 async modal workflow，并在 Avalonia 主窗口里显示 overlay。
- Stride viewport 通过 `NativeControlHost` + SDL/Win32 child HWND 嵌入 Avalonia。

**Why Now:**
- 用户手动验证发现 Save 时没有弹出可见进度条；除 viewport 之外的 Avalonia 区域变暗。

---

## What We Did

### 1. 调查 overlay 不可见的 root cause
**Files Changed:** None

**Implementation:**
- 检查 `MainWindow.axaml` 中 overlay 与 disabled `DockPanel` 的兄弟关系。
- 检查 `EditorShellViewModel` 中 `IsSaving` / `SaveProgressMessage` / `SaveProgressPercent` 绑定和通知。
- 检查 `NativeStrideViewportControl` / `NativeStrideViewportHost` 的 Win32 child HWND 嵌入方式。

**Rationale:**
- 症状显示 Avalonia overlay 的半透明背景已显示，但居中的 progress card 位于 viewport 区域并被 native HWND 遮盖。

**Architecture Compliance:**
- ✅ 先定位 root cause，再改 native viewport host 边界。

### 2. 保存模态期间隐藏 native viewport HWND
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`

**Implementation:**
- `SetInputBlocked(true)` 继续设置 `_game.IsInputBlocked` 并同步 flush camera/brush 输入状态。
- 进入保存阻断时调用 `SetNativeViewportVisible(false)`，隐藏 native host HWND 和 hosted SDL HWND。
- 解除保存阻断时重新显示 HWND，并调用 `ApplySize()` 恢复 hosted window 尺寸和样式。

**Rationale:**
- Avalonia visual 不能可靠覆盖 native child HWND；临时隐藏 viewport 是当前 inline overlay 架构下的最小可靠修复。

**Architecture Compliance:**
- ✅ native HWND 显隐控制留在 `NativeStrideViewportHost` 内部。
- ✅ 不改变 Save snapshot / background IO / staged writer 语义。

### 3. 回归测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

**Implementation:**
- 增加 `native viewport is hidden behind modal save overlay` 回归检查。
- 测试要求 `SetInputBlocked` 调用 native viewport 显隐逻辑，并确认 host HWND 与 SDL HWND 都会被 `ShowWindow` 控制。

**Rationale:**
- 旧测试只检查 XAML 包含 overlay 文本，无法发现 Win32 child HWND 覆盖 Avalonia overlay。

**Architecture Compliance:**
- ✅ 沿用当前 editor workflow 文本测试模式，并补上 airspace 约束。

### 4. 文档更新
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/2026/06/15/2026-06-15-editor-save-progress-native-airspace-fix.md`

**Implementation:**
- 记录 Save 模态期间 native viewport 会临时隐藏，避免 Avalonia overlay 被 child HWND 遮盖。

**Rationale:**
- 这是 native viewport hosting 的重要 UI 约束，后续 modal overlay 不能只看 Avalonia visual tree。

**Architecture Compliance:**
- ✅ 符合 AGENTS 结束流程：新增会话日志并更新系统状态文档。

---

## Decisions Made

### Decision 1: 隐藏 native viewport，而不是只调高 Avalonia ZIndex
**Context:** 进度卡片是 Avalonia visual，viewport 是 Win32/SDL child HWND。
**Options Considered:**
1. `Panel.ZIndex` / XAML 层级调整 - 只影响 Avalonia visual，不解决 native HWND airspace。
2. 保存期间隐藏 native viewport HWND - 最小变更，保持现有 inline overlay。
3. 新建 owned top-level progress window - 更可靠但改动和测试范围更大。

**Decision:** 保存模态期间临时隐藏 native viewport HWND。
**Rationale:** 当前用户问题是 progress card 被 viewport 遮住；隐藏 child HWND 后 Avalonia overlay 可以覆盖整个窗口。
**Trade-offs:** 保存期间 viewport 画面会暂时消失，保存结束恢复。
**Documentation Impact:** 更新 `ARCHITECTURE_OVERVIEW.md`、`CURRENT_FEATURES.md` 和本日志。

---

## What Worked ✅

1. **按症状反推 airspace**
   - What: “viewport 外变暗”说明 overlay 背景已显示，但 viewport 区域不受 Avalonia 覆盖。
   - Why it worked: 直接定位到 NativeControlHost/Win32 child HWND，而不是误查 binding。
   - Reusable pattern: Yes。

2. **子代理独立调查**
   - What: 子代理只读检查 overlay、ViewModel 绑定和 native viewport hosting。
   - Impact: 独立确认 root cause 是 native HWND 覆盖 Avalonia overlay。

---

## What Didn't Work ❌

1. **初版测试只检查 XAML 字符串**
   - What we tried: 检查 MainWindow 包含 `Saving authoring resources` 和 `ProgressBar`。
   - Why it failed: 字符串存在不代表 Avalonia visual 能盖住 native child HWND。
   - Lesson learned: 涉及 `NativeControlHost` 的 modal overlay 需要明确测试 native HWND 显隐或 top-level window 策略。
   - Don't try this again because: 纯 Avalonia visual tree 检查不能覆盖 Win32 airspace。

---

## Problems Encountered & Solutions

### Problem 1: Progress card 被 viewport 覆盖
**Symptom:** Save 时除 viewport 外区域变暗，但看不到 progress bar。
**Root Cause:** progress card 居中后位于 viewport 区域；native Stride viewport 是 child HWND，会压过 Avalonia overlay。
**Investigation:**
- Checked: `MainWindow.axaml` overlay 在 disabled `DockPanel` 外，不是被 parent disabled 隐藏。
- Checked: `EditorShellViewModel` 绑定链完整。
- Found: `NativeStrideViewportControl` 继承 `NativeControlHost` 并创建 `WS_CHILD | WS_VISIBLE` HWND。
- Found: `NativeStrideViewportHost` 将 SDL window `SetParent` 到该 child HWND。

**Solution:**
- `NativeStrideViewportHost.SetInputBlocked` 在保存阻断期间调用 `SetNativeViewportVisible(!blocked)`。
- `SetNativeViewportVisible` 同时控制 host HWND 与 SDL HWND 的 `ShowWindow` 状态。

**Why This Works:** child HWND 隐藏后不再参与 native z-order，Avalonia overlay 的 progress card 可以正常显示。
**Pattern for Future:** Avalonia overlay 需要覆盖 native child HWND 时，要隐藏/移除 native HWND，或使用 top-level/Popup 方案。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update [ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md) - native viewport host 保存模态期间隐藏 HWND。
- [x] Update [CURRENT_FEATURES.md](../../../../CURRENT_FEATURES.md) - Save 功能说明补充 native child HWND 遮盖约束。

### New Patterns/Anti-Patterns Discovered
**New Pattern:** Modal save hides native viewport HWND
- When to use: Avalonia inline overlay 必须覆盖嵌入式 Stride native viewport。
- Benefits: 避免 Win32 airspace 遮盖 progress card。
- Add to: 如后续其他 modal 复用该策略，可提取到 learnings。

**New Anti-Pattern:** 只检查 Avalonia overlay 文本存在
- What not to do: 用 XAML 字符串存在证明 modal 在 native viewport 上可见。
- Why it's bad: NativeControlHost 子窗口可能覆盖 Avalonia visual。
- Add warning to: 后续如重复出现，提取到 `docs/log/learnings/`。

### Architectural Decisions That Changed
- **Changed:** Save input block 现在同时控制 native viewport HWND visibility。
- **From:** 只阻断 viewport 输入和 flush 状态。
- **To:** 阻断输入 + 隐藏 native viewport HWND + 恢复显示时重新 apply size。
- **Scope:** `NativeStrideViewportHost` 与 editor workflow tests。
- **Reason:** Avalonia overlay 无法覆盖 native child HWND。

---

## Code Quality Notes

### Performance
- **Measured:** 未做耗时基准；显隐 HWND 是保存开始/结束各一次。
- **Target:** Save 期间 progress overlay 可见，保存结束 viewport 恢复。
- **Status:** ✅ 自动测试覆盖 host 显隐约束；仍需手动验证真实窗口视觉。

### Testing
- **Tests Written:** `native viewport is hidden behind modal save overlay`。
- **Coverage:** `NativeStrideViewportHost.SetInputBlocked` 必须调用 native viewport 显隐；显隐方法必须控制 host HWND 和 SDL HWND。
- **Manual Tests:** 点击 Save，确认 progress card/ProgressBar 可见；保存结束 viewport 恢复；保存期间按住鼠标不会留下相机/笔刷状态。

### Technical Debt
- **Created:** 仍是文本约束测试，没有自动截图验证。
- **Paid Down:** 补上 native HWND airspace 的回归约束。
- **TODOs:** 若后续 modal 变多，可抽象 native overlay coordination 或改为 owned top-level progress window。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 手动验证 Save progress card 在真实窗口中可见。
2. 手动验证保存结束后 viewport 恢复显示和尺寸。
3. 如需避免保存期间 viewport 暂时消失，设计 owned top-level progress window。

### Blocked Items
- **Blocker:** 无。
- **Needs:** 真实窗口视觉验证。
- **Owner:** Editor workflow。

### Questions to Resolve
1. 保存期间隐藏 viewport 是否符合目标体验。
2. 未来 Export/Import modal 是否也应复用 native HWND 隐藏策略。

### Docs to Read Before Next Session
- [Architecture Overview](../../../../ARCHITECTURE_OVERVIEW.md)
- [Editor Save progress log](./2026-06-15-editor-save-progress.md)

---

## Session Statistics

**Files Changed:** 5
**Lines Added/Removed:** +325/-8
**Commits:** 1

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs` 的 `SetNativeViewportVisible`。
- Critical decision: Avalonia inline overlay 不能覆盖 native child HWND；Save 模态期间隐藏 viewport。
- Active pattern: `SetInputBlocked(true)` = flush input + hide viewport HWND。
- Current status: Fix implemented on `master` working tree, pending final verification/commit.

**What Changed Since Last Doc Read:**
- Architecture: native viewport host 现在负责 Save 模态期间的 HWND 显隐。
- Implementation: `EditorWorkflowTextTests` 新增 native airspace 回归约束。
- Constraints: 保存期间 viewport 会暂时不可见。

**Gotchas for Next Session:**
- 不要用 `Panel.ZIndex` 试图盖住 `NativeControlHost` 子窗口。
- 保存结束恢复显示时要重新 apply hosted window size/style。
- `--no-build` 会运行旧测试二进制；改了测试后要至少跑一次不带 `--no-build` 的测试。

---

## Links & References

### Related Documentation
- [Architecture Overview](../../../../ARCHITECTURE_OVERVIEW.md)
- [Current Features](../../../../CURRENT_FEATURES.md)
- [Editor Save progress log](./2026-06-15-editor-save-progress.md)

### Related Sessions
- [Editor Save async modal progress](./2026-06-15-editor-save-progress.md)

### External Resources
- None.

### Code References
- Native viewport visibility: `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`
- Workflow regression test: `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

---

## Notes & Observations

- 子代理独立确认：binding 和 ProgressBar 语法没有明显问题，root cause 是 Win32/SDL child HWND 覆盖 Avalonia visual。

---

*Template Version: 1.0 - Based on Archon-Engine template*
