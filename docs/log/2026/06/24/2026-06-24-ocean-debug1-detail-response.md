# Ocean debug1 深水细节响应修正
**Date**: 2026-06-24
**Session**: Ocean debug1 RenderDoc follow-up
**Status**: Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 复核 `C:\Users\Redwa\Desktop\debug1.rdc` 与 CK3 final 显示色仍有差距的原因，并在动源码前用 RenderDoc 热替换验证修复方向。

**Success Criteria:**
- 不再机械照抄 CK3 HDR raw draw 参数。
- 保持 Ocean-only，不修改 shared refraction capture、River、global tonemap、scene light，也不加入 province/FOW/flatmap/`_WaterToSunDir`。
- 通过 shader key 生成、Stride asset 编译、runtime build 和 editor tests。

---

## Context & Background

**Previous Work:**
- `2026-06-24-ocean-debug2-display-response-correction.md` 记录了从常量 raw target response 改为深/浅 gain+bias 的过程。

**Current State:**
- `debug1.rdc` 已经使用上一轮 gain/bias display response，不是旧常量 target 版本。
- Ocean cbuffer 确认 `_WaterFlowNormalScale=0.025`、`_OceanWaveSpeedScale=0.2`、`_OceanFlowSpeedScale=0.2`、`_OceanSceneLightingScale=0.18`、`_OceanRefractionColorScale=0.30`。

---

## What We Did

### 1. RenderDoc 重新诊断
**Files Changed:** none

**Findings:**
- `debug1.rdc` final 的深水均值已经接近 CK3 final 深水，不是单纯偏黑问题：
  - 本地 bottom deep final 均值约 `[0.164, 0.240, 0.265]`
  - CK3 bottom deep final 均值约 `[0.154, 0.218, 0.250]`
- 差距主要在空间变化：
  - 本地深水 G/B 标准差约 `0.005..0.013`
  - CK3 深水 G/B 标准差约 `0.038`

### 2. RenderDoc 热替换探针
**Files Changed:** `tmp/ocean_hotreplace_display_response_v3.hlsl`

**Attempt A: reflection boost**
- 把 cubemap reflection 乘大并放宽 fresnel。
- 结果：远端掠射点 `(1400,300)` 从 `[0.192,0.251,0.275]` 到 `[0.220,0.275,0.306]`，但前景深水 `(1200,820)` 只从 `[0.157,0.231,0.259]` 到 `[0.161,0.235,0.263]`。
- 结论：单纯增强 reflection intensity 会局部过亮，不能解决大面积深水细节不足。

**Attempt B: unified detail response**
- 对深浅水都使用 reference/base/detail gain。
- 结果：深水更接近 CK3，但黑底近岸折射点 `(980,560)` 被压到 `[0.133,0.157,0.153]`。
- 结论：浅水/黑底折射区域必须保留 additive shallow bias，不能统一做对比增强。

**Attempt C: deep-only detail response + shallow bias**
- 深水使用 `base + (finalColor - reference) * detailGain`。
- 浅水继续使用 `finalColor * 0.8 + [0.16,0.22,0.20]`。
- RenderDoc final EID 997 gate:
  - `(1200,820)=[0.141,0.224,0.247]`
  - `(1400,300)=[0.200,0.255,0.275]`
  - `(980,560)=[0.235,0.286,0.267]`
- 结论：采用 Attempt C。

### 3. Source changes
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Implementation:**
```hlsl
float shallowMask = saturate(1.0f - baseRefractionDepth / max(_OceanDisplayShallowDepth, 0.0001f));
float3 deepColor = _OceanDisplayDeepBase + (finalColor - _OceanDisplayDeepReference) * _OceanDisplayDeepDetailGain;
float3 shallowColor = finalColor * _OceanDisplayShallowGain + _OceanDisplayShallowBias;
float3 displayColor = max(lerp(deepColor, shallowColor, shallowMask), float3(0.0f, 0.0f, 0.0f));
return lerp(finalColor, displayColor, saturate(_OceanDisplayResponseStrength));
```

**New defaults:**
- `_OceanDisplayDeepBase = float3(0.115f, 0.185f, 0.213f)`
- `_OceanDisplayDeepReference = float3(0.065f, 0.128f, 0.146f)`
- `_OceanDisplayDeepDetailGain = 2.0f`

---

## Decisions Made

### Use Deep-Only Detail Response
**Context:** Deep-water mean was acceptable but spatial variation was too low.

**Decision:** Replace old deep gain/bias with deep base/reference/detail gain, while keeping shallow gain/bias.

**Rationale:**
- Preserves Ocean lighting/refraction/reflection detail instead of replacing color with a constant target.
- Avoids crushing shallow/black-bottom refraction zones.
- Keeps the fix isolated to `OceanSurface`.

**Trade-offs:**
- This still compensates current post chain inside Ocean rather than matching CK3's full tonemap/post chain.
- Fresh runtime RenderDoc is still needed for final visual review after building the app.

---

## What Worked

1. **Pixel-history gate before source changes**
   - Per-event final samples were reliable even when swapchain `export_texture` returned a stale gray image.

2. **Separate deep and shallow response**
   - Deep water needs detail amplification; shallow water needs additive lift.

---

## What Didn't Work

1. **Reflection-only boost**
   - It increased grazing-angle brightness but did not fix foreground deep-water detail.

2. **Unified contrast response**
   - It darkened black-bottom shallow refraction points because those pixels rely on shallow additive bias.

---

## Code Quality Notes

### Testing
- Updated `OceanShaderTextTests` to lock:
  - deep base/reference/detail gain defaults
  - deep detail formula
  - existing shallow response
  - Ocean-only isolation constraints

### Verification
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`

All passed. Remaining warnings are existing NuGet security warnings and nullable/unused field warnings.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh runtime/editor RenderDoc after this source build and inspect Ocean final output visually.
2. If deep water still lacks CK3 wave variance, inspect whether normal/reflection inputs are too smooth before display response.
3. If land exposure remains far from the CK3 screenshot, treat that as a separate scene/terrain lighting/post issue, not an Ocean shader fix.

---

## Quick Reference

**What Changed Since Last Doc Read:**
- Deep display response is no longer `finalColor * 1.15 + bias`.
- Deep water now uses base/reference/detail gain; shallow water keeps gain/bias.

**Gotchas for Next Session:**
- Do not use `export_texture` on the swapchain as proof after shader replacement in this capture; it produced a stale gray image.
- Use `pixel_history(eventId=997, ...)` or a fresh capture for validation.

