# 河流 surface CK3 CalcWater 语义差异复核
**Date**: 2026-06-18
**Session**: RenderDoc diagnosis
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 对比 `C:\Users\Redwa\Desktop\debug1.rdc` 与 `C:\Users\Redwa\Desktop\ck3-river.rdc`，逐 pass 判断当前河流与 CK3 的 shader/参数是否语义等价。

**Success Criteria:**
- 找出当前效果差距来自 seed、bottom、refraction 还是 surface。
- 修改 SDSL 前先用 RenderDoc hot-replace 验证关键假设。

---

## Context & Background

**Previous Work:**
- [surface waterFade depth adapter](2026-06-18-river-surface-ck3-waterfade-depth-adapter.md)
- [surface refraction shore mask](2026-06-18-river-surface-refraction-shore-mask.md)
- [ADR-014 river rendering architecture](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- `debug1.rdc` 当前河流链路为 `248 seed -> 249 copy -> 276 bottom -> 305 surface`。
- `ck3-river.rdc` 参考链路为 `304/317 terrain/pre-bottom -> 332/334/336/338 bottom -> 460/462/464/466 surface`。

---

## What We Did

### 1. RenderDoc pass 对齐

**Files Changed:** documentation only

**Findings:**
- current surface event `305` 读取 half-res `ResourceId::7823`，写 full-res `ResourceId::4057`。
- CK3 surface 组 `460/462/464/466` 读取 half-res `ResourceId::49006`，写 full-res `ResourceId::49000`。
- 旧日志坐标在 `debug1.rdc` 上不再可靠；重新从导出的 RT 选取实际命中点。

### 2. CBuffer / shader 语义对比

**Current surface `305`:**
- River/refraction 参数与 CK3 大体一致：`_FlowNormalUvScale=0.4`、`_FlowNormalSpeed=0.075`、`_Depth=0.15`、`_WaterRefractionScale=500`、fade/see-through 参数匹配。
- 但 surface cbuffer 只有 34 个变量，缺少 CK3 `pdx_hlsl_cb11` 的完整 water 参数。
- current 默认水色为 `WaterColorShallow=[0,0.3,0.5,0.7]`、`WaterColorDeep=[0,0.05,0.15,0.85]`。

**CK3 surface `466`:**
- `pdx_hlsl_cb10` river 参数匹配。
- `pdx_hlsl_cb11` water 参数包括 `_WaterSpecular=0.05`、`_WaterSpecularFactor=0.01`、`_WaterGlossBase=0.7`、三层 `_WaterWave*`、`_WaterFoam*`、`_WaterFlowNormalFlatten=1.5`、`_WaterReflectionNormalFlatten=3` 等。
- CK3 riverwater 实际水色为 `_WaterColorShallow=[0.0055146,0.0078107,0.0120865]`、`_WaterColorDeep=[0.0001385,0.0001975,0.0002263]`。

### 3. RenderDoc hot-replace 验证

**Hot-replace:**
- 在 current `305` 上把 surface PS 替换成只输出 `RefractionTexture.Sample(RefractionSampler, SV_Position.xy / float2(1672,996)).rgb`。
- 不修改 SDSL/C#。

**Representative pixels:**
- current 原始 surface：
  - `(678,522)` -> `[0.260,0.499,0.634]`
  - `(841,454)` -> `[0.293,0.520,0.660]`
  - `(1078,362)` -> `[0.284,0.523,0.649]`
  - `(1315,263)` -> `[0.267,0.517,0.655]`
  - `(1479,119)` -> `[0.275,0.526,0.659]`
- hot-replace 后同点：
  - `(678,522)` -> `[0.296,0.217,0.139]`
  - `(841,454)` -> `[0.285,0.199,0.128]`
  - `(1078,362)` -> `[0.298,0.217,0.130]`
  - `(1315,263)` -> `[0.280,0.198,0.120]`
  - `(1479,119)` -> `[0.268,0.187,0.123]`
- CK3 对照：
  - `338` half-res `(55,369)` bottom/refraction -> `[0.271,0.185,0.100,a81.75]`
  - `466` full-res `(110,738)` surface -> `[0.0223,0.0280,0.0305]`
  - `464` full-res `(930,810)` surface -> `[0.00874,0.01793,0.02160]`

**Conclusion:**
- `debug1.rdc` 的主要可见偏色来自 current surface pass。
- bottom/refraction 的代表颜色已经接近 CK3 raw bottom/refraction 量级；current surface 把它推成高饱和蓝青，而 CK3 surface 用完整 `CalcWater` 管线输出低能量暗水色。

---

## Decisions Made

### Decision 1: 不做“只改水色”的 SDSL 补丁
**Context:** current 与 CK3 的水色 cbuffer 差两个数量级。

**Decision:** 本次只记录诊断，不改 `RiverSurface.sdsl`。

**Rationale:**
- CK3 surface 差异不是单个 `WaterColorShallow/Deep` 参数；current 缺少完整 `CalcWater` 的 cbuffer、wave、foam、sun/specular/gloss、FoW/cloud/fog 与 composition。
- 只改水色会让画面变暗，但 shader 仍然不等价。

---

## What Worked ✅

1. **重新选取 capture 内实际命中点**
   - 旧日志坐标在 `debug1.rdc` 不再命中 surface draw。
   - 先导出 RT，再用 pixel history 选点，避免误把 terrain draw 当 surface。

2. **direct-refraction surface hot-replace**
   - 直接证明 current surface 是高饱和偏色来源。
   - 同时保持 bottom RT alpha/path 不被破坏，结论更干净。

---

## What Didn't Work ❌

1. **复用旧 capture 坐标**
   - 旧 `(1124,478)`、`(1056,602)`、`(172,299)` 等点在 `debug1.rdc` 只命中 terrain/copy 或 depth-failed bottom，不适合继续做归因。

---

## Architecture Impact

### Documentation Updates
- Updated `docs/ARCHITECTURE_OVERVIEW.md`
- Updated `docs/CURRENT_FEATURES.md`
- Updated `docs/log/learnings/stride-river-rendering-patterns.md`

### New Pattern
**Pattern 34: bottom/refraction 已接近后，用 direct-refraction hot-replace 重新归因 surface**
- 已写入 learnings。

---

## Next Session

### Immediate Next Steps
1. 用 RenderDoc hot-replace 构建一个更接近 CK3 `CalcWater` 的 current surface replacement，先验证完整 water path 对代表像素的方向是否正确。
2. 若 hot-replace 成立，再按 TDD 增加 text tests，要求 `RiverSurface.sdsl` 暴露 CK3 `pdx_hlsl_cb11` 等价参数并禁止简化 `RiverApplyNeutralLighting` 作为最终 water lighting。
3. 按 `stride-shader-asset-workflow` 修改 `RiverSurface.sdsl`、`.sdsl.cs`、`RiverRenderSettings/Object/Feature`，运行 Stride asset compile 与测试。

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`
- CK3:
  - `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\jomini\jomini_river_surface.fxh`
  - `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\jomini\jomini_water_default.fxh`
  - `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_water.fxh`

---

## Session Statistics

**Files Changed:** 4 documentation files
**Production Code Changed:** none
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `debug1.rdc` root cause is now surface `CalcWater` parity gap, not bottom/refraction raw color.
- current surface uses simplified two ambient normals, two flow samples, simplified foam/reflection and `RiverApplyNeutralLighting`.
- CK3 surface uses `CalcRiverAdvanced -> CalcWater` with full `pdx_hlsl_cb11` water cbuffer.
- Do not fix this with only `WaterColorShallow/Deep`; port/validate the full water path.

**Artifacts:**
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_305_original.png`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_305_refraction_only.png`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_466_0.png`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_338_0.png`

---
