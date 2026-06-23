# Ocean System Implementation
**Date**: 2026-06-24
**Session**: ocean-system
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 实现全地图水平 Ocean：Editor 可调 `sea_level`，Ocean 与 River 共用同一海平面，shader 借鉴 CK3 水体贴图语义但使用 Stride scene lighting。

**Success Criteria:**
- `sea_level` 从 `game/map/default.toml [settings]` 读取、保存并进入 runtime bundle。
- `MapSurfaceComponent` 统一引用 Terrain/River/Ocean entity，但不持有 sea level 数据。
- River `_WaterHeight` 不再写死，改由 map sea level 驱动。
- Ocean component/processor/render object/render feature/SDSL 完整接入。
- Runtime scene/compositor 与 Editor fallback compositor 都能创建/渲染 ocean。
- 自动化验证、Stride asset compile 和 Editor smoke 通过。

---

## What We Did

### 1. Sea Level 与 Editor 设置
**Files Changed:** `Terrain/Resources/*`, `Terrain.Editor/Services/Resources/*`, `Terrain.Editor/ViewModels/SettingsViewModel.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Views/MainWindow.axaml`

- `RuntimeMapDefinition` / `TerrainRuntimeResourceBundle` 新增 `SeaLevel`，默认 `3.8f`。
- TOML reader/writer/scaffold/save snapshot 支持 `[settings].sea_level`，并验证 finite/range。
- Editor Settings 新增 `Show Ocean` 和 `Sea Level` 控件；保存时将当前 sea level 写入 authoring snapshot。

### 2. MapSurface Coordinator
**Files Changed:** `Terrain/MapSurface/*`, `Terrain/Core/TerrainComponent.cs`, `Terrain/Core/TerrainProcessor.cs`, `Terrain/Assets/MainScene.sdscene`

- `MapSurfaceComponent` 只持有 Terrain/River/Ocean entity 引用，不公开 `SeaLevel`。
- `MapSurfaceProcessor` 通过 `GameRuntimeResourceBootstrap` 加载共享 runtime bundle，注入 terrain，并在 terrain ready 后把 `OceanRuntimeInput(SeaLevel, MapWorldSize)` 推给 Ocean。
- resource bootstrap failure 会 latch，避免每帧重复重试/刷日志。

### 3. River Sea Level 同步
**Files Changed:** `Terrain/Rendering/River/*`, `Terrain.Editor/Services/RiverRenderingService.cs`

- `RiverRenderSettings.SeaLevel` 从 runtime bundle 或 Editor settings 同步。
- `RiverRenderFeature` 为 bottom/surface 同时绑定 `_WaterHeight = settings.SeaLevel`，用于 river under-ocean fade / water 参数。
- 旧 SDSL 默认 `_WaterHeight=3.0f` 仅作为 shader fallback，不再是运行时值来源。

### 4. Ocean 渲染链路
**Files Changed:** `Terrain/Rendering/Ocean/*`, `Terrain/Effects/Ocean/*`, `Terrain/Terrain.csproj`, `Terrain/Assets/GraphicsCompositor.sdgfxcomp`

- 新增 `OceanComponent`、`OceanProcessor`、`OceanRenderObject`、`OceanRenderFeature`。
- `OceanRenderObject` 构造全图水平 quad：XZ 覆盖 map world size，Y 使用 sea level。
- `OceanResourceLoader` 从 `game/map/water` 直读 water color、ambient normal、flowmap、flow normal、foam/foam ramp/map/noise。
- `OceanSurface.sdsl` 采样 CK3-style water DDS，复用 `RiverStrideLighting` / `WaterSceneLightingBinder` 的 Stride scene light/skybox/shadow 输入。
- `OceanRenderFeature` 在资源未完整加载时跳过 Prepare/Draw，避免 null texture 进入 effect。

### 5. Scene / Editor Wiring
**Files Changed:** `Terrain/Assets/MainScene.sdscene`, `Terrain/Assets/GraphicsCompositor.sdgfxcomp`, `Terrain.Editor/Rendering/NativeViewport/*`, `Terrain.Editor/Services/OceanRenderingService.cs`

- Runtime `MainScene` 增加独立 Ocean entity，并让 MapSurface 引用它。
- Runtime `GraphicsCompositor` 增加 `OceanRenderFeature` selector：`EffectName=OceanSurface`, `RenderGroup=Group1`, Transparent stage。
- Editor viewport 创建 editor-only Ocean entity；terrain 加载后 `OceanRenderingService` 同步 map size/sea level。
- Editor fallback compositor 确保 `OceanRenderFeature`，`Settings.SeaLevel` 同时驱动 Ocean 与 River。

---

## Decisions Made

### Editor Ocean 不通过 MapSurfaceProcessor
**Context:** Editor terrain entity 是 `EditorTerrainComponent`，不是 runtime `TerrainComponent`。  
**Decision:** Editor path 使用 `OceanRenderingService` 直接给 `OceanComponent` 写 `OceanRuntimeInput`。  
**Rationale:** 保持 runtime coordinator 严格依赖 runtime terrain，同时避免 editor-only MapSurface 因缺 `TerrainComponent` 清理 ocean input。  
**Trade-off:** Editor 和 runtime 的输入来源不同，但都消费同一个 `OceanComponent`/`OceanRenderFeature` 渲染链路。

### 不强行跟踪 `game/map/default.toml`
**Context:** `.gitignore` 明确忽略 `/game/`，现有测试只允许跟踪 `game/map/water/flowmap.dds`。  
**Decision:** 本地 `game/map/default.toml` 已写入 `sea_level = 3.8` 供当前工作区 smoke 使用，但不 force-add；自动化测试改为验证 scaffold/save writer 能写 sea level。  
**Rationale:** 保持仓库对 `game/` 的现有边界，避免把大型/本地 authoring 资源纳入版本库。

---

## Validation

**Stride shader asset workflow:**
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`

**Managed tests/build:**
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
- `dotnet build Terrain.sln --no-restore`
- `git diff --check HEAD`

**Editor smoke:**
- Launched `Bin\Editor\Debug\win-x64\Terrain.Editor.exe`.
- Captured `artifacts/ocean-editor-window-smoke-late.png`.
- Screenshot shows Terrain Editor viewport with terrain, river, and visible full-map ocean plane using water/foam texture.

Known warnings are pre-existing NuGet vulnerability warnings plus existing nullable/unused-field/WinForms DPI warnings.

---

## Problems Encountered & Solutions

### Initial screenshot captured the wrong foreground window
**Symptom:** First desktop screenshot showed another game, not Terrain Editor.  
**Solution:** Use the Terrain Editor main window handle, foreground it, wait longer, and capture that window rectangle directly.

### Ignored default map file
**Symptom:** `game/map/default.toml` changed locally but is ignored by `.gitignore`.  
**Solution:** Keep the local smoke input, but do not stage it. Regression coverage now verifies tracked scaffold/save code writes `SeaLevel = 3.8f`.

---

## Architecture Impact

- MapSurface/Ocean status changed from partial/skeleton to complete runtime/editor wiring.
- Ocean now has the same component → processor → render object → render feature shape as River.
- Sea level remains map settings data, not a public `MapSurfaceComponent` or `OceanComponent` property.
- Water scene lighting is shared through `WaterSceneLightingBinder`.

---

## Next Session

1. Use RenderDoc if visual artifacts remain: confirm `OceanSurface` draw call, water DDS bindings, scene skybox, and `_WaterHeight` / `_MapWorldSize` constants.
2. Coastline clipping and shoreline foam are still intentionally out of scope.
3. If `game/map/default.toml` should become a tracked sample asset later, update `.gitignore` and `GameResourceGitIgnoreTextTests` deliberately.

---

## Session Statistics

**Major commits:**
- `805f62b feat: add sea level map setting`
- `abe5df3 feat: expose sea level editor setting`
- `b52b5d1 feat: add map surface coordinator`
- `a9c7526 feat: drive river water height from sea level`
- `c739bc1 feat: add ocean render component`
- `1b8c68c feat: add ocean water resources`
- `cbaf4d1 refactor: share water scene lighting binding`
- `844719f feat: add ocean shader render feature`
- `6d3bbc1 fix: skip ocean draw without resources`
- `6fd0bed feat: wire ocean scene entities`

---

## Quick Reference

- Runtime coordinator: `Terrain/MapSurface/MapSurfaceProcessor.cs`
- Ocean component/render path: `Terrain/Rendering/Ocean/`
- Ocean shader: `Terrain/Effects/Ocean/OceanSurface.sdsl`
- Shared lighting binder: `Terrain/Rendering/Water/WaterSceneLightingBinder.cs`
- Runtime scene/compositor: `Terrain/Assets/MainScene.sdscene`, `Terrain/Assets/GraphicsCompositor.sdgfxcomp`
- Editor façade: `Terrain.Editor/Services/OceanRenderingService.cs`

