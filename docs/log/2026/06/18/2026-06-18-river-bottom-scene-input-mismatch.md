# River Bottom Scene Input Mismatch
**Date**: 2026-06-18
**Session**: debug.rdc bottom lighting recheck
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用新截帧 `C:\Users\Redwa\Desktop\debug.rdc` 复核 river bottom pass 当前 scene-driven shadow / cubemap 绑定是否进入 GPU，并解释为什么 shader 与 bottom 三张纹理接近 CK3 后仍然不像 CK3 的黄橙河床。

**Success Criteria:**
- 证明当前 GPU shader 是否已经使用 CK3-like BRDF。
- 证明当前 bottom pass 绑定的是哪张 shadow / environment 资源。
- 区分 bottom 纹理差异、shader 差异和 scene lighting/environment 输入差异。

---

## What We Did

### 1. 复核当前 `debug.rdc`
**Capture:** `C:\Users\Redwa\Desktop\debug.rdc`

**Findings:**
- D3D11 capture，`65` draws，bottom pass 仍是 EID `276`。
- EID `276` PS 为 `ResourceId::7827`，disasm 显示已进入 CK3-like BRDF：`0.25f * saturate(properties.g)`、GGX direct、dominant specular IBL、Burley roughness-to-mip，无旧 `_BottomSpecularIntensity`，无 final `* 3.0f`。
- EID `276` 资源绑定：
  - t0 `BottomDiffuseTexture`
  - t1 `BottomNormalTexture`
  - t2 `BottomPropertiesTexture`
  - t3 `EnvironmentMapTexture`
  - t4 `SceneShadowMapTexture`
- t3 实际资源为 `ResourceId::276`，`R16G16B16A16_FLOAT` cubemap，`256x256`，mips `9`。
- t4 实际资源为 `ResourceId::7770`，`R32_TYPELESS` shadow atlas，`4096x4096`；resource usage 显示先作为 shadow depth target 写入，再在 EID `276` 作为 PS resource 读取。
- 当前 bottom cbuffer 关键值：
  - `_SceneSunColor=[20,20,20]`
  - `_SceneSunDirection=[-0.25,-0.866,0.433]`
  - `_EnvironmentIntensity=1`
  - `_EnvironmentSkyMatrix=identity`
  - `_EnvironmentMipCount=9`
  - shadow cascades / world-to-shadow matrices / atlas texel size 均已填入真实数据。
- 当前代表像素 `(471,282)` 的 bottom shaderOut 约 `[0.313956,0.256207,0.203957,6.770526]`，postMod 约 `[0.313721,0.256104,0.203857,6.769531]`。
- 当前 `BottomProperties` GPU 资源 `ResourceId::529` 为 `BC1_UNORM`，mip0 G 通道范围约 `0.1098..0.2863`。
- 当前 scene cubemap `ResourceId::276` mip7/face2 范围约 `[0.4297..0.6216, 0.7822..1.0127, 1.2354..1.5166]`，明显偏蓝且能量高。

### 2. 复核 CK3 `ck3-river.rdc`
**Capture:** `C:\Users\Redwa\Desktop\ck3-river.rdc`

**Findings:**
- CK3 bottom pass EID `332`，PS `ResourceId::46745`。
- CK3 资源绑定结构与当前等价：BottomDiffuse / BottomNormal / BottomProperties / EnvironmentMap / ShadowTexture。
- CK3 cbuffer 关键值：
  - `SunDiffuse=[1,0.867838,0.754852]`
  - `SunIntensity=20`
  - effective sun color `[20,17.3568,15.0970]`
  - `ToSunDir=[-0.818182,0.545455,-0.181818]`
  - `CubemapIntensity=20`
  - `CubemapYRotation=identity`
- CK3 environment `ResourceId::6427` 为 `BC1_SRGB` cubemap，`512x512`，mips `10`。
- CK3 mip7/face2 为常量 `[0.017700,0.020264,0.030762]`，乘 `CubemapIntensity=20` 后约 `[0.354,0.405,0.615]`。
- CK3 代表像素 `(770,615)` bottom shaderOut 约 `[0.158601,0.104821,0.054605,46.882835]`，黄橙倾向主要来自 warm direct sun；IBL 是小补项。

### 3. 本地文件资产对比
**Files Compared:**
- `Terrain.Editor/Assets/River/Bottom/bottom-diffuse.dds`
- `Terrain.Editor/Assets/River/Bottom/bottom-normal.dds`
- `Terrain.Editor/Assets/River/Bottom/bottom-properties.dds`
- CK3 `game/gfx/map/rivers/river_bottom_*.dds`

**Findings:**
- 本地 bottom 三张 DDS 与 CK3 `game/gfx/map/rivers/` 下对应文件 SHA256 完全一致。
- `bottom-properties.dds` hash 为 `A7EA014F7A175B979A90263C98D8C6CFE29F5C6D727E63E79E98AAEFE5E24621`，等于 CK3 `river_bottom_gloss.dds`。
- 本地 `Terrain/Resources/skybox_texture_hdr.dds` hash 为 `F1765B5AD690F6DEF139CD31F2FEB6638A8D76E3863584391741627A394C515D`，未匹配 CK3 environment DDS。
- 本地 `River/Environment/reflection-specular.dds` hash 等于 CK3 `qwantani_8k_nosun_cube_specular.dds`，但当前 bottom EID `276` 实际绑定的是 scene skybox cubemap `ResourceId::276`，不是该 fallback `ResourceId::547`。

### 4. Stride scene / skybox 路径检查
**Files Inspected:**
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain/Assets/MainScene.sdscene`
- `E:\WorkSpace\stride\sources\engine\Stride.Assets\Skyboxes\SkyboxGenerator.cs`
- `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Lights\LightSkyboxRenderer.cs`

**Findings:**
- Editor 优先加载 `MainScene` 和 `GraphicsCompositor`；当前 capture 的 `_SceneSunColor=[20,20,20]` 与 `MainScene.sdscene` 的白色 directional light + intensity `20` 一致。
- `MainScene.sdscene` 的 `LightSkybox` 没有显式 `Intensity`，因此 bottom cbuffer 中 `_EnvironmentIntensity=1` 是正确的 scene-driven 结果，不是 river 绑定漏参。
- `RiverRenderFeature` 绑定的是 `LightSkybox.Skybox.SpecularLightingParameters[SkyboxKeys.CubeMap]`，不是 `BackgroundComponent.Texture`，也不是 river fallback reflection cubemap。
- Stride `SkyboxGenerator` 会对 skybox source 做 GGX specular prefilter，并生成 runtime `SpecularLightingParameters` cubemap；这意味着把 CK3 已经预过滤的 `environment_terrain_sunny.dds` 直接塞进 `.sdsky` 可能会被二次预过滤，不能默认认为会与 CK3 `JominiEnvironmentMap` 等价。

---

## Decisions Made

### Decision 1: 不修改 shader
**Context:** 当前 disasm 和 cbuffer 已经证明 CK3-like BRDF、real shadow、scene cubemap intensity/rotation 都进入了 GPU。

**Decision:** 本轮不改 `RiverBottom.sdsl`。

**Rationale:** 当前差异来自 scene 输入不等价：current 是白色太阳 + HDR/blue scene skybox，CK3 是 warm sun + low-value `BC1_SRGB` Jomini environment map + intensity `20`。继续改 shader 会把 scene setup 问题伪装成 river 专用 workaround。

### Decision 2: 先把 CK3 parity 目标定义为 scene setup 问题
**Context:** bottom 三张 DDS hash 与 CK3 一致，shader 结构也已接近 CK3；剩余主要差异来自 `SunDiffuse/SunIntensity/ToSunDir/CubemapIntensity/JominiEnvironmentMap`。

**Decision:** 下一步若要继续贴近 CK3，应优先处理 editor/runtime scene 的 light 和 environment，而不是恢复 river-local fallback 常量。

**Trade-offs:** 直接把 CK3 prefiltered cubemap 接入 Stride `LightSkybox` 需要谨慎，因为 Stride skybox asset compiler 会重新 prefilter。

---

## What Worked ✅

1. **按 pass / cbuffer / texture stats 分层复核**
   - 先证明 GPU shader 已更新，再看绑定资源和 cbuffer，避免把 scene 输入问题误判成 shader stale。

2. **文件 hash + RenderDoc resource stats 双重对比**
   - 文件 hash 证明 bottom 三张源 DDS 没错；RenderDoc stats 证明运行时 environment 输入仍不等价。

---

## Problems Encountered & Solutions

### Problem 1: 当前 bottom 仍不像 CK3 黄橙色
**Symptom:** 当前代表像素输出约 `[0.314,0.256,0.204]`，CK3 代表像素约 `[0.159,0.105,0.055]`，当前明显更白/绿/蓝。

**Root Cause:** 当前 scene-driven 输入不是 CK3 scene：
- 当前 sun 是白色 `[20,20,20]`，CK3 effective sun 是 warm `[20,17.3568,15.0970]`。
- 当前 scene cubemap mip7 是 HDR float 且偏蓝；CK3 Jomini env 是低值 BC1 sRGB cubemap，再乘 `CubemapIntensity=20`。
- 当前 skybox light intensity 为 `1`，CK3 cubemap intensity 为 `20`，但两者资源能量本身不同，不能只改一个标量。

**Solution:** 本轮只记录诊断，不做 shader 修改。下一步应先决定 Stride scene 如何表达 CK3 `JominiEnvironmentMap`，再改 scene asset / loader。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本 session log。
- [x] 追加 learnings：不要把 `BackgroundComponent.Texture` / river fallback cubemap 当作 bottom 的 scene environment。
- [ ] 暂不更新 `ARCHITECTURE_OVERVIEW.md` / `CURRENT_FEATURES.md`，因为系统状态未发生实现变化。

### New Anti-Pattern Discovered
**New Anti-Pattern:** 把 scene skybox source / river fallback / CK3 JominiEnvironmentMap 混为一谈。
- `BackgroundComponent.Texture` 只影响背景。
- `LightSkybox.Skybox.SpecularLightingParameters[SkyboxKeys.CubeMap]` 才是 current river bottom 绑定的 scene cubemap。
- CK3 `EnvironmentMap_Texture` 是 Jomini environment map，不一定等价于 Stride 对任意 skybox source 重新 GGX prefilter 后的 cubemap。

---

## Next Session

### Immediate Next Steps
1. 决定 CK3 parity 的 scene environment 来源：直接使用 CK3 `environment_terrain_sunny.dds` 作为 prefiltered cubemap，还是找一张未预过滤源图让 Stride 生成等价 specular cubemap。
2. 将 editor/runtime 默认 directional light 改为 CK3 warm sun color 和方向，或者从 map lighting 配置加载这些值。
3. 给 skybox/environment 强度建立 scene-level 参数，避免再用 river-local fallback scalar。
4. 截新帧后复核 `_SceneSunColor`、`_SceneSunDirection`、`_EnvironmentIntensity`、`EnvironmentMapTexture` 的 resource format/stats。

### Questions to Resolve
1. Stride 是否允许在 runtime 直接构造/加载 `Skybox.SpecularLightingParameters[SkyboxKeys.CubeMap]` 为 CK3 prefiltered cubemap，跳过 `.sdsky` 二次预过滤？
2. CK3 `JominiEnvironmentMap` 在当前参考帧具体绑定的是 `environment_terrain_sunny.dds` 还是另一个 runtime-composed resource？当前离线 mip7 解码强烈指向 `environment_terrain_sunny.dds`。

---

## Session Statistics

**Files Changed:** 2 documentation files
**Code Files Changed:** 0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `debug.rdc` EID `276` 已经绑定 real shadow atlas `ResourceId::7770` 和 scene skybox cubemap `ResourceId::276`。
- 当前 shader 已进入 CK3-like BRDF，无 `_BottomSpecularIntensity`，无 final `* 3.0f`。
- bottom 三张 DDS 与 CK3 `game/gfx/map/rivers/` 文件 hash 一致。
- 差异主因是 scene input：current white sun + HDR blue Stride skybox，CK3 warm sun + low-value BC1 sRGB Jomini env + intensity `20`。

**Gotchas for Next Session:**
- 不要改 river shader 去补这个差异；先改 scene light/environment。
- 不要以为 `River/Environment/reflection-specular.dds` 是当前 bottom 实际 cubemap；EID `276` 没用它。
- 不要直接把 CK3 prefiltered env cubemap 塞进 `.sdsky` 后假设等价，Stride skybox compiler 会重新 prefilter。

---

## Links & References

### Related Sessions
- [River Bottom CK3 BRDF Alignment](./2026-06-18-river-bottom-ck3-brdf-alignment.md)

### Code References
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- `Terrain/Assets/MainScene.sdscene`
- `E:\WorkSpace\stride\sources\engine\Stride.Assets\Skyboxes\SkyboxGenerator.cs`
- `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Lights\LightSkyboxRenderer.cs`
