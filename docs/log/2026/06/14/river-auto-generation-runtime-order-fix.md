# River 自动生成时序修复
**Date**: 2026-06-14
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复 `rivers.png` 已成功加载，但 Editor 仍不会自动生成 river mesh 的时序 bug

**Secondary Objectives:**
- 为这个时序问题补回归测试

**Success Criteria:**
- runtime-ready 路径会先接好 river 服务，再加载资源会话
- 自动加载 `rivers.png` 时会触发 river mesh 生成
- 回归测试能稳定卡住顺序错误

---

## Context & Background

**Previous Work:**
- Related: [river-auto-generation-on-load.md](./river-auto-generation-on-load.md)

**Current State:**
- 代码已经改成“river map 变化后自动生成”
- 但用户实测仍反馈：`rivermap` 导入成功后没有自动生成 river

**Why Now:**
- 这是自动资源加载主链路上的真实回归，必须优先修

---

## What We Did

### 1. 定位真实根因
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Services/TerrainManager.cs`

**Implementation:**
- 检查 `OnViewportRuntimeStateChanged()` 和 `LoadEditorResourceSessionAsync()` 的调用顺序
- 确认 `TerrainManager.LoadTerrainAsync()` / `LoadFromResourceSession()` 虽然是 `async`，但当前实现几乎同步执行
- 因此 `_ = LoadEditorResourceSessionAsync()` 会在 `TryWireRiverServices()` 之前就把 `LoadRiverMap()` 跑完，并提前触发 `RiverMapChanged`

**Rationale:**
- 当时 `RiverViewModel` 还没有 generator，所以事件虽然触发了，但不会真正生成 mesh

### 2. 用测试先卡住错误顺序
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

**Implementation:**
- 新增文本回归测试：
  - `editor wires river services before loading workspace session`
- 要求两个 runtime-ready 入口都满足：
  - 构造期 `HasSceneRuntime` 路径
  - `OnViewportRuntimeStateChanged()` 路径
- 都必须先 `TryWireRiverServices()`，再 `LoadEditorResourceSessionAsync()`

**Rationale:**
- 这是本次 bug 的直接触发条件，测试先红后绿

### 3. 最小修复时序
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`

**Implementation:**
- 构造期 `HasSceneRuntime` 路径中，把 `TryWireRiverServices()` 移到 `LoadEditorResourceSessionAsync()` 之前
- `OnViewportRuntimeStateChanged()` 中同样先 `TryWireRiverServices()`，再 `_ = LoadEditorResourceSessionAsync()`

**Rationale:**
- 保证自动加载 `rivers.png` 前，`RiverViewModel` 已经拿到 `RiverMeshGenerator`

---

## Problems Encountered & Solutions

### Problem 1: `async` 语义掩盖了真实同步执行顺序
**Symptom:** `rivers.png` 加载成功，但没有自动生成 river
**Root Cause:** `LoadEditorResourceSessionAsync()` 先于 `TryWireRiverServices()` 启动，而内部会同步走到 `LoadRiverMap()`，导致 `RiverMapChanged` 提前发生

**Solution:**
```csharp
TryWireRiverServices();
_ = LoadEditorResourceSessionAsync();
```

**Why This Works:** generator 先接上后，`LoadRiverMap()` 触发的 `RiverMapChanged` 才能真正走到 mesh 生成

---

## Code Quality Notes

### Testing
- **Tests Written:** 1 个新的 shell 时序回归测试
- **Verification:**
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`
    - 新增时序测试通过
    - 全量命令最终仍因既有 scaffold 断言失败：`repository scaffold should not check in terrain.terrain`
  - `dotnet build Terrain.sln -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false`
    - 通过

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 这次 bug 不是 `RiverViewModel` 自动生成逻辑本身坏了，而是 shell 在 runtime-ready 时的 wiring 顺序错了
- 根因是 `LoadEditorResourceSessionAsync()` 当前实现几乎同步执行，不能把它当成“先发起、后回调”的真正异步链路

**Gotchas for Next Session:**
- 如果以后再调整 shell 启动顺序，优先检查 `TryWireRiverServices()` 是否仍早于资源会话加载
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` 的既有 scaffold 失败仍与本次修复无关
