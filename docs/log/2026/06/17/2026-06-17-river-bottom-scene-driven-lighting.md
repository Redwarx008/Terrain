# River Bottom Scene-Driven Lighting
**Date**: 2026-06-17
**Session**: River bottom scene-driven lighting
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 把 `RiverBottom` 的 bottom lighting 从 river-local fallback 常量切到更接近 CK3 的 scene-driven 语义。

**Secondary Objectives:**
- 补上真实 directional shadow、scene skybox intensity、scene skybox rotation。
- 清理上一轮 `DirectLightGroup` permutation 方案留下的 shader 资产冲突。

**Success Criteria:**
- `RiverBottom` 直接消费 scene directional light / shadow atlas / skybox 参数。
- `StrideAssetUpdateGeneratedFiles`、`StrideCleanAsset`、`StrideCompileAsset`、`dotnet build`、测试全部通过。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-bottom-yellow-lighting-decomposition.md](./2026-06-17-river-bottom-yellow-lighting-decomposition.md)
- Related: [adr-014-river-rendering-architecture.md](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- 上一轮已经确认 bottom 发灰的主因在 lighting balance，而不是 bottom 贴图资产本身。
- 同时确认现有 scene 已有可用的 `Directional Light + ShadowMapRenderer + LightSkybox` 管线。

**Why Now:**
- 用户明确要求 bottom lighting 不再依赖 river 专用 fallback 常量，而要补齐 CK3 更接近的 scene-driven 语义。

---

## What We Did

### 1. 放弃 `DirectLightGroup` permutation 路径
**Files Changed:** `Terrain.Editor/Terrain.Editor.csproj`, `Terrain.Editor/Effects/*`

**Implementation:**
- 删除临时文件：
  - `RiverBottomEffect.sdfx`
  - `RiverBottomEffectKeys.cs`
  - `RiverBottomNoLightGroup.sdsl`
  - `TextureProjectionGroup.sdsl`
  - 对应生成的 `*.cs`
- `RiverRenderFeature` 的 `bottomEffect` 回到直接使用 `RiverBottom`。

**Rationale:**
- 这条路会把 custom effect 带进 Stride 的 include/permutation 细节，前一轮已经出现 `TextureProjectionGroup.sdsl` 导入冲突，不值得继续追。

### 2. 在 `RiverBottom.sdsl` 内实现最小 scene shadow receiver
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`

**Implementation:**
- 新增 scene-driven 输入：
  - `_SceneSunDirection`
  - `_SceneSunColor`
  - `_SceneShadowCascadeCount`
  - `_SceneShadowBlendCascades`
  - `_SceneShadowDepthBias`
  - `_SceneShadowOffsetScale`
  - `_SceneShadowCascadeSplits[4]`
  - `_SceneWorldToShadowCascadeUV[4]`
  - `SceneShadowMapTexture`
  - `_SceneShadowMapTextureSize`
  - `_SceneShadowMapTextureTexelSize`
- 在 shader 内直接实现 5x5 PCF、cascade 选择、cascade blend、normal offset。
- 保留现有 bottom IBL 路径，并继续使用 `_EnvironmentSkyMatrix / _EnvironmentIntensity / _EnvironmentMipCount`。

**Rationale:**
- 目标不是复用 Stride 的 exact mixin，而是把 runtime 语义对齐到同一套 scene light / shadow / skybox 数据。
- 手工绑定 shadow receiver 能避开 `DirectLightGroup` / `TextureProjectionGroup` 的模块依赖，同时保住真实 shadow。

### 3. `RiverRenderFeature` 直接绑定 scene light/shadow/skybox 数据
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- 从 `ForwardLightingRenderFeature.ShadowMapRenderer` 获取 scene shadow atlas。
- 通过 `FindShadowMap(renderView.LightingView ?? renderView, directionalLight)` 找到当前 directional light 的 shadow map。
- 读取 `LightDirectionalShadowMapRenderer.ShaderData`，把 cascade splits、world-to-shadow、depth bias、offset scale、atlas size/texel size 直接灌给 `RiverBottom`。
- scene skybox 继续优先从 `LightSkybox.Skybox.SpecularLightingParameters.Get(SkyboxKeys.CubeMap)` 取 cubemap，并绑定 intensity / rotation / mip count。
- `SetTexture(...)` 改为统一支持 `null`，避免 object 参数残留上一帧资源。

**Rationale:**
- 这一步把 bottom lighting 的输入源彻底切到了 scene pipeline，而不是 river 专用太阳/阴影 fallback。

### 4. 更新文本测试与 shader 编译测试期望
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 把原先围绕 `BottomLightGroup` / `RiverBottomEffect` 的断言改成 scene-driven shadow/skybox 绑定断言。
- 保留 `lighting_x3` 的断言，但更新为新的 `CalculateRiverBottomLighting(positionWS, ...)` 签名。

**Rationale:**
- 测试要跟新的语义对齐，否则会继续把代码拉回旧的 permutation 路径。

---

## Decisions Made

### Decision 1: 放弃继续复用 Stride `DirectLightGroup` mixin
**Context:** 上一轮为了把 direct light group 组合进 `RiverBottom`，引入了 effect wrapper 与本地 `TextureProjectionGroup.sdsl`。

**Options Considered:**
1. 继续追 `DirectLightGroup` / `RiverBottomEffect` 编译链
2. 自己写一套 river-local fallback shadow
3. 直接绑定 scene `RenderLight + LightDirectionalShadowMapRenderer.ShaderData + LightSkybox`

**Decision:** 选择 3
**Rationale:** 这是最短路径，也最符合用户要求的 scene-driven 语义。
**Trade-offs:** 阴影接收器逻辑需要在 `RiverBottom.sdsl` 里维护一份最小实现。

### Decision 2: `RiverRenderSettings` 上保留 bottom 本地参数，但只作为 tuning multiplier
**Context:** 现有 UI / render object / settings 已经有 `BottomEnvironmentIntensity`、`BottomSpecularIntensity`、`BottomNormalStrength` 等参数链路。

**Options Considered:**
1. 一次性删掉全部 bottom 本地参数
2. 保留参数，但不再让它们承担 sun/shadow 的主语义

**Decision:** 选择 2
**Rationale:** 这样改动最小，也不会破坏现有 editor 参数链路；scene light/shadow 负责主语义，本地参数只做 tuning。

---

## What Worked ✅

1. **手工 scene binding + 最小 shadow receiver**
   - What: 直接绑定 directional light、shadow atlas、skybox 参数
   - Why it worked: 避开了 Stride effect permutation/include 的不稳定点
   - Reusable pattern: Yes

2. **先跑 Stride asset rebuild 再谈 shader 语义**
   - What: 依次执行 `StrideAssetUpdateGeneratedFiles`、`StrideCleanAsset`、`StrideCompileAsset`
   - Impact: 快速确认问题已经从“资产链坏了”收敛成“shader/runtime 语义本身正确”

---

## What Didn't Work ❌

1. **继续复用 `RiverBottomEffect + DirectLightGroup`**
   - What we tried: 用 effect permutation 把 `LightDirectionalGroupRenderer` 产出的 shader source compose 进 `RiverBottom`
   - Why it failed: include 链对 `TextureProjectionGroup` / material stream 依赖太深，custom effect 下容易出模块冲突
   - Lesson learned: 这类单一 pass scene-driven 改造，优先手工绑定 scene 数据，比硬接 Stride mixin 更稳

---

## Problems Encountered & Solutions

### Problem 1: `TextureProjectionGroup.sdsl` 导入冲突导致 `StrideCompileAsset` 失败
**Symptom:** asset compiler 报同名 shader 被引擎和本地工程同时导入。
**Root Cause:** 为了给 `DirectLightGroup` 补 include，临时加了本地 `TextureProjectionGroup.sdsl`，与引擎自带模块写到了同一目标路径。
**Solution:**
- 删除本地 `TextureProjectionGroup.sdsl` 及其生成文件
- 删除 `RiverBottomEffect` 这套 wrapper/permutation 路径
- 回到单一 `RiverBottom` shader

**Why This Works:** 不再重复定义引擎已有 shader 模块，asset compiler 恢复到单一路径。

### Problem 2: 需要真实 shadow 语义，但不能依赖 internal shadow keys
**Symptom:** 项目侧不能直接消费引擎内部的 `ShadowMapReceiver*Keys`。
**Root Cause:** 这些 key 在引擎里是 internal。
**Solution:**
- 在 `RiverBottom.sdsl` 自己声明 `_SceneShadow*` 参数
- C# 侧从 `LightDirectionalShadowMapRenderer.ShaderData` 提取数据并绑定到这些自定义 key

**Why This Works:** 绕开了 internal API 限制，同时仍然吃到引擎真实 shadow runtime 数据。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `ARCHITECTURE_OVERVIEW.md`
- [x] Update `CURRENT_FEATURES.md`

### Architectural Decisions That Changed
- **Changed:** river bottom lighting 输入源
- **From:** river-local sun/shadow fallback + scene cubemap fallback
- **To:** scene directional light + real shadow atlas + scene skybox intensity/rotation，river-local 参数仅做 tuning
- **Scope:** `RiverBottom.sdsl`、`RiverRenderFeature.cs`、相关测试与 shader 资产配置

---

## Code Quality Notes

### Testing
- **Verified Commands:**
  - `dotnet msbuild Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
  - `dotnet build Terrain.Editor.csproj -c Debug -p:UseSharedCompilation=false`
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
- **Result:** 全部通过；`StrideCompileAsset` 仅有 HLSL loop unroll warning，`dotnet build` / tests 仅有既有 NuGet/WinForms warnings。

### Technical Debt
- **Remaining:** bottom 仍保留 `lighting_x3` 最终能量增益；这说明和 CK3 的 scene energy parity 还没完全收敛。
- **Paid Down:** river bottom 不再依赖 river-local sun/shadow fallback 作为主 lighting 语义。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 用新的 runtime 重新抓一帧 RenderDoc，确认 bottom draw 现在已经绑定 real shadow atlas 和 scene skybox rotation/intensity。
2. 在 scene-driven 语义稳定后，再决定是否继续收敛 `lighting_x3`。
3. 如果河岸泄漏仍明显，继续单独定位 pre-bottom seed payload 与 CK3 的剩余差异。

### Questions to Resolve
1. `lighting_x3` 是否还能继续降回更接近 CK3 原始 energy 的形式？
2. 当前 5x5 PCF 是否已经足够，还是要进一步匹配 scene filter mode 的动态切换？

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverBottom` 不再走 `RiverBottomEffect` / `BottomLightGroup` permutation 路线。
- 真实 shadow 数据来自 `LightDirectionalShadowMapRenderer.ShaderData`。
- scene skybox cubemap / intensity / rotation 已直接进入 `RiverBottom`。

**Gotchas for Next Session:**
- 不要再把 `TextureProjectionGroup.sdsl` 本地复制回项目。
- 先跑 Stride asset rebuild，再判断 shader 语义是否有问题。

---

## Code References

- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

---
