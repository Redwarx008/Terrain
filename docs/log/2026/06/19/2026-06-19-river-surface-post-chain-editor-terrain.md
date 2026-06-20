# 河流 surface 后段资源/参数链接入
**Date**: 2026-06-19
**Session**: river surface post-chain terrain provider
**Status**: ⚠️ Superseded
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 补齐 CK3 surface `CalcWater` 后段的资源/参数链，并按用户要求使用本项目地形系统，不复制 CK3 的 height lookup 资源。

**Success Criteria:**
- surface 后段至少包含 cloud shadow、terrain shadow tint、fog-of-war、map distance fog 的 shader 边界。
- Editor 路径的 terrain normal 使用项目自己的 height slice 数据。
- 不创建白色 FogOfWar 或 flat-height 替代纹理。

**2026-06-19 correction:** 后续确认战争迷雾纹理不应影响河流着色；`FogOfWarAlphaTexture` / sampler / `_HasFogOfWarAlphaTexture` / `SurfaceFogOfWarAlphaTexture` 已从 river surface 链路移除。当前有效状态见 `2026-06-19-river-remove-fog-of-war-alpha-input.md`。

---

## Context & Background

**Previous Work:**
- `docs/log/2026/06/19/2026-06-19-river-debug-still-black-hotedit.md`
- `docs/log/2026/06/19/2026-06-19-river-surface-lighting-cbuffer-parity.md`

**Current State:**
- CK3 surface EID 460 绑定 `ShadowNoiseTexture`、`FogOfWarAlpha`、shadow map 与 terrain height/normal 相关 cbuffers。
- 本项目有 Editor 和 Runtime 两套地形 GPU 数据路径，不能直接搬 CK3 `HeightLookupTexture/PackedHeightTexture`。

---

## What We Did

### 1. 补 surface map post shader 链
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- 新增 `ApplySurfacePostProcessing`，在 `CalcRiverAdvanced` 后执行。
- 补入 CK3 后段对应函数：cloud shadow mask、terrain shadow tint、fog-of-war、map distance fog。
- surface alpha 改为从 `_WaterZoomedInZoomedOutFactor` 推导 zoom blend，并乘 `1 - _FlatMapLerp`。
- 删除旧 `_ShadowTermFallback`、`_CloudMaskFallback`、`_ZoomBlendOut` surface 参数链。

### 2. 使用 Editor 地形 height slices 计算 terrain normal
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- `RiverSurface` 内联所需 height slice 资源、bounds 和采样函数，避免继承 `EditorTerrainHeightParameters` 时引入 `Texturing` 的 TEXCOORD streams。
- `RiverRenderFeature.TryBindEditorTerrainInputs` 从 `VisibilityGroup.RenderObjects` 查找 `EditorTerrainRenderObject`，绑定 height slice textures、slice bounds、height scale、world offset 与 normal step。
- Runtime streaming terrain provider 未混入 Editor 路径。

### 3. 补 shadow-color 静态资源
**Files Changed:** `Terrain.Editor/Assets/River/Water/shadow-color.dds`, `Terrain.Editor/Assets/River/Water/shadow-color.sdtex`, `Terrain.Editor/Terrain.Editor.sdpkg`, `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`

**Implementation:**
- 从 `game/gfx/map/textures/shadow_color.dds` 复制到中性路径 `River/Water/shadow-color`。
- `.sdtex` 使用 sRGB sampling。
- `RiverResourceLoader` 加载并绑定到 `RiverSurfaceKeys.ShadowNoiseTexture`。

### 4. 明确不使用替代纹理（已被后续修正）
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Rendering/River/RiverRenderResources.cs`

**Implementation:**
- 没有创建 1x1 white FogOfWar 或 flat heightmap。
- 当时暴露了 `SurfaceFogOfWarAlphaTexture` 作为真实输入；后续修正为 river surface 完全不依赖该 strategy-layer 纹理。

---

## Decisions Made

### Decision 1: terrain normal provider 使用项目地形数据
**Context:** CK3 的 `ApplyTerrainShadowTintWithClouds` 需要 terrain normal，但 CK3 的 height lookup 是其内部压缩地形系统。
**Decision:** Editor surface 后段绑定 `EditorTerrainRenderObject` 的 height slices/bounds 并在 river shader 内差分计算 normal。
**Rationale:** 语义对齐 CK3 的 terrain-normal shadow tint，同时不引入不属于本项目的 CK3 height lookup 资源。
**Trade-offs:** Runtime streaming terrain 的 provider 需要后续单独实现。

### Decision 2: 不创建 FogOfWar/height 替代纹理（已被后续修正）
**Context:** 用户明确要求不要 fallback。
**Decision:** 当时认为 `SurfaceFogOfWarAlphaTexture` 必须由真实系统设置；后续确认战争迷雾不属于河流着色输入，已删除该接口。
**Rationale:** 河流 surface 不应因为 strategy-layer FOW 数据缺失而改变着色或阻断后段。
**Trade-offs:** 不再执行 CK3 strategy fog-of-war color adjustment；保留 river-relevant cloud shadow、terrain shadow tint 与 map distance fog。

---

## What Worked

1. **先跑 Stride shader workflow**
   - `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` 成功生成新 keys。
   - `StrideCleanAsset` + `StrideCompileAsset` 验证资产不是旧缓存。

2. **测试发现 mixin 语义冲突**
   - 初版直接继承 `EditorTerrainHeightParameters` 会让 `Texturing` 的 TEXCOORD stream 与 river vertex streams 冲突。
   - 改为内联资源/采样函数后 `river surface shader compiles through stride effect compiler` 通过。

---

## What Didn't Work

1. **直接继承 Editor terrain height mixin**
   - 失败原因：`Texturing` 注入 `TexCoord` streams，占用 TEXCOORD0/2/3/4/5，与 `RiverVertexStreams` 类型冲突。
   - 不要再在 river surface shader 中直接继承该 mixin；只复用数据资源和必要采样逻辑。

---

## Testing

- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-build`

All passed. Asset compile still reports an existing HLSL loop-unroll warning, with 0 failed commands.

---

## Next Session

### Immediate Next Steps
1. 为 Runtime streaming terrain 增加独立 river surface terrain-normal provider，不复用 Editor 8-slice 参数。
2. 捕获新 `debug.rdc`，确认 surface PS bindings 包含 `ShadowNoiseTexture` 与 Editor height slices，且不包含 `FogOfWarAlphaTexture`。

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/2026/06/19/2026-06-19-river-debug-still-black-hotedit.md`

---

## Session Statistics

**Files Changed:** 14+
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- CK3 surface 后段需要 terrain normal，但本项目用 Editor height slices 计算，不复制 CK3 height lookup。
- `SurfaceFogOfWarAlphaTexture` 已从 river surface 删除；不要重新引入战争迷雾纹理作为河流着色依赖。
- 不要让 `RiverSurface` 继承 `EditorTerrainHeightParameters`，会导致 TEXCOORD stream 冲突。
