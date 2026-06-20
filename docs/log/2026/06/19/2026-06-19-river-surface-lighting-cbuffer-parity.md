# 河流 surface lighting cbuffer 语义修正
**Date**: 2026-06-19
**Session**: RenderDoc `debug.rdc` follow-up
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 复核 `C:\Users\Redwa\Desktop\debug.rdc` 中河流仍然偏黑的问题，确认 bottom 修正是否生效，并继续对齐 CK3 surface shader 语义。

**Success Criteria:**
- 确认当前黑色来源是在 bottom/refraction，还是 surface `CalcWater`。
- 对照 CK3 cbuffer / shader 源码找出不等价处。
- 落地低风险 SDSL 修正并通过 Stride asset 编译与测试。

---

## Context & Background

**Previous Work:**
- `docs/log/2026/06/18/2026-06-18-river-bottom-shadow-hotedit-direct-light.md`
- `docs/log/2026/06/18/2026-06-18-river-target-shader-semantics.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Current State:**
- 当前帧：`C:\Users\Redwa\Desktop\debug.rdc`
- 目标帧：`C:\Users\Redwa\Desktop\ck3-river.rdc`
- CK3 loose shader 可读：`E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river_surface.fxh`、`jomini_water_default.fxh`。

---

## What We Did

### 1. RenderDoc 复核当前 `debug.rdc`
**Files Changed:** none

**Findings:**
- 新帧 pass：scene seed EID `248`，bottom EID `274/287`，surface EID `315/333`。
- bottom PS EID `287` 的 bindings 已无 `SceneShadowMapTexture`，说明上一轮禁用非等价 Stride shadow 的正式 SDSL 已进入 GPU。
- bottom RT EID `287` stats：RGB min 约 `[0.1866,0.1320,0.0873]`，max 约 `[1.2168,1.1201,0.9922]`；bottom 已不是近黑输入。
- surface EID `333` 仍有很暗像素，stats min 约 `[0.0472,0.0437,0.0343]`。
- direct-refraction replacement 证明 surface 可以把正常 refraction 显著压低；当前问题转移到 surface composition。

### 2. 对照 CK3 surface cbuffer 和源码
**Files Changed:** none

**Findings:**
- CK3 EID `460` surface cbuffer 的 water 参数和本地当前参数基本一致：water color、see-through、refraction、wave、gloss/spec/cubemap 等都已对上。
- CK3 advanced river flow normal 源码/disasm 为单次采样：`Input.UV.yx * float2(1,-1) * float2(Input.Width,1) * _FlowNormalUvScale`，再加 `GlobalTime * _FlowNormalSpeed`；当前 `SampleFlowNormal` 与该方向等价。
- 真正不等价处在 `CalcWater` lighting：本地仍保留旧 `cloudShadowMask` helper，并额外用 `sunIntensityMask=smoothstep(0.05,0.1,glossMap)` 门控太阳光；CK3 没有这个门控，直接使用 water cbuffer 参数。

### 3. 落地 surface lighting 修正
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- 新增 `_WaterZoomedInZoomedOutFactor` 与 `_WaterToSunDir`，默认值来自 CK3 surface cbuffer。
- 删除 `GetWaterGlossScale`、`GetWaterSpecularFactor`、`GetWaterCubemapIntensity`、`GetWaterSunIntensity` 等旧 helper。
- `CalcWater` 改为：
  - `fowGlossiness = lerp(_WaterGlossBase, glossMap, _WaterZoomedInZoomedOutFactor)`
  - `nonLinearGlossiness *= _WaterGlossScale`
  - direct sun 使用 `_DefaultEnvironmentSunDiffuse * _DefaultEnvironmentSunIntensity`
  - specular 使用 `_WaterSpecularFactor`
  - reflection 使用 `_WaterCubemapIntensity`
- 文本测试改为锁住上述目标语义，并禁止 `sunIntensityMask` / old helper 回归。

---

## Decisions Made

### Decision 1: surface lighting 直接消费 CK3 water cbuffer 参数
**Context:** cbuffer 一致但画面仍偏暗，说明公式仍不等价。
**Decision:** 删除本地 shadow/cloud wrapper，不再从 `cloudShadowMask` 派生 gloss/spec/reflection/sun intensity。
**Rationale:** CK3 `jomini_water_default.fxh` 在当前 river surface 命中路径直接消费 `_Water*` 参数；多一层本地 helper 会让 shader 语义偏离。
**Trade-offs:** 仍未移植 CK3 river_surface.shader 后段的 shadow tint / cloud / fog-of-war / map distance fog。

---

## What Worked

1. **先看 bindings 和 texture stats**
   - 直接确认 bottom shader 已无非等价 shadow，并且 bottom RT 已不近黑，避免继续在 bottom 上误修。

2. **cbuffer + loose shader + disasm 三方对照**
   - cbuffer 证明参数已对上；源码/disasm 暴露剩余 gap 是公式层面，而不是资源或参数缺失。

---

## Testing

- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "-t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" -p:Configuration=Debug -p:Platform=x64`
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj -t:StrideCleanAsset -p:Configuration=Debug -p:Platform=x64`
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj -t:StrideCompileAsset -p:Configuration=Debug -p:Platform=x64`
- `dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug --no-build`

**Result:**
- Asset compile: 911 succeeded, 0 failed.
- Tests: all PASS.
- Remaining warnings: existing NuGet vulnerability warnings, existing C# warnings, and one existing shader loop warning.

---

## Next Session

### Immediate Next Steps
1. 重新截帧验证新 surface shader 是否进入 GPU，重点看 EID `333` 对应 surface disasm 是否已无 `sunIntensityMask` 和 old helper。
2. 如果画面仍偏黑，继续移植 CK3 `river_surface.shader` 后段：`ApplyTerrainShadowTintWithClouds`、cloud mask、fog-of-war、distance fog。
3. 若用户仍看到流动方向不对，优先查 mesh `RiverUV` / centerline segment 方向，而不是再改 surface flow UV 公式；当前 advanced flow formula 已与 CK3 源码/disasm 等价。

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 新 `debug.rdc` 已证明 bottom 修正生效，当前暗色主要在 surface composition。
- CK3 surface cbuffer 与本地参数基本一致；这次修的是公式消费方式，不是资源。
- `RiverSurface.sdsl` 现在禁止 `sunIntensityMask` 和旧 `GetWater*` helper。
- 下一步需要新的 `debug.rdc` 来确认正式 shader 编译进 GPU 后的画面结果。
