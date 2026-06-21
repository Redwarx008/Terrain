# Runtime River Integration
**Date**: 2026-06-22
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

把河流从 editor-only 渲染路径接到 Runtime，同时遵守 Stride scene system：运行时 `RiverSystem` 由 scene asset 创建，`RiverRenderFeature` 由 graphics compositor asset 注册，不在 `TerrainRuntimeResourceBundle` 或 runtime bootstrap 代码里手动挂系统。

---

## What We Did

### 1. 共享 River Core

- 将河流 mesh/service/rendering 代码整理到 `Terrain/Rivers/` 与 `Terrain/Rendering/River/`。
- 将 river shaders 和 environment content 移到 runtime 项目资产目录。
- Editor 保留 façade 和 embedded viewport 动态 wiring，但不再拥有核心 river 渲染实现。

### 2. Runtime Resource Bundle

- `TerrainRuntimeResourceBundle` 暴露 `RiversPath`、`RiverMinWidth`、`RiverMaxWidth`。
- `GameRuntimeResourceBootstrap` 从 `game/map/default.toml` 的 `[settings]` 读取河流宽度配置。
- 缺少 `rivers.png` 作为可选资源处理，不阻断 terrain runtime bootstrap。

### 3. Terrain Height Source

- `TerrainProcessor` 不再为 Runtime DetailMap 读取完整 height data，也不再把整张 `ushort[]` 挂到 `TerrainComponent` 上。
- `TerrainComponent.GetHeight(int sampleX, int sampleZ)` 暴露离散高度 sample 查询；缺 tile 时通过 `.terrain` height page 读取和固定 4-tile CPU cache 补齐。Runtime DetailMap 与 River 都复用这个组件接口。
- River mesh 生成需要连续高度时自己对 4 个离散 sample 做双线性插值，不让 Terrain 承担 river-specific 采样策略。

### 4. RiverProcessor Runtime Load

- `RiverProcessor` 在发现 initialized `TerrainComponent` 后加载 runtime river 资源。
- 通过 `RiverMapService` / `RiverMeshService` 生成 mesh snapshot 并写回 `RiverComponent`。
- 加载失败按 config 记一次失败状态，缺少 `rivers.png` 标记为 `NoRiverResource`，避免逐帧重试。

### 5. Scene Asset Wiring

- `Terrain/Assets/MainScene.sdscene` 增加 `RiverSystem` entity，挂 `RiverComponent`。
- `Terrain/Assets/GraphicsCompositor.sdgfxcomp` 增加 `RiverRenderFeature`，绑定 Transparent render stage。
- `TerrainRuntimeResourceBundle` 仍只做资源/config carrier，不创建 scene object。

---

## Decisions Made

### Runtime RiverSystem Comes From Scene Asset

**Decision:** 使用 Stride scene/compositor asset 注册 runtime 河流系统，而不是在 bootstrap 代码里创建 entity 或 render feature。

**Rationale:** Runtime asset graph 才是 Stride 的系统组合入口；bundle/bootstrap 保持资源解析职责，避免把 scene 生命周期混进资源层。

### Mesh Generation Belongs In RiverProcessor

**Decision:** `RiverComponent` 只保存 mesh snapshot 和 load state；runtime mesh 生成由 `RiverProcessor` 触发。

**Rationale:** Processor 已拥有 entity/component 到 render object 的同步生命周期，也能等待 terrain height source 初始化后再构建 mesh。

---

## Verification

- `dotnet msbuild Terrain/Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain/Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain/Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`
- `dotnet build Terrain.sln --no-restore`
- `git diff --check`

All verification commands completed successfully. Build output still includes existing warnings: package advisory warnings, nullable warning in `TerrainRenderFeature`, unused fields/events, and WFO0003.

---

## Architecture Impact

- `ARCHITECTURE_OVERVIEW.md` now records runtime river scene/compositor asset ownership.
- `CURRENT_FEATURES.md` now marks Runtime river rendering as complete.
- No new ADR was needed; this follows the existing river architecture and the user decision to use Stride scene files.

---

## Quick Reference

- Runtime entity: `Terrain/Assets/MainScene.sdscene` (`RiverSystem`)
- Runtime render feature: `Terrain/Assets/GraphicsCompositor.sdgfxcomp`
- Runtime mesh load: `Terrain/Rendering/River/RiverProcessor.cs`
- Runtime discrete height sampling: `Terrain/Streaming/TerrainHeightSampler.cs`
- Width config: `Terrain/Resources/TerrainRuntimeResourceBundle.cs`
