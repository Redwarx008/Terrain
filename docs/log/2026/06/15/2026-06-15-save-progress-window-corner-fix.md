# Save 进度窗口方角背景修复
**Date**: 2026-06-15
**Session**: 3
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 修复 Save progress modal 出现“方形窗口背景里套圆角卡片”，以及透明尝试后四角变黑的问题。

**Success Criteria:**
- Save progress window 不再显示圆角透明区域或黑色窗口角。
- 自动测试覆盖该约束。

---

## Context & Background

**Previous Work:**
- Related: [Editor Save progress native airspace fix](./2026-06-15-editor-save-progress-native-airspace-fix.md)

**Current State:**
- Save progress 已改为 owned top-level `SaveProgressWindow`，可以显示在 native viewport 之上。
- 用户截图显示窗口外层仍是矩形背景，内部 `Border CornerRadius=6` 形成方角包圆角的双层外观。
- 增加 `TransparencyLevelHint="Transparent"` 后，用户再次截图显示四角变黑，说明当前 Win32/Avalonia 路径下透明 top-level alpha 不可靠。

**Why Now:**
- 进度窗口必须可靠显示，不应依赖在当前环境表现不稳定的透明圆角窗口。

---

## What We Did

### 1. 改为不透明矩形 progress window
**Files Changed:** `Terrain.Editor/Views/SaveProgressWindow.axaml`

**Implementation:**
- 移除 `TransparencyLevelHint="Transparent"`。
- 将 `Window Background` 设为 `{DynamicResource EditorSurfaceBrush}`。
- 移除内部 `Border CornerRadius` 和 `BoxShadow`，避免透明边角和裁剪阴影。

**Rationale:**
- 透明 top-level 窗口在当前环境把角落渲染成黑色；不透明矩形是更稳定的 modal 表现，且不会再出现方角包圆角。

### 2. 回归测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

**Implementation:**
- 在 `save progress uses owned top-level window` 中要求：
  - 不包含 `TransparencyLevelHint`；
  - 不包含 `CornerRadius`；
  - 不包含 `BoxShadow`；
  - 使用不透明 `EditorSurfaceBrush` 作为窗口背景。

**Rationale:**
- 当前可靠方案是避免透明边角，而不是继续追逐平台相关的透明合成。

### 3. 日志修正
**Files Changed:** `docs/log/2026/06/15/2026-06-15-editor-save-progress-native-airspace-fix.md`

**Implementation:**
- 修正上一条日志的文件统计：本次 native airspace fix 实际改动 12 个文件。

---

## Decisions Made

### Decision 1: 放弃透明圆角，使用不透明矩形窗口
**Context:** 进度窗口当前用内部圆角 `Border`，透明尝试在用户环境中产生黑角。
**Options Considered:**
1. 继续调透明 top-level window - 可能依赖平台/驱动/窗口合成，已实测黑角。
2. 去掉圆角和阴影，使用不透明矩形 top-level window - 稳定，视觉更朴素。

**Decision:** 使用不透明矩形 `SaveProgressWindow`。
**Rationale:** Save progress 首要目标是可靠可见；矩形窗口避免透明合成黑角和方角包圆角。

---

## What Worked ✅

1. **文本测试锁住 UI 约束**
   - What: 检查 progress window 不使用透明、圆角或裁剪阴影。
   - Why it worked: 能防止后续重新引入黑角/方角包圆角问题。

---

## Problems Encountered & Solutions

### Problem 1: 方形窗口背景里套圆角卡片
**Symptom:** Save modal 外层是矩形，内部卡片是圆角。
**Root Cause:** owned top-level 窗口的实际形状仍是矩形，内部 `Border CornerRadius` 形成双层边角。
**Solution:** 移除内部圆角，窗口本身使用不透明 surface 背景。
**Pattern for Future:** 不确定平台透明支持时，不要在 top-level modal 内做圆角透明边角。

### Problem 2: 透明尝试后四角变黑
**Symptom:** 添加 `TransparencyLevelHint="Transparent"` 后，modal 四角显示黑色。
**Root Cause:** 当前 Win32/Avalonia/native viewport 组合下 top-level 透明 alpha 合成不可靠；透明区域被黑色背景填充或阴影裁剪污染。
**Solution:** 移除 `TransparencyLevelHint`、`CornerRadius`、`BoxShadow`，使用不透明矩形窗口。
**Pattern for Future:** 对可靠性优先的 editor modal，不要依赖透明 top-level corner。

---

## Architecture Impact

### Documentation Updates Required
- [x] 创建本会话日志。

### New Patterns/Anti-Patterns Discovered
**New Pattern:** Opaque owned modal window
- When to use: 需要稳定覆盖 native child HWND，且透明 top-level window 在目标环境表现不可靠。
- Benefits: 避免系统窗口背景、黑角、阴影裁剪等平台相关问题。

**New Anti-Pattern:** Transparent rounded top-level modal without visual validation
- What not to do: 仅凭 `TransparencyLevelHint="Transparent"` 假设透明圆角可用。
- Why it's bad: 可能在用户环境渲染为黑角。

---

## Code Quality Notes

### Testing
- **Tests Written:** 文本回归约束不使用 `TransparencyLevelHint` / `CornerRadius` / `BoxShadow`，并使用不透明 surface 背景。
- **Coverage:** Save progress modal 的稳定矩形窗口配置。
- **Manual Tests:** 点击 Save，确认不再出现方形包圆角或黑角。

---

## Session Statistics

**Files Changed:** 3
**Lines Added/Removed:** +52/-36
**Commits:** 1

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `SaveProgressWindow` 是无边框 owned top-level window；当前使用不透明矩形背景，避免透明圆角黑角。

**Gotchas for Next Session:**
- 不要重新引入 `TransparencyLevelHint` / `CornerRadius` / `BoxShadow`，除非先做真实窗口视觉验证。

---

*Template Version: 1.0 - Based on Archon-Engine template*
