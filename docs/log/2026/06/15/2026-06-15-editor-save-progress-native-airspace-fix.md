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

### 2. 使用 owned top-level progress window
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Views/MainWindow.axaml`, `Terrain.Editor/Views/MainWindow.axaml.cs`, `Terrain.Editor/Views/SaveProgressWindow.axaml`, `Terrain.Editor/Views/SaveProgressWindow.axaml.cs`, `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`

**Implementation:**
- `SetInputBlocked(true)` 继续设置 `_game.IsInputBlocked` 并同步 flush camera/brush 输入状态。
- 移除保存期间隐藏 native viewport HWND 的尝试，避免暴露白色 native host / SDL window。
- 新增 `SaveProgressWindow`，作为 owned top-level window 显示保存标题、进度文字、`ProgressBar` 和百分比。
- `MainWindow` 监听 `EditorShellViewModel.IsSaving`，保存开始时 `_saveProgressWindow.Show(this)`，保存结束时关闭窗口。
- `MainWindow.axaml` 只保留保存 dimmer，不再承载 inline progress card，避免重复或被 native child HWND 覆盖。
- `Save` 在 `BeginSaveProgress()` 后先 `await Task.Yield()`，让进度窗口在同步 snapshot 捕获前有机会绘制首帧。

**Rationale:**
- Avalonia inline visual 不能可靠覆盖 native child HWND；owned top-level window 位于 native child HWND 之上，是更可靠的进度条承载方式。

**Architecture Compliance:**
- ✅ progress window 生命周期留在 `MainWindow` code-behind，ViewModel 仍只暴露保存状态和进度数据。
- ✅ 不改变 Save snapshot / background IO / staged writer 语义。

### 3. 回归测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

**Implementation:**
- 增加 `save progress uses owned top-level window` 回归检查。
- 测试要求存在 `SaveProgressWindow.axaml`，`MainWindow` 持有窗口生命周期，并通过 `.Show(this)` 作为 owned window 显示。
- 测试要求主窗口只负责 dimmer，不再内嵌 progress card；同时要求 Save 打开进度后先 yield，再捕获 authoring snapshot。

**Rationale:**
- 旧测试只检查 XAML 包含 overlay 文本，无法发现 Win32 child HWND 覆盖 Avalonia overlay。

**Architecture Compliance:**
- ✅ 沿用当前 editor workflow 文本测试模式，并补上 airspace 约束。

### 4. 文档更新
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/2026/06/15/2026-06-15-editor-save-progress-native-airspace-fix.md`

**Implementation:**
- 记录 Save 模态进度使用 owned top-level window，避免 Avalonia inline overlay 被 child HWND 遮盖。

**Rationale:**
- 这是 native viewport hosting 的重要 UI 约束，后续 modal overlay 不能只看 Avalonia visual tree。

**Architecture Compliance:**
- ✅ 符合 AGENTS 结束流程：新增会话日志并更新系统状态文档。

---

## Decisions Made

### Decision 1: 使用 owned top-level window，而不是 inline overlay 或隐藏 native viewport
**Context:** 进度卡片是 Avalonia visual，viewport 是 Win32/SDL child HWND。
**Options Considered:**
1. `Panel.ZIndex` / XAML 层级调整 - 只影响 Avalonia visual，不解决 native HWND airspace。
2. 保存期间隐藏 native viewport HWND - 初看改动小，但实测会暴露白色 native host / SDL window。
3. 新建 owned top-level progress window - 更可靠，能显示在 native child HWND 之上。

**Decision:** Save 进度条使用 owned top-level `SaveProgressWindow`。
**Rationale:** 当前用户问题是 progress card 被 native child HWND 遮住；owned top-level window 不依赖 Avalonia inline visual 覆盖 native child HWND。
**Trade-offs:** 进度条是一个独立无边框窗口，由 `MainWindow` 负责生命周期。
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

2. **保存期间隐藏 native viewport HWND**
   - What we tried: 在 `SetInputBlocked(true)` 时隐藏 native host HWND 与 hosted SDL HWND。
   - Why it failed: 用户截图显示仍有白色 `Terrain Editor Viewport` native window/host 区域压在 overlay 上。
   - Lesson learned: 对复杂 SDL/NativeControlHost 嵌套层，隐藏部分 HWND 不能保证移除所有 airspace。
   - Don't try this again because: 容易暴露宿主白底或 SDL window chrome，且仍不能保证 progress card 可见。

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
- `MainWindow` 监听 `IsSaving` 并显示 owned top-level `SaveProgressWindow`。
- `SaveProgressWindow` 绑定 `SaveProgressMessage` 和 `SaveProgressPercent`。
- `EditorShellViewModel.Save` 在开启进度后先 `await Task.Yield()`，避免同步 snapshot 捕获挡住进度窗口首帧绘制。
- `NativeStrideViewportHost.SetInputBlocked` 只负责输入阻断和 flush，不再负责隐藏 HWND。

**Why This Works:** owned top-level window 不在 native child HWND 的同一 Avalonia visual airspace 内，可显示在 child HWND 之上。
**Pattern for Future:** Avalonia overlay 需要覆盖 native child HWND 时，优先使用 owned top-level window 或经验证的 popup，不要依赖 inline overlay。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update [ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md) - Save progress 使用 owned top-level window。
- [x] Update [CURRENT_FEATURES.md](../../../../CURRENT_FEATURES.md) - Save 功能说明补充 native child HWND 遮盖约束。

### New Patterns/Anti-Patterns Discovered
**New Pattern:** Modal save progress uses owned top-level window
- When to use: Avalonia 进度 UI 必须显示在嵌入式 Stride native viewport 之上。
- Benefits: 避免 Win32 airspace 遮盖 progress card。
- Add to: 如后续其他 modal 复用该策略，可提取到 learnings。

**New Anti-Pattern:** 只检查 Avalonia overlay 文本存在
- What not to do: 用 XAML 字符串存在证明 modal 在 native viewport 上可见。
- Why it's bad: NativeControlHost 子窗口可能覆盖 Avalonia visual。
- Add warning to: 后续如重复出现，提取到 `docs/log/learnings/`。

### Architectural Decisions That Changed
- **Changed:** Save progress card 从 inline overlay 改为 owned top-level window。
- **From:** Avalonia inline overlay 承载 progress card。
- **To:** `MainWindow` 管理 `SaveProgressWindow` owned top-level window；`MainWindow.axaml` inline overlay 只保留 Avalonia 区域 dimmer。
- **Scope:** `NativeStrideViewportHost` 与 editor workflow tests。
- **Reason:** Avalonia inline overlay 无法覆盖 native child HWND。

---

## Code Quality Notes

### Performance
- **Measured:** 未做耗时基准；owned window 只在保存开始/结束创建和关闭。
- **Target:** Save 期间 progress window 可见，保存结束自动关闭。
- **Status:** ✅ 自动测试覆盖 owned top-level window 约束；仍需手动验证真实窗口视觉。

### Testing
- **Tests Written:** `save progress uses owned top-level window`。
- **Coverage:** `SaveProgressWindow.axaml` 存在并绑定进度；`MainWindow` 监听 `IsSaving` 并通过 `.Show(this)` 显示 owned window；Save 在 snapshot 前 yield 给 UI loop。
- **Manual Tests:** 点击 Save，确认 progress card/ProgressBar 可见；保存结束 viewport 恢复；保存期间按住鼠标不会留下相机/笔刷状态。

### Technical Debt
- **Created:** 仍是文本约束测试，没有自动截图验证。
- **Paid Down:** 补上 native HWND airspace 的回归约束。
- **TODOs:** 若后续 modal 变多，可抽象 native overlay coordination / owned window 管理。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 手动验证 Save progress card 在真实窗口中可见。
2. 手动验证保存结束后 progress window 自动关闭。
3. 若需要全窗口包含 viewport 一起变暗，设计 owned dimmer window 或统一 modal overlay coordinator。

### Blocked Items
- **Blocker:** 无。
- **Needs:** 真实窗口视觉验证。
- **Owner:** Editor workflow。

### Questions to Resolve
1. owned top-level progress window 是否符合目标体验。
2. 未来 Export/Import modal 是否也应复用 owned top-level window 策略。

### Docs to Read Before Next Session
- [Architecture Overview](../../../../ARCHITECTURE_OVERVIEW.md)
- [Editor Save progress log](./2026-06-15-editor-save-progress.md)

---

## Session Statistics

**Files Changed:** 10
**Lines Added/Removed:** +349/-132
**Commits:** 2

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/Views/SaveProgressWindow.axaml` and `Terrain.Editor/Views/MainWindow.axaml.cs`。
- Critical decision: Avalonia inline overlay 不能覆盖 native child HWND；Save 进度使用 owned top-level window。
- Active pattern: `IsSaving=true` = disable shell + block viewport input + show owned progress window + yield once before synchronous snapshot。
- Current status: Fix implemented on `master` working tree, pending final verification/commit.

**What Changed Since Last Doc Read:**
- Architecture: `MainWindow` 负责 Save progress owned window lifecycle。
- Implementation: `EditorWorkflowTextTests` 新增 owned top-level progress window 回归约束。
- Constraints: inline overlay 不能作为 native viewport 上方的唯一可见进度 UI。

**Gotchas for Next Session:**
- 不要用 `Panel.ZIndex` 试图盖住 `NativeControlHost` 子窗口。
- 如果需要覆盖 native viewport，使用 owned top-level window；不要隐藏部分 HWND 作为主要方案。
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
- Save progress window: `Terrain.Editor/Views/SaveProgressWindow.axaml`
- Save progress lifecycle: `Terrain.Editor/Views/MainWindow.axaml.cs`
- Workflow regression test: `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

---

## Notes & Observations

- 子代理独立确认：binding 和 ProgressBar 语法没有明显问题，root cause 是 Win32/SDL child HWND 覆盖 Avalonia visual。

---

*Template Version: 1.0 - Based on Archon-Engine template*
