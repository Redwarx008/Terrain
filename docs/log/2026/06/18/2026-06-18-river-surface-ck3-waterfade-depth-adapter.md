# 河流水面 CK3 WaterFade 深度语义复核
**Date**: 2026-06-18
**Session**: River surface CK3 reference follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 参考 CK3 shader 确认当前 `RiverSurface` 为什么在新截帧 `C:\Users\Redwa\Desktop\debug1.rdc` 中仍显得偏黑。

**Success Criteria:**
- 明确 CK3 `jomini_river_surface.fxh` / `jomini_water_default.fxh` 对 `Depth`、`WaterFade`、alpha 的真实处理方式。
- 如果确认本地 shader 有回归，修正并用测试/资产编译验证。

---

## Context & Background

**Previous Work:**
- [2026-06-16 河流水面黑色输出修复](../16/2026-06-16-river-surface-black-output-fix.md)
- [2026-06-18 river bottom scene lighting / tangent 修正链路](2026-06-18-river-bottom-tangent-sign-correction.md)

**Current State:**
- bottom pass 已从 scene-driven shadow/cubemap 与 tangent no-flip 方向修正过。
- 新 `debug1.rdc` 中 bottom RT 不再是纯黑，但最终 surface 覆盖到亮地形上仍显得偏暗。

---

## What We Found

### CK3 surface shader 语义

CK3 `jomini_river_surface.fxh`：

```hlsl
float Depth = CalcDepth(Input.UV);
Params._Depth = Depth * Input.Width + 0.1f;
float4 Color = CalcWater(Params)._Color;
Color.a = saturate(Depth * 2.0f / _Depth) * Input.Transparency * saturate((Input.DistanceToMain - 0.1f) * 5.0f);
```

CK3 `jomini_water_default.fxh`：

```hlsl
float3 Refraction = CalcRefraction(..., Input._Depth);
float Depth = Input._Depth;
#if defined(RIVER) && defined(JOMINI_REFRACTION_ENABLED)
    float RefractionDepth = Input._WorldSpacePos.y - RefractionWorldSpacePos.y;
    Depth = min(Depth, RefractionDepth);
#endif
float WaterFade = 1.0f - saturate((_WaterFadeShoreMaskDepth - Depth) * _WaterFadeShoreMaskSharpness);
FinalColor *= WaterFade;
float FresnelFactor = Fresnel(...) * WaterFade;
FinalColor += lerp(Refraction, Reflection, FresnelFactor);
```

**Conclusion:** CK3 没有额外的“亮度 fallback”。它用 `CalcDepth * Input.Width + 0.1` 作为 surface depth，再与 refraction depth 取更浅值进入同一个 `WaterFade` 公式。颜色 fade 和最终 surface alpha 是两条路径。

### 本地截帧证据

`debug1.rdc` 里对 surface PS 热替换输出诊断值：

- 代表像素 `(1124,478)`：当前 `WaterFade≈0.01181`，physical/effective depth 约 `0.30236`。
- 代表像素 `(1056,602)`：当前 `WaterFade≈0.0`，physical/effective depth 约 `0.29994`。
- 同一热替换里用 `max(physicalDepth, depthFactor * _WaterFadeShoreMaskDepth)` 作为视觉深度下限时，绿色通道显示中心 fade 约 `0.94..0.97`。

这解释了“水面仍很黑”：当前 surface 主水色和 fresnel 几乎被 `WaterFade` 清零，最终输出接近 bottom/refraction buffer。

### RenderDoc hot-replace confirmation

对 EID 305 surface PS 做了两个 replacement：

1. 完整 surface replacement，只把 `WaterFade` 输入从 `physicalDepth` 改为 `max(physicalDepth, depthFactor * _WaterFadeShoreMaskDepth)`。
2. 诊断 replacement，输出 `R=oldFade, G=newFade, B=effectiveDepth`。

结果：

- `(1124,478)` 原始 surface shaderOut/postMod 为 `[0.25897,0.19664,0.13147,1]`；完整 replacement 后为 `[0.48407,0.74269,0.86199,1]`。
- `(1056,602)` 原始 surface shaderOut/postMod 为 `[0.19216,0.13127,0.08049,1]`；完整 replacement 后为 `[0.23791,0.46675,0.59736,1]`。
- 诊断 replacement `(1124,478)` 为 `[0.01181,0.97418,0.30236,1]`。
- 诊断 replacement `(1056,602)` 为 `[0.0,0.94451,0.29994,1]`。

导出图：

- Baseline: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_305_0_baseline_original.png`
- Adapted color replacement: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_305_0_waterfade_adapter.png`
- Fade diagnostic replacement: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_305_0_waterfade_diagnostic.png`

结论：surface 偏黑的直接主因就是 `WaterFade` 使用偏小 physical refraction depth 后接近 0。replacement 后颜色变亮但偏蓝，说明这一步解决“被清零/黑”的主因；若目标继续是 CK3 色相，还需要单独追 water diffuse/reflection/cubemap/后处理阶段，而不是再质疑 `WaterFade` 是否参与。

---

## What We Changed

### 1. 恢复窄 ribbon 的视觉深度下限

**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

```sdsl
float ComputeRiverWaterFade(float physicalDepth, float depthFactor)
{
    float fadeSharpness = max(_WaterFadeShoreMaskSharpness, 0.0001f);
    float visualDepth = max(physicalDepth, depthFactor * _WaterFadeShoreMaskDepth);
    return 1.0f - saturate((_WaterFadeShoreMaskDepth - visualDepth) * fadeSharpness);
}
```

**Rationale:**
- CK3 fade 公式保持不变。
- `physicalDepth` 仍来自 CK3-style refraction path：`min(surfaceDepth, refractionDepth)`。
- `depthFactor` 只作为当前 Stride 窄 ribbon 的视觉深度下限，避免河心主水色被偏小 refraction depth 完全清零。
- 这不是 edge alpha depth-floor，也不是亮度乘法 fallback。

### 2. 补回防回归测试

**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

测试现在锁定：

- `ComputeRiverWaterFade(float physicalDepth, float depthFactor)`
- `float visualDepth = max(physicalDepth, depthFactor * _WaterFadeShoreMaskDepth);`
- `WaterFade` 仍使用 CK3 `1 - saturate((maskDepth - visualDepth) * sharpness)` 公式。
- 禁止退回 `ComputeRiverWaterFade(refractionFadeDepth)` 单参数调用。

---

## Decisions Made

### Decision 1: 保留 CK3 公式，适配输入 depth

**Context:** CK3 shader 公式和当前本地实现结构基本一致，但本地 capture 的 physical refraction depth 只有约 `0.30`，低于 `_WaterFadeShoreMaskDepth=0.5`，让河心水色接近 0。

**Decision:** 使用 `max(physicalDepth, depthFactor * _WaterFadeShoreMaskDepth)` 作为进入 CK3 `WaterFade` 公式的 depth。

**Trade-offs:**
- 这不是 CK3 原始 shader 的逐字实现，而是针对当前 Stride ribbon 尺度的输入语义适配。
- 它比 edge alpha depth-floor 更窄，因为不会用 alpha 或 bank fade 反推水深。

---

## Verification

通过：

- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet build Terrain.Editor\Terrain.Editor.csproj -c Debug`

Build/test 仍有既有 NuGet vulnerability、WinForms DPI、Stride shader loop unroll 等警告；无新增错误。

---

## Next Session

### Immediate Next Steps
1. 重新运行 editor 抓新帧，确认 surface PS disasm 中已出现 `ComputeRiverWaterFade(refractionFadeDepth, depthFactor)`。
2. 在新帧里复查 surface 代表像素：`WaterFade` 应不再接近 0，最终输出不应只等于 bottom/refraction。
3. 如果视觉仍比 CK3 暗，下一步不要继续调 water diffuse multiplier；应比较当前 main RT 的 terrain HDR exposure 与 CK3 surface target 的场景阶段是否同一类 render target。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- CK3 surface 的颜色 fade 公式没有额外增亮：`1 - saturate((maskDepth - Depth) * sharpness)`。
- 本地需要 cross-section `depthFactor` 下限，是因为当前 Stride ribbon/refraction depth 尺度让河心 `WaterFade≈0`。
- 不要把这次修正误解成 edge alpha depth-floor；`_BankFade=0.025` 仍保持 CK3 默认。

**Code References:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
