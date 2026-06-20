# River Bottom Scene Lighting RenderDoc Recheck
**Date**: 2026-06-18
**Session**: bottom scene-lighting RenderDoc verification
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 复核 `C:\Users\Redwa\Desktop\debug.rdc` 中 river bottom pass 的 scene-driven shadow / cubemap / sun 参数是否已经进入 GPU。

**Success Criteria:**
- 明确用户提供的 `debug.rdc` 是否包含最新 scene-lighting 改动。
- 如果不是，使用当前 build 抓新帧验证真实 GPU cbuffer。
- 修正发现的 scene-level 输入问题，不回退到 river shader 或 fallback 常量。

---

## What We Did

### 1. 复核用户提供的 `debug.rdc`
**Finding:**
- `C:\Users\Redwa\Desktop\debug.rdc` 的 `LastWriteTime` 是 `2026-06-18 11:54:13`，早于当前 Debug exe `2026-06-18 12:23:37`。
- bottom EID 276 仍是旧 scene 输入：
  - `_SceneSunColor=[20,20,20]`
  - `_SceneSunDirection=[-0.25,-0.866,0.433]`
  - `_EnvironmentIntensity=1`
  - 旧 HDR/blue Stride cubemap仍在使用。

**Conclusion:**
- 该截帧没有包含 CK3 scene-lighting 实现。

### 2. 用当前 build 抓帧验证
**Capture:**
- `C:\Users\Redwa\Desktop\debug-current-codex.rdc`

**Finding:**
- CK3 environment 已进入 scene：`_EnvironmentIntensity=20`、`_EnvironmentMipCount=10`。
- sun direction 已对齐：shader 中 `-_SceneSunDirection` 对应 CK3 `ToSunDir=[-0.8181818,0.54545456,-0.18181819]`。
- 但 `_SceneSunColor=[20,14.505,10.602]`，低于 CK3 目标 `[20,17.3568,15.0970]`。

### 3. 修正 Stride light color-space 语义
**Files Changed:**
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Root Cause:**
- CK3 cbuffer 中的 `SunDiffuse` 是 shader 线性值。
- Stride `LightComponent.SetColor` 存的是 gamma-space provider 值；`LightProcessor` 会在 linear color space 下调用 `ToLinear()` 后乘 intensity。
- 直接把 CK3 线性值传给 `SetColor` 会被二次转 linear。

**Fix:**
- 保留 `Ck3SunDiffuseLinear` 作为目标线性值。
- 新增 `Ck3SunDiffuseForStrideColorProvider = Ck3SunDiffuseLinear.ToSRgb()`。
- `ApplyCk3MapLighting` 调用 `light.SetColor(Ck3SunDiffuseForStrideColorProvider)`。
- 测试增加断言，防止以后把 CK3 线性值直接传给 `SetColor`。

### 4. 重新抓帧验证
**Capture:**
- `C:\Users\Redwa\Desktop\debug-current-codex-fixed.rdc`

**Verified GPU State:**
- bottom draws `276/290/304`:
  - `_SceneSunColor=[20,17.3567638,15.0970440]`
  - `_SceneSunDirection=[0.8181819,-0.5454546,0.1818179]`
  - `_EnvironmentIntensity=20`
  - `_EnvironmentMipCount=10`
  - `_EnvironmentSkyMatrix=identity`
- Jomini cubemap:
  - `ResourceId::284`, `512x512`, `mips=10`, `BC3_SRGB`
  - usage: `204/276/290/304` as `PS_Resource`
  - mip stats around `[0.0152,0.0212,0.0308]`, matching low-value CK3-style environment semantics after intensity.
- Shadow atlas:
  - `ResourceId::7764`, `4096x4096`, `R32_TYPELESS`
  - usage: `Clear`, shadow caster depth writes at `37/57/77/97`, then `PS_Resource` at `204/276/290/304`
  - stats min/max show non-empty depth data.

---

## Decisions Made

### Decision 1: Keep CK3 cbuffer values as linear targets, adapt at Stride API boundary
**Context:** The river shader consumes linear GPU values, but Stride light components expose gamma-space color provider values.

**Decision:** Store the CK3 target as `Ck3SunDiffuseLinear`, then pass `ToSRgb()` to `LightComponent.SetColor`.

**Rationale:** This preserves scene-driven lighting and matches CK3 cbuffer without adding shader-side compensation.

---

## Verification

- `dotnet run --project "E:\Stride Projects\Terrain\Terrain.Editor.Tests\Terrain.Editor.Tests.csproj" -c Debug` ✅
- `dotnet build "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" -c Debug` ✅
- RenderDoc MCP capture/recheck on `debug-current-codex-fixed.rdc` ✅

**Warnings:**
- Existing NuGet vulnerability warnings.
- Existing unused field/event and WinForms DPI warnings.

---

## Architecture Impact

**Updated:**
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**New Learning:**
- Do not pass CK3 linear cbuffer light color directly to `LightComponent.SetColor`; compensate with `ToSRgb()` at the scene API boundary.

---

## Next Session

1. If visual parity is still off, compare `debug-current-codex-fixed.rdc` against `ck3-river.rdc` at matching representative pixels.
2. Decompose bottom direct / diffuse IBL / specular IBL again with the corrected scene inputs.
3. Do not add river-local light multipliers unless RenderDoc proves CK3 has an equivalent scene/material parameter.

---

## Quick Reference

**Key implementation:**
- `EmbeddedStrideViewportGame.ApplyCk3MapLighting`
- `Ck3SunDiffuseForStrideColorProvider = Ck3SunDiffuseLinear.ToSRgb()`

**Critical capture:**
- `C:\Users\Redwa\Desktop\debug-current-codex-fixed.rdc`

**Gotcha:**
- The user-provided `C:\Users\Redwa\Desktop\debug.rdc` in this session was stale relative to the current executable.
