# 河流 Water/Bottom 纹理迁移到 game/map/water
**Date**: 2026-06-20
**Session**: River local water texture files
**Status**: Complete
**Priority**: Medium

---

## Session Goal

- 将 `Terrain.Editor/Assets/River/Water` 和 `Terrain.Editor/Assets/River/Bottom` 中的 DDS 迁移到 `game/map/water`。
- 使用下划线文件名。
- Bottom/Water 纹理不再通过 Stride `.sdtex`、RootAsset 或 `ContentManager` 加载。
- `River/Environment` 保持现有 Stride 内容资源路径。

## Context & Background

- 河流渲染已有 `bottom -> refraction -> surface` 链路。
- 原实现通过 `Terrain.Editor/Assets/River/Bottom` / `Water` 下的 `.sdtex` 和 `Terrain.Editor.sdpkg` RootAssets，把 Bottom/Water 纹理作为 Stride 内容资源动态加载。
- 用户要求资源改到 `game/map/water`，并要求中间命名使用下划线。

## What We Did

### 1. 资源位置与命名

- 新增 `game/map/water`。
- 将 Bottom/Water 12 张 DDS 迁移到该目录，并把中划线文件名改为下划线文件名：
  - `bottom_diffuse.dds`
  - `bottom_normal.dds`
  - `bottom_properties.dds`
  - `bottom_depth.dds`
  - `ambient_normal.dds`
  - `flow_normal.dds`
  - `foam.dds`
  - `foam_ramp.dds`
  - `foam_map.dds`
  - `foam_noise.dds`
  - `shadow_color.dds`
  - `water_color.dds`
- 删除 Bottom/Water `.sdtex` 描述文件。
- `Terrain.Editor/Assets/River/Water/flow-map.dds` 当前未纳入 loader/shader/RootAsset 使用，保留为非运行时入口资源。

### 2. 加载链路

- `RiverResourceLoader` 改为对 Bottom/Water DDS 使用文件流和 `Texture.Load`。
- `reflection-specular` 继续使用 `ContentManager.Load<Texture>("River/Environment/reflection-specular")`。
- 缺失本地 DDS 时先写 `Terrain.Editor` 错误日志，再让 `File.OpenRead` 的原始异常冒泡。
- 不 catch 或包装纹理加载异常；DDS 内容损坏、文件不可读、Stride 内容资源缺失等情况按原始异常崩溃。

### 3. Stride 包配置

- 从 `Terrain.Editor.sdpkg` 移除 `River/Bottom/*` 和 `River/Water/*` RootAssets。
- 保留 `River/Environment/reflection-specular` RootAsset。

### 4. 测试与文档

- `RiverShaderTextTests` 改为验证 `game/map/water` 下 12 个 DDS 存在且非空。
- 测试锁定 Bottom/Water 不再是 RootAssets，且 loader 不再包含 `River/Bottom/` / `River/Water/` 内容 URL。
- 更新 `Terrain.Editor/Assets/River/README.md`、`docs/ARCHITECTURE_OVERVIEW.md`、`docs/CURRENT_FEATURES.md`。

## Verification

Ran:

```powershell
dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug --no-restore
dotnet test Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RiverShaderTextTests"
rg -n "River/(Bottom|Water)" Terrain.Editor/Terrain.Editor.sdpkg
```

Results:

- Build passed with existing NuGet/security/analyzer warnings.
- Targeted river shader text tests passed.
- `Terrain.Editor.sdpkg` no longer contains `River/Bottom` or `River/Water` RootAssets.

## Notes

- `River/Environment` 未迁移。
- Shader 参数名与采样语义未改。
- Bottom/Water 纹理加载失败不再被包装成 `InvalidOperationException`。
