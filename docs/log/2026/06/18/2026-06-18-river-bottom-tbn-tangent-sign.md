# River Bottom TBN Tangent Sign Fix
**Date**: 2026-06-18
**Session**: river bottom ribbon-normal capture follow-up
**Status**: ⚠️ Superseded
**Priority**: High

---

## Correction 2026-06-18

This session's fixed `-normalize(streams.RiverTangent)` conclusion was incomplete. It was derived from `debug-current-codex-ribbon-normal_frame870.rdc`, where the inspected river segment had a `-X` tangent and fixed shader inversion happened to improve the sampled pixels.

The later `C:\Users\Redwa\Desktop\debug.rdc` capture shows the opposite case: EID 276 already contains the fixed inversion in the compiled shader, but a representative bottom pixel has PS tangent `[0.9993387,0.0005159,0.0292324]` and still outputs `[0.0742,0.0553,0.0416]`. Hot-replacing the same capture with no hard-coded flip raises the direct-light `nDotL` from about `0.17` to `0.72/0.82`.

CK3 source `jomini\gfx\FX\jomini\jomini_river_bottom.fxh` uses `float3 Tangent = normalize(Input.Tangent);` directly. Current code therefore uses `float3 tangent = normalize(streams.RiverTangent);`; if tangent direction is still inconsistent, the fix belongs in river segment/mesh direction semantics, not in `RiverBottom` shader sign fallback.

---

## Session Goal

**Primary Objective:**
- 复核 `C:\Users\Redwa\Desktop\debug-current-codex-ribbon-normal_frame870.rdc` 中 ribbon normal 修复后 bottom pass 仍偏黑的根因。

**Success Criteria:**
- 用 RenderDoc 证明 scene-driven shadow / cubemap / ribbon normal 之后的剩余差异来自哪里。
- 在修改 SDSL 前用 shader hot-replace 验证修复方向。
- 落地最小修复并跑 Stride shader/asset 验证。

---

## Context & Background

**Previous Work:**
- `RiverSceneSeed` 已改用 Presenter depth 写 camera-relative seed alpha。
- bottom lighting 已切到 scene-driven sun / shadow / Jomini cubemap，并修正 Stride light color-space。
- `RiverMeshService` 已保留 sloped centerline tangent，并用 ribbon normal 替代 terrain height-diff normal。
- CK3 material `_BankFade=0.025` 已同步到 render settings/object 和 bottom/surface SDSL defaults。

**Current State:**
- 新 capture 显示 bottom pass EID 276 已绑定真实 BottomDiffuse/Normal/Properties、scene cubemap 和 shadow map。
- 代表像素的 PS 输入 normal 已接近 `[0,1,0]`，但 bottom 输出仍为暗蓝黑约 `[0.013,0.016,0.017]`。

---

## What We Did

### 1. RenderDoc 复核新 capture

**Capture:**
- `C:\Users\Redwa\Desktop\debug-current-codex-ribbon-normal_frame870.rdc`

**Findings:**
- Capture 有效：D3D11，65 draws，78 events，HIGH/MEDIUM log 为空。
- bottom draw 是 EID 276：2808 indices，RT `R16G16B16A16_FLOAT`。
- EID 276 绑定：
  - `BottomDiffuseTexture`
  - `BottomNormalTexture`
  - `BottomPropertiesTexture`
  - `EnvironmentMapTexture`
  - `SceneShadowMapTexture`
- Pixel `(528,301)`：
  - seed event 249 postMod `[1.035,0.886,0.685,6.672]`
  - bottom event 276 shaderOut `[0.01339,0.01596,0.01676,7.599]`
  - PS normal `[-0.00337,0.99999,-0.00009]`
  - PS tangent `[-0.99963,-0.00338,-0.02693]`
- Pixel `(562,239)` 同样为暗输出，normal 已接近向上。

### 2. Shader hot-replace 隔离 tangent 符号

**Experiment:**
- 用最小 HLSL replacement 保持同一 PS 输入签名和纹理绑定。
- 只计算 bottom normal-map TBN 与 CK3 warm sun direct diffuse。
- 对比当前 `normalize(input.RiverTangent)` 和翻转 `-normalize(input.RiverTangent)`。

**Result:**
- 当前 tangent 符号：
  - `(528,301)` `nDotL≈0.0185`
  - `(562,239)` `nDotL≈0.0135`
- 翻转 tangent：
  - `(528,301)` `nDotL≈0.861`，RT 约 `[0.292,0.247,0.160]`
  - `(562,239)` `nDotL≈0.881`，RT 约 `[0.294,0.228,0.147]`

**Conclusion:**
- 剩余主因不是 diffuse、shadow、cubemap 或 ribbon normal。
- bottom normal map 的 tangent-space x 分量需要按 CK3 TBN 方向投影；当前生成的 centerline tangent 对 bottom shader 来说方向相反。

### 3. 落地修复

**Files Changed:**
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
```hlsl
// CK3 bottom normal maps expect the tangent basis opposite to the generated river centerline direction.
float3 tangent = -normalize(streams.RiverTangent);
```

**Rationale:**
- 保留 mesh tangent 数据作为真实中心线方向，避免影响 surface 或未来几何逻辑。
- 只在 bottom normal-map TBN 中翻转 tangent，精确匹配 RenderDoc hot-replace 证明的差异点。
- 增加文本测试锁住 `RiverBottom` 的 CK3 TBN orientation。

---

## Decisions Made

### Decision 1: 在 bottom shader TBN 中翻转，不改 mesh tangent
**Context:** mesh tangent 是中心线方向；bottom normal map 的 CK3 TBN 期望与当前生成方向相反。

**Decision:** `RiverBottom.sdsl` 使用 `-normalize(streams.RiverTangent)`，`RiverMeshService` 继续输出真实 sloped centerline tangent。

**Rationale:** 这是最小、局部、已被 RenderDoc hot-replace 验证的修复，避免改变 surface/mesh 语义。

---

## Verification

- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" /t:StrideCleanAsset /p:Configuration=Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" /t:StrideCompileAsset /p:Configuration=Debug` ✅
- `dotnet run --project "E:\Stride Projects\Terrain\Terrain.Editor.Tests\Terrain.Editor.Tests.csproj" -c Debug` ✅
- `dotnet build "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" -c Debug` ✅

**Warnings:**
- Existing NuGet vulnerability warnings.
- Existing unused field/event and WinForms DPI warnings.
- Existing Stride shader warning X3557 about loop unroll.

---

## Architecture Impact

**Updated:**
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**New Learning:**
- Do not assume bottom TBN is correct just because ribbon normal is correct.
- Verify `RiverTangent` sign with RenderDoc hot-replace and `nDotL` before changing lighting energy.

---

## Next Session

1. Capture a fresh frame after this build and verify EID 276 disassembly contains `-normalize(streams.RiverTangent)` behavior.
2. Compare bottom RT color again against CK3 representative pixels.
3. If remaining difference persists, decompose direct / diffuse IBL / specular IBL after tangent fix rather than adding fallback multipliers.

---

## Quick Reference

**Key implementation:**
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `float3 tangent = -normalize(streams.RiverTangent);`

**Critical evidence:**
- `debug-current-codex-ribbon-normal_frame870.rdc`
- Current tangent `nDotL≈0.0185/0.0135`
- Flipped tangent `nDotL≈0.861/0.881`

**Gotcha:**
- This is not a mesh tangent reversal. It is a CK3 bottom normal-map TBN convention inside `RiverBottom` only.
