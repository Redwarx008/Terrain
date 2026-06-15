# Editor Save 异步模态进度
**Date**: 2026-06-15
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Editor Save 添加模态进度，避免保存大图资源时看起来卡死。

**Success Criteria:**
- Save 写回作者态资源时持续报告进度。
- 保存期间不能触发会修改状态的命令。
- Avalonia overlay 之外，原生 Stride HWND 也不能继续接收相机/笔刷输入。
- 自动验证通过。

---

## What We Did

### 1. Save 进度模型与异步写回
**Files Changed:** `Terrain.Editor/Services/Resources/AuthoringSaveProgress.cs`, `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs`, `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`, `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`

**主要实现：**
- 新增 `AuthoringSaveProgress`，按保存阶段报告准备、校验、各 writer 写入、commit、刷新状态。
- 新增 `EditorAuthoringSaveSnapshot`，由 `TerrainManager.CreateAuthoringSaveSnapshot` 在 UI 线程捕获高度图、biome mask、材质和规则状态的不可变快照。
- `EditorShellViewModel.Save` 改为 async 流程：先做 UI 线程快照，再通过后台任务执行作者态资源文件写入。
- `EditorResourceSaveService` 保留事务化 staged writer 语义，并接入进度上报。

### 2. 模态 overlay 与命令门禁
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Views/MainWindow.axaml`

**主要实现：**
- Save 期间设置 `IsSaving`、`SaveProgressMessage`、`SaveProgressPercent`，在主窗口显示 modal overlay。
- Save/Export/Import/Undo/Redo/资源修改等可变更命令共用保存门禁，保存期间不可执行。

### 3. 原生视口输入阻断与 flush
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`, `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

**主要实现：**
- `NativeStrideViewportHost.SetInputBlocked` 将输入阻断状态传递给 Stride game。
- `EmbeddedStrideViewportGame` 在阻断时停止相机与笔刷更新，释放相机控制、结束笔触、隐藏笔刷 decal，并 flush 鼠标锁定状态。
- 该处理覆盖 native HWND，因为 Avalonia overlay 不能单独阻止嵌入式 Stride 视口输入。

### 4. 测试覆盖
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorSaveProgressWorkflowTests.cs`, `Terrain.Editor.Tests/Program.cs`

**主要实现：**
- 覆盖保存进度模型、async Save 入口、modal overlay、命令门禁、viewport input block/flush 等关键文本约束。

---

## Decisions Made

### Decision 1: 本轮不做取消
- **Context:** Save 仍使用事务化 staged writer，取消会引入半完成状态、回滚边界和 UI 语义问题。
- **Decision:** 本轮只显示不可取消的模态进度。
- **Rationale:** 先解决“看起来卡死”和误输入问题；取消需要另行设计写入中断、回滚和用户提示。

### Decision 2: UI 线程捕获快照，后台只文件写入
- **Context:** 保存数据来自编辑器内存状态，直接在后台读取可变编辑状态会与 UI 操作和渲染状态竞争。
- **Decision:** `TerrainManager.CreateAuthoringSaveSnapshot` 在 UI 线程捕获不可变快照，后台任务只消费快照并写文件。
- **Rationale:** 明确线程边界，降低保存过程中数据竞争风险。

### Decision 3: native HWND 不能只依赖 Avalonia overlay
- **Context:** Stride 视口是嵌入式原生窗口，Avalonia overlay 不保证拦截底层 SDL/Stride 输入。
- **Decision:** Save modal 同时调用 `NativeStrideViewportHost.SetInputBlocked`，并在 `EmbeddedStrideViewportGame` 中 flush 相机/笔刷输入状态。
- **Rationale:** 防止保存期间仍发生相机移动、笔触继续或鼠标锁定残留。

---

## Verification

- `dotnet build Terrain.sln`
  - 结果：通过。
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build`
  - 结果：通过。

---

## Next Session

### Immediate Next Steps
1. 手动验证大图保存时进度刷新是否持续、窗口是否仍响应。
2. 取消保存需要另行设计，不在本轮实现范围内。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Save progress uses `AuthoringSaveProgress`, not export progress.
- UI thread captures `EditorAuthoringSaveSnapshot`; background work writes files only.
- Save modal must block both Avalonia mutating commands and native Stride viewport input.

**Gotchas for Next Session:**
- 不要只依赖 Avalonia overlay 阻断嵌入式视口输入。
- 取消保存涉及 writer 中断与 staged rollback 语义，需要单独设计。

