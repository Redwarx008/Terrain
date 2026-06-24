# Ocean 岸边透底过渡方向修复
**Date**: 2026-06-25
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复更新后的 `debug.rdc` 中 Ocean 岸边水下透视从水面颜色过渡到半透的反向问题。

**Success Criteria:**
- 先用 RenderDoc 热替换验证，不直接改 SDSL。
- 保留 CK3/River 的 see-through 公式结构，不新增颜色 response 或后处理 trick。
- 只影响 Ocean，不改 River、shared refraction capture、global tonemap 或 scene lighting。

---

## Context & Background

**Capture:**
- `C:\Users\Redwa\Desktop\debug.rdc`
- API: D3D11
- Ocean draw: EID 280
- Final pass: EID 997
- No HIGH severity GPU log messages.

**Symptom:**
- 岸边贴岸处先被均一水体青色覆盖，离岸稍深的水下山体反而更明显。
- 代表像素 `(750,436)` Ocean 前 terrain 为约 `[1.095, 0.867, 0.691]`，Ocean 后变成 `[0.269, 0.500, 0.512]`。

---

## What We Did

### 1. RenderDoc 热替换验证

**Hot candidates:**
- 旧 Ocean 语义：`(_WaterSeeThroughShoreMaskDepth - refractionDepth)`，但 depth 仍为 `0.0`。
- 上一轮错误候选：`(refractionDepth - _WaterSeeThroughShoreMaskDepth)`。
- CK3/River 语义加正阈值：保留 `(_WaterSeeThroughShoreMaskDepth - refractionDepth)`，分别测试 depth `3 / 6 / 12 / 20`。

**Evidence:**
- CK3 源码 `jomini_water_default.fxh` 使用：
```hlsl
float WaterSeeThroughShoreMask = 1.0 - saturate((_WaterSeeThroughShoreMaskDepth - Depth) * _WaterSeeThroughShoreMaskSharpness);
Color = lerp(Color, WaterColorMap, WaterSeeThroughShoreMask);
```
- 本项目 `RiverSurface.sdsl` 也使用同一符号，默认 `_WaterSeeThroughShoreMaskDepth = 20.0f`。
- Ocean 的问题不是 CK3 公式方向，而是 Ocean 默认阈值为 `0.0f`，导致任意正深度都回到 water color。
- 热替换 `depth=3` 已能让岸边先透底、外侧保留水体颜色；`3` 到 `20` 在当前岛屿区域平均差异很小，`3` 是保守最小正阈值。

### 2. 源码修复

**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`

**Implementation:**
```hlsl
stage float _WaterSeeThroughShoreMaskDepth = 3.0f;
float shoreMask = 1.0f - saturate((_WaterSeeThroughShoreMaskDepth - refractionDepth) * _WaterSeeThroughShoreMaskSharpness);
return lerp(color, waterColorMap, shoreMask);
```

**Rationale:**
- 浅水 `refractionDepth < 3` 时 mask 低，保留 density 计算出的透底 `color`。
- 水深超过阈值后 mask 逐渐回到 `waterColorMap`，避免外海过度透明。
- 该修复沿用 CK3/River 的水体路径，不新增 display/close/far response。

---

## Verification

**RenderDoc:**
- `debug.rdc` 热替换已导出：
  - `tmp/renderdoc/shore-transition-20260625/contact-depth-variants.png`
  - `tmp/renderdoc/shore-transition-20260625/hot-ck3-depth3-final-997.png`
- 接受候选：CK3 公式 + `_WaterSeeThroughShoreMaskDepth = 3.0f`。

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
- Existing NuGet vulnerability warnings and the existing `TerrainRenderFeature.cs(675,40)` nullable warning remain; no new errors.

---

## What Didn't Work

1. **翻转 mask 符号**
   - It made the transition slope backwards: shore became water color first, then deeper water became see-through.
   - The previous session log has been marked superseded.

2. **No-mask / full see-through**
   - It reveals bottom too broadly and is not the CK3 path.

---

## Architecture Impact

- Ocean remains Ocean-only.
- River, shared refraction capture, global tonemap, scene lighting and strategy-layer resources remain unchanged.
- Learning doc updated: do not compensate a zero see-through threshold by flipping the CK3 formula.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- CK3 and River use `(_WaterSeeThroughShoreMaskDepth - Depth)`, not the reversed sign.
- Ocean needs a positive `_WaterSeeThroughShoreMaskDepth`; `0.0f` makes every positive refraction depth fade back to water color.
- Current validated conservative value: `3.0f`.

---

## Links & References

- Previous superseded log: `docs/log/2026/06/25/2026-06-25-ocean-see-through-shore-mask-fix.md`
- Learning doc: `docs/log/learnings/ocean-ck3-renderdoc-validation.md`
- CK3 source: `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_water_default.fxh`
