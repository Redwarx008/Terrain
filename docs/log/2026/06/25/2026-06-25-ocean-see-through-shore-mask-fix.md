# Ocean 水下透视 shore mask 修复
**Date**: 2026-06-25
**Session**: 3
**Status**: ⚠️ Superseded by `2026-06-25-ocean-see-through-transition-direction.md`
**Priority**: High

---

## Session Goal

> 2026-06-25 later correction: this session identified the right failure layer but chose the wrong source fix. CK3/River semantics keep `(_WaterSeeThroughShoreMaskDepth - refractionDepth)` and require a positive `_WaterSeeThroughShoreMaskDepth`; flipping the sign reverses the shore transition.

**Primary Objective:**
- 修复 `debug.rdc` 中岸边水下透视被改坏的问题。

**Success Criteria:**
- 先用 RenderDoc 热替换验证问题来源。
- 只修确认成立的回归，不把未验证的“缺细节”方案落地。
- 保持 River、shared refraction capture、global tonemap、scene lighting 不变。

---

## Context & Background

**Capture:**
- `C:\Users\Redwa\Desktop\debug.rdc`
- Ocean draw: EID 280
- Final pass: EID 1099

**Symptom:**
- 岸边水下地形/岸坡透视被统一青绿色水色覆盖。
- 部分水面细节仍缺，但不能继续用颜色 response 或未验证的岸浪 trick 处理。

---

## What We Did

### 1. RenderDoc 复现
**Evidence:**
- 岸边像素 `(820,510)` 在 Ocean draw 前 terrain 为约 `[1.833,1.517,1.161]`，Ocean draw 后变为约 `[0.270,0.500,0.521]`。
- 另一个岸边像素 `(930,500)` 在 Ocean draw 前 terrain 为约 `[0.437,0.354,0.280]`，Ocean draw 后变为约 `[0.253,0.483,0.508]`。
- final pass 只是把 Ocean 输出 tonemap；主问题发生在 Ocean draw 280。

### 2. 分量热替换
**Hot Tests:**
- `HOT_VIS_REFRACTION`
- `HOT_VIS_WATER_COLOR_MAP`
- 修正 `see-through shore mask` 符号
- 临时 `HOT_APPROACHING_WAVES` / mask 可视化

**Findings:**
- `HOT_VIS_REFRACTION` 与 `HOT_VIS_WATER_COLOR_MAP` 在岸边像素完全同值，说明 `CalcTerrainUnderwaterSeeThrough` 的结果被替换成纯 water-color。
- 根因是 shore mask 方向写反：默认 `_WaterSeeThroughShoreMaskDepth=0` 时，任意正 `refractionDepth` 都让 `shoreMask=1`，最终 `return lerp(color, waterColorMap, 1)`，水下透视被完全覆盖。
- 修正为 `(refractionDepth - _WaterSeeThroughShoreMaskDepth)` 后，岸边 final 代表点从 `[0.302,0.443,0.455]` 降到 `[0.290,0.435,0.447]`，岸线底色/细节重新参与。
- `HOT_APPROACHING_WAVES` 的 mask 诊断大面积铺满海面，未证明是可靠 CK3 路径，本轮不落地。

### 3. 源码修复
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`

**Implementation:**
```hlsl
float shoreMask = 1.0f - saturate((refractionDepth - _WaterSeeThroughShoreMaskDepth) * _WaterSeeThroughShoreMaskSharpness);
```

**Rationale:**
- 对正 refraction depth，默认 depth threshold `0` 应保持 see-through 路径，而不是强制回到 water-color。
- 浅到接近阈值时才 fade 回 water map。

---

## Verification

**RenderDoc:**
- 热替换修正符号后，岸边透底重新出现。
- 未落地 `HOT_APPROACHING_WAVES`，因为 mask 诊断不可靠。

**Commands Run:**
```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet build Terrain.sln --no-restore
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

**Result:**
- All commands passed.
- Build/tests still emit existing NuGet vulnerability warnings and existing code warnings, but no errors.

---

## Next Session

### Immediate Next Steps
1. 用户刷新 capture 后复核岸边水下透视。
2. 如果仍缺细节，继续 RenderDoc 热替换分解 foam / reflection / fresnel / normal / water-color，不使用未验证的 approaching-wave mask。
3. 若要做岸浪，需要先找到 CK3 对应 shader 源码路径和可靠 mask，而不是使用当前临时代码。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `(_WaterSeeThroughShoreMaskDepth - refractionDepth)` 是反号，会让默认 depth `0` 覆盖所有正深度透底。
- 正确方向是 `(refractionDepth - _WaterSeeThroughShoreMaskDepth)`。
- `HOT_APPROACHING_WAVES` 在本帧没有通过 mask 验证，不要直接搬到 SDSL。
