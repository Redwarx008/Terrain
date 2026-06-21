# Terrain 编译工作区资源根修正
**Date**: 2026-06-22
**Session**: terrain assembly resource root
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复 Stride Game Studio 加载项目时，Runtime 从引擎 GameStudio 输出目录定位 `game/` 导致失败的问题。

**Success Criteria:**
- 生产资源入口不再依赖宿主进程的 `AppContext.BaseDirectory`。
- Runtime、Editor 资源会话、材质恢复 resolver、河流 water 纹理加载都通过 `TerrainWorkspaceRoot` 元数据优先定位资源根，元数据不可用时回退 `Terrain` 程序集目录。
- 有回归测试防止重新传入宿主目录。

---

## Context & Background

**Symptom:**
- Game Studio 日志报错：
  `Terrain runtime resources could not be read: Could not locate game resource root from 'E:\WorkSpace\stride\sources\editor\Stride.GameStudio\bin\Release\net10.0-windows\'`

**Root Cause:**
- Stride Game Studio 作为宿主进程加载项目时，`AppContext.BaseDirectory` 指向引擎编辑器输出目录，而不是 Terrain 项目输出目录。
- 旧资源入口从 `AppContext.BaseDirectory` 向上扫描，因此找不到 `E:\Stride Projects\Terrain\game`。
- 查 `E:\WorkSpace\stride` 源码确认：`AssemblyContainer.LoadAssemblyFromPathInternal` 对非 `AppDomain.CurrentDomain.BaseDirectory` 目录下的程序集读取 `File.ReadAllBytes(assemblyFullPath)`，再执行 `Assembly.Load(assemblyBytes, pdbBytes)` / `Assembly.Load(assemblyBytes)`。因此 GameStudio 编辑器内项目程序集的 `Assembly.Location` 不可靠，不能作为项目资源根定位依据。
- Stride 确实在 editor/session 层知道项目输出路径：`PackageLoadedAssembly.Path` 来自 `SolutionProject.TargetPath` / `VSProjectHelper.GetOrCompileProjectAssembly(...).AssemblyPath`，并在 reload 时更新。但这些信息没有注入到运行时组件/processor 服务中，项目代码不能直接从 `TerrainProcessor` 拿到。

---

## What We Did

### 1. 新增 Terrain 编译工作区入口
**Files Changed:** `Terrain/Terrain.csproj`, `Terrain/Resources/GameResourceRootLocator.cs`, `Terrain/Resources/GameResourceResolverBootstrap.cs`

**Implementation:**
- `Terrain.csproj` 将工作区根写入 `Terrain.dll` 的 `AssemblyMetadata("TerrainWorkspaceRoot", ...)`。
- 新增 `GameResourceRootLocator.TerrainAssemblyDirectory`。
- 新增 `GameResourceRootLocator.FindFromTerrainAssembly()`。
- 新增 `GameResourceResolverBootstrap.CreateForTerrainAssemblyDirectory()`。

**Rationale:**
- `TerrainWorkspaceRoot` 元数据在程序集被 GameStudio 按字节加载后仍随 DLL 内容保留，比 `Assembly.Location` 更稳定。
- `Terrain` 程序集目录作为元数据不可用时的回退入口。
- `CreateForAppDirectory(appRoot)` 继续保留给测试和显式工具入口。

### 2. 切换生产调用点
**Files Changed:** `Terrain/Core/TerrainProcessor.cs`, `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`, `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`

**Implementation:**
- Runtime 地形加载改用 `CreateForTerrainAssemblyDirectory()`。
- Editor 默认资源会话改用 `CreateForTerrainAssemblyDirectory()`，显式传入 appDirectory 的测试/工具路径仍走 `CreateForAppDirectory(appDirectory)`。
- `TerrainManager` 材质恢复 resolver 改用 `CreateForTerrainAssemblyDirectory()`。
- 河流本地 water 纹理目录改用 `FindFromTerrainAssembly()`。
- `CreateForTerrainAssemblyDirectory()` 现在直接调用 `FindFromTerrainAssembly()` 获取 game root，并用 `TerrainResourceAppDirectory` 读取/生成 `LaunchSetting.json`。

### 3. 回归测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/GameResourceRootLocatorTests.cs`, `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`

**Coverage:**
- `FindFromTerrainAssembly()` 能解析到仓库 `game/`。
- `FindFromTerrainAssemblyContext(hostAssemblyDirectory, buildWorkspaceRoot)` 能覆盖 `Terrain.dll` 的实际加载位置不可靠、但编译工作区元数据可用的场景。
- 生产代码不再出现 `CreateForAppDirectory(AppContext.BaseDirectory)` 或 `FindFrom(AppContext.BaseDirectory)`。
- 运行时入口、Editor 入口、TerrainManager、RiverResourceLoader 都被文本断言锁定。

---

## Decisions Made

### Decision 1: 生产资源入口以 TerrainWorkspaceRoot 元数据优先
**Context:** Game Studio 宿主目录不属于项目工作区。

**Decision:** 生产代码优先从 `Terrain.dll` 的 `TerrainWorkspaceRoot` 编译元数据定位 `game/` 与本地 `LaunchSetting.json`；元数据不可用时回退 `Terrain` 程序集所在目录。

**Trade-offs:**
- 获得：Game Studio 宿主进程目录和 byte-loaded assembly 的不可靠 `Assembly.Location` 不会污染项目资源定位。
- 放弃：开发构建的 DLL 携带本机工作区路径；如果未来要支持可重定位发布包，需要新增显式部署入口或发布期重写元数据。

**Documentation Impact:**
- 更新 `docs/ARCHITECTURE_OVERVIEW.md`。
- 更新 `docs/CURRENT_FEATURES.md`。
- 修订 `docs/log/decisions/adr-015-workspace-game-root-and-runtime-requirements.md`。

---

## Problems Encountered & Solutions

### Problem 1: 文本回归测试扫描到自身
**Symptom:** 新增的禁止字符串测试失败在 `RuntimeMigrationTextTests.cs` 自己。

**Solution:**
- 用字符串拼接构造禁止模式，避免源码里直接出现完整禁止字符串。

### Problem 2: 程序集目录入口在 GameStudio byte-load 下不可靠
**Symptom:** 用户复测后仍看到同一路径：`E:\WorkSpace\stride\sources\editor\Stride.GameStudio\bin\Release\net10.0-windows\`。

**Root Cause:**
- Stride 源码确认项目程序集通常由 `Assembly.Load(byte[])` 加载；这类 assembly 的 `Location` 不提供项目输出路径。
- 初版 `CreateForTerrainAssemblyDirectory()` 仍把 `TerrainAssemblyDirectory` 传给 `CreateForAppDirectory()`，没有真正通过 `FindFromTerrainAssembly()` 使用新元数据。

**Solution:**
- 在 `Terrain.dll` 中嵌入 `TerrainWorkspaceRoot` 编译元数据。
- `FindFromTerrainAssembly()` 优先使用 `TerrainWorkspaceRoot`，元数据不可用时才回退程序集目录。
- `CreateForTerrainAssemblyDirectory()` 直接通过 `FindFromTerrainAssembly()` 获取 game root，避免再次绕回 host assembly path。

**Stride Source Evidence:**
- `E:\WorkSpace\stride\sources\core\Stride.Core.Design\Reflection\AssemblyContainer.cs:232` reads assembly bytes from disk.
- `E:\WorkSpace\stride\sources\core\Stride.Core.Design\Reflection\AssemblyContainer.cs:314` loads those bytes via `Assembly.Load(...)`.
- `E:\WorkSpace\stride\sources\assets\Stride.Core.Assets\Package.cs:1041` stores the project assembly path in `PackageLoadedAssembly`.
- `E:\WorkSpace\stride\sources\assets\Stride.Core.Assets\Package.cs:1056` loads that path through `AssemblyContainer`.
- `E:\WorkSpace\stride\sources\editor\Stride.Assets.Presentation\AssemblyReloading\ReloadAssembliesOperation.cs:75` updates `PackageLoadedAssembly.Path` during reload.

---

## Code Quality Notes

### Testing
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`
- `dotnet build Terrain.sln --no-restore`
- `git diff --check`
- Binary scan of `Terrain/bin/Debug/net10.0-windows/Terrain.dll` confirmed both `TerrainWorkspaceRoot` marker and `E:\Stride Projects\Terrain` value are present.

**Result:** Passed.

**Known Warnings:**
- NuGet vulnerability warnings from existing package versions.
- Existing nullable / WinForms manifest / unused field warnings outside 本次改动范围。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 生产资源入口不要回退到 `AppContext.BaseDirectory`。
- `Terrain.dll` 现在携带 `TerrainWorkspaceRoot` 编译元数据，`FindFromTerrainAssembly()` 会优先使用它。
- 使用 `GameResourceResolverBootstrap.CreateForTerrainAssemblyDirectory()` 构建生产 resolver。
- 使用 `GameResourceRootLocator.FindFromTerrainAssembly()` 直接定位 `game/`。
- `CreateForAppDirectory(appRoot)` 仍是测试和显式工具入口，不是生产默认入口。

**Gotchas for Next Session:**
- 如果 Game Studio 仍报同类错误，先确认加载到 GameStudio 的 `Terrain.dll` 是否为最新构建，并检查其中是否含 `TerrainWorkspaceRoot` 元数据；不要恢复 `AppContext.BaseDirectory` 或 `Assembly.Location` 作为生产定位依据。
