# River Review Follow-up: Dedicated WaterColor Sampler And Runtime Blend Test
**Date**: 2026-06-17
**Session**: Follow-up after subagent review findings
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 处理子代理 review 的后续两点：核实 `WaterColorTexture` sampler 语义是否真的应改 `Clamp`，并把 payload-alpha 修复从字符串断言提升一层到可执行测试。

**Success Criteria:**
- 不盲从 review 建议，先对照 CK3 shader 源码决定 sampler 语义。
- 如果 `Clamp` 结论不成立，仍然要去掉 `WaterColorTexture` 和 flow/foam sampler 的隐式耦合。
- 增加至少一条直接执行到 runtime state 级别的 river 测试。

---

## What We Verified

### 1. review 提到的 `WaterColorTexture` 改 `Clamp` 不成立
- 对照了 CK3 本地源码：
  - `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\jomini\jomini_water_default.fxh`
  - `WaterColorTexture` sampler 在该文件 `52-59` 行，`SampleModeU/V` 都是 `Wrap`
- 结论：
  - “map-space texture 就必须 clamp”不是 CK3 参考语义
  - 这条 review 建议不能直接照做

### 2. review 对“共享 sampler 语义偶合”的提醒是有效的
- 当前 `RiverSurface` 里，`WaterColorTexture` 之前确实复用了 `WaterTextureSampler`
- 这让 map-space water-color 与 tileable flow/foam/ambient-normal 在绑定层没有边界
- 即使当前地址模式继续保持 `Wrap`，也应该把这层语义拆开

### 3. review 对测试覆盖不足的判断部分成立
- 原先 payload-alpha 修复主要由源码字符串断言锁住
- 现在补了一条可执行测试，直接反射调用 `RiverRenderFeature.CreateDualSourceBlendState()`，断言 RT0 的 color/alpha blend 字段

---

## What We Changed

### 1. `WaterColorTexture` 改为 dedicated sampler，但继续保持 CK3 的 `LinearWrap`
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, generated `Terrain.Editor/Effects/RiverSurface.sdsl.cs`

**Implementation:**
- `RiverSurface.sdsl` 新增：
  - `stage SamplerState WaterColorSampler;`
- `WaterColorTexture` 的两处采样改为：
  - `WaterColorTexture.Sample(WaterColorSampler, worldUv)`
  - `WaterColorTexture.Sample(WaterColorSampler, refractionWorldUv)`
- `RiverRenderFeature.BindRiverTextures(...)` 新增：
  - `surfaceEffect.Parameters.Set(RiverSurfaceKeys.WaterColorSampler, graphicsDevice.SamplerStates.LinearWrap);`

**Rationale:**
- sampler 语义分离是对的
- 但地址模式继续保持 `LinearWrap`，因为这才和 CK3 源码一致

### 2. 新增 runtime-state 级别的 blend 测试
**Files Changed:** `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 新增测试：
  - `river dual-source blend state keeps payload alpha direct-write`
- 通过反射调用 `RiverRenderFeature.CreateDualSourceBlendState()`
- 断言：
  - `ColorSourceBlend = SecondarySourceAlpha`
  - `ColorDestinationBlend = InverseSecondarySourceAlpha`
  - `AlphaSourceBlend = One`
  - `AlphaDestinationBlend = Zero`

**Rationale:**
- 这条测试直接跑到真实 `BlendStateDescription`
- 不再只是字符串匹配

### 3. 更新字符串测试，改为断言 dedicated `WaterColorSampler`
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 断言 `RiverSurface` 声明 `WaterColorSampler`
- 断言 `WaterColorTexture.Sample(...)` 走 `WaterColorSampler`
- 断言 `RiverRenderFeature` 绑定 `RiverSurfaceKeys.WaterColorSampler`
- 断言不再通过 `WaterTextureSampler` 直接采 `WaterColorTexture`

---

## Verification

**Red phase first:**
- `dotnet run --project Terrain.Editor.Tests\\Terrain.Editor.Tests.csproj -c Debug`
- 结果：按预期失败，失败点正是 `WaterColorSampler` 尚未实现的 3 处断言

**Green verification after implementation:**
- `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet build Terrain.Editor\\Terrain.Editor.csproj -c Debug -p:UseSharedCompilation=false`
- `dotnet run --project Terrain.Editor.Tests\\Terrain.Editor.Tests.csproj -c Debug`

**Result:**
- 全部通过
- 仍保留既有 warning：
  - NuGet vulnerability warnings
  - `RiverBottom` asset compile `X3557 loop doesn't seem to do anything`

---

## Key Takeaway

- 这轮 review 里，应该修的是“共享 sampler 语义偶合”，不是机械地把 `WaterColorTexture` 改成 `Clamp`
- 对 CK3 parity 工作，**独立语义** 和 **地址模式选择** 必须分开判断

---
