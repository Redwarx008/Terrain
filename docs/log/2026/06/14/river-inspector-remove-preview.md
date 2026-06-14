# River Inspector 移除 Preview
**Date**: 2026-06-14
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 移除 River inspector 中的 preview UI 与对应 ViewModel 状态

**Secondary Objectives:**
- 保留 river 自动生成与宽度缩放行为
- 用文本级测试防止 preview 绑定回流

**Success Criteria:**
- River 面板不再显示 preview
- `RiverViewModel` 不再持有 preview bitmap 状态
- 现有 river 自动生成测试继续通过

---

## Context & Background

**Previous Work:**
- See: [river-preview-lazy-load-regression-fix.md](./river-preview-lazy-load-regression-fix.md)
- See: [river-auto-generation-on-load.md](./river-auto-generation-on-load.md)

**Current State:**
- 前一轮为了规避启动期大图解码回归，把 river preview 改成了按需加载
- 本轮用户明确要求直接去掉 preview，而不是继续保留懒加载版本

**Why Now:**
- River inspector 当前只需要展示已加载资源路径、自动生成结果和宽度缩放
- preview 已经不是必需功能，继续保留只会增加 UI 和状态复杂度

---

## What We Did

### 1. 删除 RiverViewModel 的 preview 状态与懒加载逻辑
**Files Changed:** `Terrain.Editor/ViewModels/RiverViewModel.cs`

**Implementation:**
- 删除 `PreviewImage`
- 删除 `EnsurePreviewLoaded()`
- 删除 bitmap 加载/替换/释放逻辑
- 保留 `RiverMapPath`、`StatusText`、`HasRiverMap`、`WidthScale`

**Rationale:**
- River preview 既不是运行时能力，也不是自动生成链路的必要输入
- 直接删除比继续维护懒加载分支更符合当前产品口径

### 2. 删除 River inspector 中的 preview 区块
**Files Changed:** `Terrain.Editor/Views/MainWindow.axaml`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`

**Implementation:**
- 从 XAML 移除 `River.PreviewImage` 对应的 `<Border>` / `<Image>`
- 从 `EditorShellViewModel.OnSelectedModeChanged()` 删除 `River?.EnsurePreviewLoaded()`

**Rationale:**
- 既然 preview 功能退场，模式切换时不应再触发任何 preview 相关副作用

### 3. 补回归测试并清理旧 preview 测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`, `Terrain.Editor.Tests/RiverViewModelAutoGenerationTests.cs`

**Implementation:**
- 新增文本测试，确保 River inspector 不再绑定 `River.PreviewImage`
- 删除只服务 preview 懒加载的旧测试
- 保留 river 自动生成、清空、宽度缩放重建测试

**Rationale:**
- 这类 UI 绑定回归更适合用源码文本测试快速钉住

---

## Decisions Made

### Decision 1: River inspector 不再保留 preview 能力
**Context:** 现有需求只要求查看当前加载资源和自动生成结果
**Options Considered:**
1. 保留 eager preview - 已知会把大图解码挂进主链
2. 保留 lazy preview - 仍然需要额外 UI、状态和回归保护
3. 完全删除 preview - UI 与状态最简单

**Decision:** Chose Option 3
**Rationale:** 当前功能目标不需要 preview，删除比继续维护更直接
**Trade-offs:** River 模式下不能再直接查看 `rivers.png` 缩略图
**Documentation Impact:** 更新 `docs/CURRENT_FEATURES.md`

---

## Problems Encountered & Solutions

### Problem 1: 需要证明 preview 已彻底退场，而不是只删掉一半
**Symptom:** 仅靠编译通过无法证明 XAML 绑定是否还残留
**Root Cause:** preview 同时存在于 ViewModel、Shell 和 XAML 三层
**Investigation:**
- 查找 `PreviewImage`
- 查找 `EnsurePreviewLoaded`
- 查找 `River.HasRiverMap` 对应旧 preview frame

**Solution:**
- 增加 `EditorWorkflowTextTests` 文本断言
- 断言 `MainWindow.axaml` 不再出现 `River.PreviewImage`
- 断言 `RiverViewModel.cs` 不再出现 `PreviewImage` / `EnsurePreviewLoaded`

**Why This Works:** 可以直接约束最容易回流的绑定与 API 名称
**Pattern for Future:** 对这类纯 UI 绑定退场需求，优先用文本测试钉住源码特征

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/CURRENT_FEATURES.md` - 删除 River preview 的实现描述
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 无需更新，架构边界未变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** UI 退场用文本测试锁回归
- When to use: 删除命令、绑定、菜单项、局部 inspector UI
- Benefits: 成本低，定位直接
- Add to: 本会话日志即可

---

## Code Quality Notes

### Testing
- **Tests Written:** 1 条 River inspector preview 退场文本测试
- **Coverage:** XAML 绑定移除、ViewModel preview API 移除、既有 river 自动生成行为未回归
- **Manual Tests:** 可手动打开 Editor，确认 River 面板只显示路径、状态和 Width Scale

### Technical Debt
- **Created:** 无
- **Paid Down:** 删除无业务价值的 preview 状态和模式切换副作用
- **TODOs:** 仍有一个独立失败测试要求仓库 scaffold 不应提交 `game/map_data/terrain.terrain`

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 收紧 `RiverWorkspaceDiagnosticsTests` 的断言，固定已知稳定计数
2. 统一 `SystemCount` 语义，解决 parse 阶段与生成阶段统计不一致
3. 处理仓库 scaffold 相关的既有失败测试

### Blocked Items
- **Blocker:** 无

### Questions to Resolve
1. `RiverGenerationResult.SystemCount` 应该表示“独立水系数”还是“本次 publish 批次数”

---

## Session Statistics

**Files Changed:** 6
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverViewModel` 已不再持有 preview bitmap
- `MainWindow.axaml` 的 River inspector 已无 preview 区块
- 文本测试会阻止 `River.PreviewImage` / `EnsurePreviewLoaded` 回流

**What Changed Since Last Doc Read:**
- Implementation: River preview 从“懒加载”进一步收口为“完全删除”
- Constraints: River inspector 只保留路径、状态、宽度缩放

**Gotchas for Next Session:**
- `docs/log/2026/06/14/river-preview-lazy-load-regression-fix.md` 现在属于历史记录，不代表当前最终 UI
- 测试套件仍有一个与本次无关的 scaffold 失败

---

## Links & References

### Related Sessions
- [river-preview-lazy-load-regression-fix.md](./river-preview-lazy-load-regression-fix.md)
- [river-auto-generation-on-load.md](./river-auto-generation-on-load.md)

### Code References
- Key implementation: `Terrain.Editor/ViewModels/RiverViewModel.cs`
- River inspector UI: `Terrain.Editor/Views/MainWindow.axaml`
- Regression test: `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

---

## Notes & Observations

- preview 的存在并不是 river 自动生成链路的必要条件
- 这次收口后，River inspector 的职责更清晰：展示加载结果与调参，不负责贴图缩略图浏览

