# 河流 surface 删除战争迷雾输入
**Date**: 2026-06-19
**Session**: river remove fog-of-war alpha input
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 删除 river surface 对 `FogOfWarAlphaTexture` 的依赖，避免 strategy-layer 战争迷雾影响河流着色。

**Success Criteria:**
- `RiverSurface.sdsl` 不再声明或采样 `FogOfWarAlphaTexture`。
- `RiverRenderFeature` 不再公开或绑定 `SurfaceFogOfWarAlphaTexture`。
- shader key 文件更新，Stride asset compile 和测试通过。

---

## Context & Background

**Previous Work:**
- `docs/log/2026/06/19/2026-06-19-river-surface-post-chain-editor-terrain.md`

**Current State:**
- 上一轮为补 CK3 surface 后段引入了 FOW 资源接口，但用户指出战争迷雾按理不应影响河流着色。
- CK3 的 strategy fog-of-war color adjustment 属于更高层地图可见性逻辑；本项目当前 river surface 应保持水面、云影、地形阴影 tint 和距离雾链路，不把 FOW 作为必需 texture。

---

## What We Did

### 1. 删除 shader FOW texture 链路
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- 删除 `_FogOfWarPattern*`、`_FogOfWarAlphaMin`、`_FogOfWarContrast`、`_FogOfWarBrightness`、`_HasFogOfWarAlphaTexture`。
- 删除 `FogOfWarAlphaTexture` 与 `FogOfWarAlphaSampler`。
- 删除 `SampleFogOfWarAlphaRaw`、`ApplyFogOfWarPattern`、`GetFogOfWarAlpha`、`FogOfWarColorAdjustment`、`ApplyFogOfWar`。
- `GetCloudShadowMask` 改为只接收 world coordinate，云影不再被 FOW alpha 门控。
- `ApplySurfacePostProcessing` 保留 alpha/zoom fade、cloud shadow、terrain shadow tint、map distance fog。

### 2. 删除 C# 绑定接口
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- 删除 `SurfaceFogOfWarAlphaTexture` 属性和 backing field。
- 删除缺 FOW warning 与 `_HasFogOfWarAlphaTexture` 参数绑定。
- 删除 `FogOfWarAlphaTexture` / sampler 绑定。

### 3. 更新回归测试
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- `river surface shader includes target map post chain` 现在断言不含 `FogOfWarAlphaTexture`、`FogOfWarAlphaSampler`、`_HasFogOfWarAlphaTexture`、`ApplyFogOfWar`、`SampleFogOfWarAlphaRaw`。
- 同一测试断言 `GetCloudShadowMask(worldPosition.xz)` 不再接受 FOW alpha。

---

## Decisions Made

### Decision 1: 战争迷雾不是 river surface 输入
**Context:** CK3 截帧 surface 后段里包含 strategy fog-of-war，但本项目没有同等 strategy visibility 系统，且用户明确指出战争迷雾不应影响河流着色。
**Decision:** River surface 删除 FOW texture/sampler/capability switch 和 FOW color adjustment。
**Rationale:** 河流着色应由水体、折射、云影、terrain shadow tint 和距离雾决定；缺失 strategy-layer FOW 不应导致河流变黑或改变能量。
**Trade-offs:** 不再逐字执行 CK3 strategy FOW 后段；保留河流相关的可见 map-lighting 后段。

---

## Testing

- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-build`

All passed. Asset compile still reports the existing HLSL loop-unroll warning; test build still reports existing NuGet vulnerability and unrelated C# warnings, with 0 errors.

---

## Next Session

### Immediate Next Steps
1. 用新 `debug.rdc` 或 RenderDoc MCP 确认 surface PS bindings 不再包含 `FogOfWarAlphaTexture`。
2. 继续分析 surface 发黑是否来自 cloud shadow / terrain shadow tint / distance fog 的能量参数。
3. 为 Runtime streaming terrain 增加独立 river surface terrain-normal provider，不复用 Editor 8-slice 参数。

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/2026/06/19/2026-06-19-river-debug-still-black-hotedit.md`

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 不要把 `FogOfWarAlphaTexture`、`FogOfWarAlphaSampler`、`_HasFogOfWarAlphaTexture` 或 `SurfaceFogOfWarAlphaTexture` 重新引入 river surface。
- Cloud shadow 现在独立于 FOW alpha：`GetCloudShadowMask(worldPosition.xz)`。
- River surface 后段仍需要 Editor terrain height slices 计算 terrain normal，并绑定 `River/Water/shadow-color`。
