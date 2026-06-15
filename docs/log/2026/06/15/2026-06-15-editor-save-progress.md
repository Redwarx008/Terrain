# Editor Save 异步模态进度
**Date**: 2026-06-15
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Editor Save 添加模态进度，避免保存大图资源时看起来卡死。

**Secondary Objectives:**
- 保存期间阻断会修改编辑器状态的命令。
- 保存期间阻断嵌入式 Stride native viewport 输入。
- 保持作者态资源写回的事务化 staged writer 语义。

**Success Criteria:**
- Save 写回作者态资源时持续报告进度。
- 保存期间不能触发会修改状态的命令。
- Avalonia overlay 之外，原生 Stride HWND 也不能继续接收相机/笔刷输入。
- 自动验证通过。

---

## Context & Background

**Previous Work:**
- See: [Architecture Overview](../../../../ARCHITECTURE_OVERVIEW.md)
- Related: [Current Features](../../../../CURRENT_FEATURES.md)
- Related: [Design](../../../../superpowers/specs/2026-06-15-editor-save-progress-design.md)
- Related: [Plan](../../../../superpowers/plans/2026-06-15-editor-save-progress.md)

**Current State:**
- Editor 已有自动作者态资源 bootstrap、pending heightmap 分支、事务化作者态资源保存。
- Save 原本是同步 UI 命令，保存大图或多资源时窗口看起来卡住。

**Why Now:**
- 用户报告 Editor 的 Save 会将进程卡住，需要进度条反馈并避免保存期间继续编辑。

---

## What We Did

### 1. Save 进度模型与异步写回
**Files Changed:** `Terrain.Editor/Services/Resources/AuthoringSaveProgress.cs`, `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs`, `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`, `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`

**Implementation:**
- 新增 `AuthoringSaveProgress`，按保存阶段报告准备、校验、各 writer 写入、commit、刷新状态。
- 新增 `EditorAuthoringSaveSnapshot`，由 `TerrainManager.CreateAuthoringSaveSnapshot` 在 UI 线程捕获高度图、biome mask、材质和规则状态的不可变快照。
- `EditorShellViewModel.Save` 改为 async 流程：先做 UI 线程快照，再通过后台任务执行作者态资源文件写入。
- `EditorResourceSaveService` 保留事务化 staged writer 语义，并接入进度上报。

**Rationale:**
- 用户需要看到 Save 仍在推进；后台写文件可避免 UI 消息循环被长时间占用。
- 快照先行可以避免后台线程直接读取可变编辑器状态。

**Architecture Compliance:**
- ✅ 保持 virtual resource / staged writer 架构。
- ✅ 仍由 Editor 负责作者态资源保存，不改变 runtime bootstrap 边界。

### 2. 模态 overlay 与命令门禁
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Views/MainWindow.axaml`

**Implementation:**
- Save 期间设置 `IsSaving`、`SaveProgressMessage`、`SaveProgressPercent`，在主窗口显示 modal overlay。
- Save/Export/Import/Undo/Redo/资源修改等可变更命令共用保存门禁，保存期间不可执行。

**Rationale:**
- Save 是不可取消事务流程；保存期间继续执行可变更命令会让 UI 状态和写回快照产生语义冲突。

**Architecture Compliance:**
- ✅ 复用 ViewModel 命令门禁和 Avalonia 绑定，不引入新的全局状态管理。

### 3. 原生视口输入阻断与 flush
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`, `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

**Implementation:**
- `NativeStrideViewportHost.SetInputBlocked` 将输入阻断状态传递给 Stride game。
- `EmbeddedStrideViewportGame` 在阻断时停止相机与笔刷更新，释放相机控制、结束笔触、隐藏笔刷 decal，并 flush 鼠标锁定状态。

**Rationale:**
- Stride 视口是嵌入式原生窗口，Avalonia overlay 不保证拦截底层 SDL/Stride 输入。

**Architecture Compliance:**
- ✅ native viewport 输入状态仍封装在 viewport host/game 边界内。

### 4. 测试覆盖
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs`, `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`, `Terrain.Editor.Tests/VirtualResources/EditorPendingResourceWorkflowTests.cs`

**Implementation:**
- 覆盖保存进度模型、失败进度终态、async Save 入口、modal overlay、命令门禁、viewport input block/flush 等关键约束。

**Rationale:**
- 当前 editor 测试大量采用文本约束保护 workflow，不启动完整 Avalonia/Stride UI；本轮沿用该模式降低测试脆弱度。

**Architecture Compliance:**
- ✅ 测试覆盖现有保存服务和 editor workflow 文本约束。

---

## Decisions Made

### Decision 1: 本轮不做取消
**Context:** Save 仍使用事务化 staged writer，取消会引入半完成状态、回滚边界和 UI 语义问题。
**Options Considered:**
1. 不可取消模态进度 - 范围小，直接解决卡住感和误输入。
2. 加取消按钮 - 需要 writer 中断、事务回滚和 UI 恢复语义。
3. 只显示 indeterminate spinner - 反馈不足，无法表达阶段推进。

**Decision:** 选择不可取消的模态进度。
**Rationale:** 先解决“看起来卡死”和误输入问题；取消需要另行设计写入中断、回滚和用户提示。
**Trade-offs:** 保存过程中用户仍需等待完成。
**Documentation Impact:** 已记录到本日志和 `docs/CURRENT_FEATURES.md`。

### Decision 2: UI 线程捕获快照，后台只文件写入
**Context:** 保存数据来自编辑器内存状态，直接在后台读取可变编辑状态会与 UI 操作和渲染状态竞争。
**Options Considered:**
1. 后台线程直接读 editor state - 实现短，但数据竞争风险高。
2. UI 线程先捕获不可变快照 - 边界清晰，后台只写文件。
3. 全部留在 UI 线程 - 保留卡住问题。

**Decision:** `TerrainManager.CreateAuthoringSaveSnapshot` 在 UI 线程捕获不可变快照，后台任务只消费快照并写文件。
**Rationale:** 明确线程边界，降低保存过程中数据竞争风险。
**Trade-offs:** 快照会产生一次高度图和 biome mask 数据复制成本。
**Documentation Impact:** 已更新 `docs/ARCHITECTURE_OVERVIEW.md`。

### Decision 3: native HWND 不能只依赖 Avalonia overlay
**Context:** Stride 视口是嵌入式原生窗口，Avalonia overlay 不保证拦截底层 SDL/Stride 输入。
**Options Considered:**
1. 只禁用 Avalonia root - 简单，但 native viewport 仍可能接收输入。
2. overlay + viewport input block - 覆盖 Avalonia 和 Stride 两层输入。
3. 暂停 Stride game loop - 风险更大，可能影响渲染和资源状态。

**Decision:** Save modal 同时调用 `NativeStrideViewportHost.SetInputBlocked`，并在 `EmbeddedStrideViewportGame` 中 flush 相机/笔刷输入状态。
**Rationale:** 防止保存期间仍发生相机移动、笔触继续或鼠标锁定残留。
**Trade-offs:** viewport host/game 增加一个编辑器保存状态入口。
**Documentation Impact:** 已记录到本日志。

---

## What Worked ✅

1. **先红测试再实现**
   - What: 先提交保存进度、modal workflow、viewport input block 的文本和服务测试。
   - Why it worked: 能在不启动完整 UI 的情况下锁定关键行为。
   - Reusable pattern: Yes。

2. **快照隔离后台写入**
   - What: UI 线程复制作者态状态，后台只做文件系统写入。
   - Impact: 避免保存线程读取可变 editor state。

---

## What Didn't Work ❌

1. **初版日志未完全套用模板**
   - What we tried: 先写了精简会话日志。
   - Why it failed: AGENTS 明确要求使用 `docs/log/TEMPLATE.md` 模板，精简版缺少若干模板字段。
   - Lesson learned: 会话结束日志需要保留模板关键段落，即使某些段落只记录“无”。
   - Don't try this again because: 后续审查会把模板字段缺失视为流程问题。

---

## Problems Encountered & Solutions

### Problem 1: 保存期间 UI 看起来卡死
**Symptom:** Save 执行文件写回时，Editor 没有进度反馈，用户会误以为进程卡住。
**Root Cause:** Save 命令原本同步执行长写入流程。
**Investigation:**
- Found: 作者态保存集中在 `EditorResourceSaveService` 和 `TerrainManager`。
- Found: 保存流程可以拆成 UI 快照和后台文件写入。

**Solution:**
- `EditorShellViewModel.Save` 改为 async。
- `TerrainManager.CreateAuthoringSaveSnapshot` 捕获快照。
- `EditorResourceSaveService.SaveAuthoringResources` 报告 `AuthoringSaveProgress`。

**Why This Works:** UI 线程能继续处理 Avalonia 消息和进度绑定更新，文件写入在后台执行。
**Pattern for Future:** 长时间 editor 写操作优先拆成 UI 快照 + 后台 IO。

### Problem 2: Avalonia overlay 不能保证阻断 Stride native viewport
**Symptom:** 保存 modal overlay 只能覆盖 Avalonia 层，底层 native HWND 仍可能处理输入。
**Root Cause:** 嵌入式 Stride 视口有独立原生输入路径。
**Investigation:**
- Found: viewport host 可以访问 `EmbeddedStrideViewportGame`。
- Found: camera 和 brush 状态需要同步 flush，不能只设置布尔值等待下一帧。

**Solution:**
- `NativeStrideViewportHost.SetInputBlocked` 同步传递阻断状态。
- `EmbeddedStrideViewportGame` 阻断时释放 camera、结束 brush stroke、隐藏 decal 并重置鼠标状态。

**Why This Works:** 保存开始时立即清理已有输入状态，并在保存期间跳过 camera/brush 更新。
**Pattern for Future:** Avalonia modal 涉及 native child window 时，要在 native host 层同步处理输入门禁。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update [ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md) - 记录 async save progress、snapshot 和 native viewport block。
- [x] Update [CURRENT_FEATURES.md](../../../../CURRENT_FEATURES.md) - 记录 Save 现在有 modal progress 且暂不支持取消。

### New Patterns/Anti-Patterns Discovered
**New Pattern:** UI snapshot + background IO
- When to use: Editor 长时间保存、导出、烘焙等需要读 UI/editor state 后写文件的流程。
- Benefits: UI 能继续刷新进度，后台不读取可变编辑状态。
- Add to: 后续如多次复用，可提取到 architecture/learnings。

**New Anti-Pattern:** 只靠 Avalonia overlay 阻断 native child input
- What not to do: 在嵌入式 Stride HWND 场景里只禁用 Avalonia root。
- Why it's bad: native viewport 仍可能保留 camera/brush/mouse capture 状态。
- Add warning to: 后续如果再次遇到 native child modal，可提取到 learnings。

### Architectural Decisions That Changed
- **Changed:** Editor Save 从同步 UI 写回改为 async modal workflow。
- **From:** UI 命令直接执行完整保存。
- **To:** UI 捕获快照，后台写入文件，UI 显示进度并阻断输入。
- **Scope:** Editor shell、resource save service、terrain manager、native viewport host/game。
- **Reason:** 消除保存期间卡死感，并避免保存期间误编辑。

---

## Code Quality Notes

### Performance
- **Measured:** 未做大图保存耗时基准。
- **Target:** Save 期间 UI 保持可刷新，后台写文件不直接读取可变 editor state。
- **Status:** ✅ 自动测试覆盖 workflow；仍需手动大图保存体验验证。

### Testing
- **Tests Written:** 保存进度顺序、失败进度终态、async modal workflow、overlay、命令门禁、viewport input block/flush。
- **Coverage:** `EditorResourceSaveServiceTests.cs`、`EditorWorkflowTextTests.cs`、`EditorPendingResourceWorkflowTests.cs`。
- **Manual Tests:** 大图保存时观察进度持续刷新、窗口响应、viewport 不移动、不继续笔刷绘制。

### Technical Debt
- **Created:** Save 仍不可取消。
- **Paid Down:** 保存进度和输入门禁现在有明确测试约束。
- **TODOs:** 无新增代码 TODO。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 手动验证大图保存时进度刷新是否持续、窗口是否仍响应。
2. 手动验证保存期间 viewport 相机、笔刷、鼠标锁定状态不会残留。
3. 如果需要取消保存，先设计 writer 中断、事务回滚和 UI 提示语义。

### Blocked Items
- **Blocker:** 无。
- **Needs:** 取消保存需要新的需求和设计。
- **Owner:** Editor workflow。

### Questions to Resolve
1. 是否需要 Save 取消按钮 - 影响事务回滚和用户提示语义。
2. 是否要给 Export/Import 复用同一套 modal progress 基础设施 - 取决于后续用户体验反馈。

### Docs to Read Before Next Session
- [Architecture Overview](../../../../ARCHITECTURE_OVERVIEW.md) - 当前 editor/runtime 边界。
- [Current Features](../../../../CURRENT_FEATURES.md) - Save progress 当前完成度。
- [Design](../../../../superpowers/specs/2026-06-15-editor-save-progress-design.md) - 本轮设计范围。

---

## Session Statistics

**Files Changed:** 14
**Lines Added/Removed:** +993/-53
**Commits:** 15

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/ViewModels/EditorShellViewModel.cs` 的 async `Save` workflow。
- Critical decision: UI thread captures `EditorAuthoringSaveSnapshot`; background work writes files only。
- Active pattern: Editor modal save blocks both Avalonia mutating commands and native Stride viewport input。
- Current status: Implementation and docs are complete on `codex/editor-save-progress`。

**What Changed Since Last Doc Read:**
- Architecture: Editor Save 增加 async modal progress 和 native viewport input block。
- Implementation: 新增 `AuthoringSaveProgress` 和 `EditorAuthoringSaveSnapshot`。
- Constraints: Save 仍不可取消。

**Gotchas for Next Session:**
- 不要只依赖 Avalonia overlay 阻断嵌入式视口输入。
- 不要让后台保存线程直接读取可变 editor state。
- 取消保存涉及 writer 中断与 staged rollback 语义，需要单独设计。

---

## Links & References

### Related Documentation
- [Architecture Overview](../../../../ARCHITECTURE_OVERVIEW.md)
- [Current Features](../../../../CURRENT_FEATURES.md)
- [Design](../../../../superpowers/specs/2026-06-15-editor-save-progress-design.md)
- [Plan](../../../../superpowers/plans/2026-06-15-editor-save-progress.md)

### Related Sessions
- None directly; context restored from architecture and current-features documents.

### External Resources
- None.

### Code References
- Save workflow: `Terrain.Editor/ViewModels/EditorShellViewModel.cs`
- Resource save progress: `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`
- Native viewport input block: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

---

## Notes & Observations

- 并行执行 `dotnet build` 和 `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build` 会让 build 偶发看到测试 exe 文件锁重试警告；最终验证应顺序执行。

---

*Template Version: 1.0 - Based on Archon-Engine template*
