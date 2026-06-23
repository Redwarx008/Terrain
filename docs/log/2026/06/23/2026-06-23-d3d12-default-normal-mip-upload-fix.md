# D3D12 Default Normal Mip Upload Fix
**Date**: 2026-06-23
**Session**: runtime interruption diagnosis via VS MCP
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Inspect the interrupted `Terrain.Windows.exe` process through Visual Studio MCP and identify the current break cause.

**Success Criteria:**
- Capture debugger status, call stack, and locals.
- Fix the immediate startup interruption if it is project-side.
- Verify with build/tests and a runtime smoke test.

---

## Context & Background

**Previous Work:**
- [Runtime Vulkan API Switch](2026-06-23-runtime-vulkan-api-switch.md)
- [River 1x1 Scene Color Guard](2026-06-23-river-1x1-scene-color-guard.md)

**Current State:**
- VS was paused in `Terrain.Windows.exe`.
- Current working copy has `Terrain.Windows.csproj` set to `Direct3D12`, despite the previous Vulkan session log.
- Current `Stride.Graphics.4.3.0.2743.nupkg` contains `lib/net10.0/Direct3D12/Stride.Graphics.dll`, not a Vulkan backend folder.

---

## What We Did

### 1. Inspected The Break With VS MCP
**Tools Used:** `debugger_status`, `debugger_get_callstack`, `debugger_get_locals`

**Findings:**
- Exception: `System.ExecutionEngineException`
- Break location: `Terrain/Materials/RuntimeMaterialManager.cs:246`
- Current mip: `1`
- Texture format: `BC3_UNorm`
- Texture size: `2048x2048`, `12` mips
- Native path: `Stride.Graphics.CommandList.UpdateSubResource` -> `Silk.NET.Direct3D12.WriteToSubresource`

### 2. Fixed Mip Upload Region
**Files Changed:**
- `Terrain/Materials/RuntimeMaterialManager.cs`
- `Terrain.Editor/Services/MaterialSlotManager.cs`

**Implementation:**
- Each generated fallback normal mip now calls `Texture.SetData` with an explicit `ResourceRegion` matching the current mip dimensions.

**Rationale:**
- Stride D3D12 `UpdateSubResource(resource, subResourceIndex, DataBox)` builds a default region from the full texture dimensions.
- For mip 0 that is valid.
- For mip 1+ the upload resource dimensions no longer match the `DataBox` row/slice pitch, and D3D12 can fail inside `WriteToSubresource`.

### 3. Removed Runtime Profiling Overlay
**Files Changed:**
- `Terrain.Windows/TerrainApp.cs`

**Findings:**
- After the mip upload fix, the next VS break was a `NullReferenceException` at `TerrainGame.Draw -> base.Draw`.
- `$exception.ToString()` showed the real stack:
  - `Stride.Profiling.GameProfilingSystem.Draw`
  - `CommandList.SetRenderTargetAndViewport(null, renderTarget)`
  - D3D12 `SetRenderTargetsImpl`
- Presenter backbuffer and depth were both valid (`1280x720`), so the crash was isolated to the optional profiling overlay path.

**Implementation:**
- Removed `ProfilingSystem.EnableProfiling(false, GameProfilingKeys.GameDrawFPS)` from runtime `BeginRun`.

**Rationale:**
- The runtime FPS overlay is not required for map rendering.
- Avoids a D3D12 backend/profiling path that can throw during draw.

---

## What Worked ✅

1. **VS MCP call stack + locals**
   - It identified the exact texture upload state without guessing from logs.

2. **Explicit mip-sized upload region**
   - Keeps runtime and editor fallback texture paths aligned.
   - Avoids depending on backend default-region behavior for nonzero mips.

3. **Expanding `$exception.ToString()`**
   - VS stopped at `TerrainGame.Draw`, but the expanded exception showed the real Stride profiling stack.

---

## What Didn't Work ❌

1. **Assuming the session was still Vulkan**
   - The current project file and call stack both showed Direct3D12.
   - The local `4.3.0.2743` graphics package currently contains Direct3D12 backend output.

2. **Parallel project builds**
   - `Terrain.Windows` and `Terrain.Editor.Tests` builds raced over `Terrain/obj/.../Terrain.dll`.
   - Serial build fixed the issue.

---

## Code Quality Notes

### Testing
- `dotnet build Terrain.Windows\Terrain.Windows.csproj --no-restore` passed.
- `dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-build` ran with the two known pre-existing failures:
  - `runtime scene contains river system component`
  - `runtime compositor registers river render feature`
- Runtime smoke launched `Terrain.Windows.exe` for 10 seconds; process stayed running and did not reproduce either the default normal upload crash or the profiling overlay crash.

### Warnings
- Existing NuGet vulnerability warnings remain.
- Existing nullable/unused-field/WinForms manifest warnings remain.

---

## Next Session

### Immediate Next Steps
1. Decide whether `Terrain.Windows` should remain `Direct3D12` or switch back to `Vulkan`.
2. If Vulkan is still desired, rebuild local Stride packages with `StrideGraphicsApis=Vulkan` and verify the package contains `lib/net10.0/Vulkan/Stride.Graphics.dll`.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- The break was not the previous river `1x1` scaler exception.
- The active backend was Direct3D12.
- Generated fallback texture mips should be uploaded with an explicit mip-sized `ResourceRegion`.
- Runtime `GameDrawFPS` profiling overlay is currently disabled because it crashed in Stride D3D12 `SetRenderTargetsImpl`.

**Gotchas for Next Session:**
- Do not trust the previous Vulkan log without checking the current `.csproj` and package contents.
- Avoid parallel MSBuild invocations that share the same project `obj` folder.
