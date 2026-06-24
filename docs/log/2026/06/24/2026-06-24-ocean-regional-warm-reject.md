# Ocean 英伦北欧区域暖色污染修正
**Date**: 2026-06-24
**Session**: Ocean regional tint follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复 `C:\Users\Redwa\Desktop\debug.rdc` 中英伦/北欧附近海域过度屎黄色的问题。

**Success Criteria:**
- 用 RenderDoc 定位黄偏是在 Ocean draw、后处理、还是底层 scene/refraction 产生。
- 保留上一轮 CK3-style 区域水色方向，但防止 land-like 暖色样本污染海面。
- 通过 shader key 生成、Stride asset 编译、build 和 editor tests。

---

## Context & Background

**Previous Work:**
- `2026-06-24-ocean-regional-water-color.md` 引入 `water_color.dds` 驱动的区域 display tint。

**Current State:**
- 区域 tint 解决了单一全局海色的问题，但在英伦/北欧附近采到了暖棕 `water_color.dds` 样本，并将它当成海水色转移到最终响应。

---

## What We Did

### 1. RenderDoc 复现与定位
**Files Changed:** none

**Findings:**
- Capture: `C:\Users\Redwa\Desktop\debug.rdc`
- Ocean draw: EID 280, PS `ResourceId::7874`
- Final output: EID 939
- 英伦/北欧坏点 `(790,360)`:
  - Ocean EID 280 output: `[0.9006, 0.6433, 0.3873]`
  - Final EID 939 output: `[0.6627, 0.5765, 0.4549]`
  - `WaterColorTexture` sample in pixel trace: `[0.1607, 0.1045, 0.0530]`
- 正常大西洋点 `(300,300)`:
  - Ocean EID 280 output: `[0.0698, 0.1745, 0.1920]`

**Conclusion:**
- 黄偏已经在 Ocean shader 输出中产生，不是后处理造成。
- 直接用 `waterColorAndSpec.rgb` 做区域 tint 会把 `R > G/B` 的 land-like 暖色样本当成海水色。

### 2. Source changes
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Implementation:**
```hlsl
float3 SanitizeOceanRegionalWaterColor(float3 regionalWaterColor)
{
    float warmExcess = regionalWaterColor.r - max(regionalWaterColor.g, regionalWaterColor.b);
    float warmReject = saturate((warmExcess - _OceanDisplayRegionalWarmRejectThreshold) * _OceanDisplayRegionalWarmRejectScale);
    return lerp(regionalWaterColor, _OceanDisplayRegionalReference, warmReject);
}
```

**New defaults:**
- `_OceanDisplayRegionalWarmRejectThreshold = 0.01f`
- `_OceanDisplayRegionalWarmRejectScale = 20.0f`

---

## Decisions Made

### Reject Warm Regional Samples Before Tinting
**Context:** The `water_color.dds` RGB map includes warm land-like or shallow/terrain-influenced samples in some sea regions. These are useful data in CK3's full strategic stack, but not safe as raw Ocean chroma in this current close-up/terrain lighting chain.

**Decision:** Treat samples where `R` is clearly greater than `max(G,B)` as warm contamination and blend them back to the neutral regional water reference before luminance-preserving tint.

**Rationale:**
- Keeps blue/green regional water variation.
- Removes the obvious British/Nordic brown failure mode.
- Does not disable the whole regional color feature.

**Trade-offs:**
- Some legitimately warm shallow seas may be less regionally tinted until a dedicated far-map/strategy water path exists.

---

## Code Quality Notes

### Testing
- Updated `OceanShaderTextTests` to lock:
  - warm reject parameters
  - `SanitizeOceanRegionalWaterColor`
  - `warmExcess = R - max(G,B)`
  - fallback to `_OceanDisplayRegionalReference`
  - regional tint still consuming `waterColorAndSpec.rgb`

### Verification
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`

All passed. Remaining warnings are existing NuGet security warnings plus existing nullable/unused-field warnings.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh runtime/editor RenderDoc and verify the British/Nordic seas no longer show the warm brown patch.
2. If some far-map shallow seas now look too neutral, tune `_OceanDisplayRegionalWarmRejectThreshold` and `_OceanDisplayRegionalWarmRejectScale` from fresh pixel traces.

---

## Quick Reference

**What Changed Since Last Doc Read:**
- Regional tint now sanitizes warm `water_color.dds` samples before chroma transfer.
- The regional water color feature remains active for cool/blue-green samples.

**Gotchas for Next Session:**
- Do not remove regional tint entirely; the CK3 far-map diagnosis still stands.
- Do not use raw warm `water_color.dds` RGB directly as Ocean chroma in this close-up shader.

