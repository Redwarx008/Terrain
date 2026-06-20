# River Bottom CK3 Scene Lighting Inputs
**Date**: 2026-06-18
**Session**: scene-level CK3 lighting implementation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续上一轮 `debug.rdc` 诊断：不要再改 river shader 或 fallback 常量，把 bottom lighting 的剩余 CK3 差异落到 scene-level sun / environment 输入上。

**Success Criteria:**
- CK3 Jomini environment 作为 scene asset 加入 bundle。
- Editor scene 的 directional light / skybox light 使用 CK3 capture 里的 warm sun、cubemap intensity、cubemap resource 语义。
- River bottom 仍只从 scene lighting 读取，不新增 river-local fallback 参数。
- 测试、asset compile、build 通过。

---

## What We Did

### 1. 新增 scene-level Jomini environment asset
**Files Changed:**
- `Terrain.Editor/Assets/Scene/Environment/jomini-environment-terrain-sunny.dds`
- `Terrain.Editor/Assets/Scene/Environment/jomini-environment-terrain-sunny.sdtex`
- `Terrain.Editor/Terrain.Editor.sdpkg`

**Implementation:**
- 从 CK3 `game/gfx/map/environment/environment_terrain_sunny.dds` 复制到 editor scene environment assets。
- `.sdtex` 使用 `!ColorTextureType`，`UseSRgbSampling: true`，保持 CK3 capture 中 `BC1_SRGB` environment 语义。
- 将 `Scene/Environment/jomini-environment-terrain-sunny` 加入 `Terrain.Editor.sdpkg` `RootAssets`，确保 `Content.Load<Texture>` 可直接加载并打包进 bundle。

### 2. Editor scene 应用 CK3 map lighting
**Files Changed:**
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

**Implementation:**
- 新增 scene-level CK3 lighting constants：
  - `SunDiffuse = [1,0.86783814,0.7548521]`
  - `SunIntensity = 20`
  - `ToSunDir = [-0.8181818,0.54545456,-0.18181819]`
  - `CubemapIntensity = 20`
- `ApplyCk3MapLighting(scene)` 在 asset scene 和 fallback scene 初始化后运行。
- 对 scene 中的 `LightDirectional` 设置 warm color / intensity，并用 `Quaternion.BetweenDirections(LightProcessor.DefaultDirection, -Ck3ToSunDirection)` 让 Stride `RenderLight.Direction` 与 CK3 `ToSunDir` 语义匹配。
- 对 scene 中的 `LightSkybox` 设置 intensity `20`，并将 `Skybox.SpecularLightingParameters[SkyboxKeys.CubeMap]` 直接指向 `jomini-environment-terrain-sunny` texture。
- 保留 `Debug.Assert` 作为 scene precondition：scene 必须有 directional light 和 skybox light；不在 river path 里静默 fallback。

### 3. 测试覆盖
**Files Changed:**
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 新增测试 `river bottom lighting uses ck3 scene environment inputs`。
- RED：测试先因缺少 scene environment descriptor 失败。
- GREEN：新增 asset/root 和 scene lighting 初始化后通过。

---

## Decisions Made

### Decision 1: 直接暴露 CK3 prefiltered cubemap，不走 `.sdsky` 二次 prefilter
**Context:** Stride `SkyboxGenerator` 会把 skybox source 再做 GGX prefilter。CK3 `EnvironmentMap_Texture` 已经是 shader 直接采样的 Jomini environment resource。

**Decision:** 运行时创建 `Skybox`，直接把 CK3 `environment_terrain_sunny` texture 写入 `SpecularLightingParameters[SkyboxKeys.CubeMap]`。

**Rationale:** 避免把已经预过滤的 CK3 environment 当作原始 skybox 再处理，导致 mip 能量/色彩继续偏离。

### Decision 2: 缺 scene light 用 assert，不在 river path fallback
**Context:** 用户明确要求不要继续靠 fallback 常量或大量 try/catch。

**Decision:** `ApplyCk3MapLighting` 只更新 scene light；缺 directional/skybox light 时用 `Debug.Assert` 暴露。

**Rationale:** Bottom lighting 应该来自 scene。缺 scene light 是场景配置问题，不应由 river shader 或 river resource fallback 掩盖。

---

## Verification

- `dotnet run --project "E:\Stride Projects\Terrain\Terrain.Editor.Tests\Terrain.Editor.Tests.csproj" -c Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" /t:StrideCleanAsset /p:Configuration=Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" /t:StrideCompileAsset /p:Configuration=Debug` ✅
- `dotnet build "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" -c Debug` ✅

**Warnings:**
- 既有 NuGet vulnerability warnings。
- 既有 Stride shader loop-unroll warning。
- 既有 C# unused-field/event warnings。

---

## Architecture Impact

**Updated:**
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**New Pattern Reinforced:**
- CK3 bottom 颜色 parity 的剩余差异应优先修 scene-level sun/environment，而不是继续向 river shader 注入专用增益或 fallback 常量。

---

## Next Session

1. 启动 editor 截新 `debug.rdc`。
2. 复核 EID bottom cbuffer：
   - `_SceneSunColor` 应接近 `[20,17.3568,15.0970]`
   - `_SceneSunDirection` 应使 `-_SceneSunDirection` 接近 CK3 `ToSunDir`
   - `_EnvironmentIntensity` 应为 `20`
   - `EnvironmentMapTexture` 应使用新增 scene Jomini environment asset
3. 若仍偏蓝/偏亮，再用 RenderDoc 分解 direct / diffuse IBL / specular IBL，不要直接改全局亮度。

---

## Quick Reference

**Key implementation:**
- `EmbeddedStrideViewportGame.ApplyCk3MapLighting`
- `Scene/Environment/jomini-environment-terrain-sunny`

**Critical constraint:**
- River bottom lighting 仍是 scene-driven；新增资产是 scene environment，不是 river fallback。
