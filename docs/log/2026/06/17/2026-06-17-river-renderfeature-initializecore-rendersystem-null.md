# RiverRenderFeature InitializeCore RenderSystem Null
**Date**: 2026-06-17
**Session**: RiverRenderFeature InitializeCore RenderSystem null
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用 VS MCP 定位 `Terrain.Editor` 调试时的中断原因，并恢复正常启动。

**Success Criteria:**
- 找到 VS 中断点对应的真实根因。
- 修复后重新 F5，程序不再在同一点异常中断。

---

## What We Did

### 1. 用 VS MCP 确认中断现场
**Observed via VS MCP:**
- `Mode = Break`
- `LastBreakReason = ExceptionThrown`
- `CurrentProcessName = Terrain.Editor.exe`
- `CurrentFile = Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `CurrentLine = 63`
- `CurrentFunction = RiverRenderFeature.InitializeCore`

### 2. 对照 Stride 初始化时序
**Root Cause:**
- `RiverRenderFeature.InitializeCore()` 里使用了 `Context.RenderSystem.RenderFeatures`。
- 对 `RenderFeature` 来说，这个时机 `Context.RenderSystem` 还没有由 `GraphicsCompositor` 填入。
- 但 `RenderFeature.RenderSystem` 属性已经在 `RenderSystem.RenderFeatures_CollectionChanged` 中先行设置好。

### 3. 修复
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

```csharp
- var meshRenderFeature = Context.RenderSystem.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
+ var meshRenderFeature = RenderSystem?.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
```

**Why This Works:**
- 避免在 `InitializeCore()` 过早访问 `Context.RenderSystem`。
- 与项目里现有 `EditorTerrainRenderFeature` / `TerrainRenderFeature` 的写法保持一致。

### 4. 验证
- `dotnet build Terrain.Editor.csproj -c Debug -p:UseSharedCompilation=false` 通过
- 杀掉被 VS 挂住的旧进程后重新 F5
- VS MCP 复查：
  - `Mode = Run`
  - `IsDebugging = true`
  - `LastBreakReason = None`

---

## Quick Reference

**Bug Pattern:**
- `RenderFeature.InitializeCore()` 里不要假设 `Context.RenderSystem` 已可用。

**Preferred Access Path:**
- 取 root render feature 列表时优先用 `RenderFeature.RenderSystem`，不要用 `Context.RenderSystem`。

---
