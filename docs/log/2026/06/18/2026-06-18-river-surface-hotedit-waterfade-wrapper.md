# 河流 surface 热修改复核：WaterFade 与 CK3 wrapper 缺口
**Date**: 2026-06-18
**Session**: RenderDoc hot-replace diagnosis
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 在继续修改 `RiverSurface.sdsl` 前，用 RenderDoc shader replacement 验证当前黑水面和 CK3 的实际差异来源。

**Success Criteria:**
- 不靠猜测或直接改 SDSL，先确认 bottom/surface 黑色来自哪个 shader 阶段或输入。

---

## Context & Background

**Previous Work:**
- `docs/log/2026/06/18/2026-06-18-river-target-shader-semantics.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Current State:**
- 本地 capture: `C:\Users\Redwa\Desktop\debug-river-after-surface-alpha_frame798.rdc`
- CK3 capture: `C:\Users\Redwa\Desktop\ck3-river.rdc`

---

## What We Did

### 1. 排除 FoamMap 坐标是主因
**Files Changed:** none

**Implementation:**
- 对本地 surface draw 做 shader replacement，只输出 `1 - FoamMap.r`。
- 对 CK3 surface draw 做相同 replacement，并另外导出红色覆盖 mask，只在河面像素上统计。

**Findings:**
- 本地河面区域 `1 - FoamMap.r` 均值约 `173/255`。
- CK3 河面区域 `1 - FoamMap.r` 均值约 `184/255`。
- 去掉本地 Y flip 或去掉 `0.5` map-unit 缩放后，本地统计没有向 CK3 显著靠近。

**Rationale:**
- 早期全图统计被背景污染，不能拿整张 RT 的均值判断 FoamMap 坐标错。基于 surface 覆盖 mask 后，FoamMap 分布和 CK3 同量级。

### 2. 定位本地黑点来自 surface PS 自身输出
**Files Changed:** none

**Implementation:**
- 对本地黑点 `(1000,500)` 使用 `pixel_history` 和 `debug_pixel`。

**Findings:**
- 该像素实际由 surface event `351` 写入。
- surface PS 输出约 `[0.00946, 0.01795, 0.02075, 1.0]`，不是 blend 后被压黑。
- trace 反推该点 `worldDepth≈0.227`，`_WaterFadeShoreMaskDepth≈0.5`，`_WaterFadeShoreMaskSharpness≈5`，`waterFade` 被压到接近 `0`。

### 3. 热替换验证 depth/width 对 WaterFade 的影响
**Files Changed:** none

**Implementation:**
- 生成 waterFade-only 诊断 shader。
- 使用当前宽度输出 waterFade 灰度。
- 使用“参与 depth 的宽度翻倍”输出 waterFade 灰度。

**Findings:**
- 当前宽度：河面 mask 内 waterFade 均值约 `24/255`，`57.8%` 近黑。
- 宽度翻倍：河面 mask 内 waterFade 均值约 `141/255`，`40.2%` 高亮。
- 这说明本地黑水面与 surface depth/width 输入强相关。

### 4. 确认 CK3 surface pass wrapper 仍未完整移植
**Files Changed:** none

**Implementation:**
- 读取 `game/gfx/FX/river_surface.shader`、`jomini_river_surface.fxh`、`jomini_water_default.fxh`。
- 对照 CK3 event `460/466` disasm。

**Findings:**
- CK3 `river_surface.shader` 在 `CalcRiverAdvanced(Input)._Color` 后继续执行：
  - shadow map projection + `CalculateShadow`
  - `GetCloudShadowMask`
  - `ApplyTerrainShadowTintWithClouds`
  - `lerp(Color.rgb, CloudyColor, CloudMask * 0.8)`
  - `ApplyFogOfWar`
  - `ApplyMapDistanceFogWithoutFoW`
- 本地 `RiverSurface.sdsl` 当前在 `CalcWater` 后基本直接输出，仅有本地 fallback shadow/cloud 参数，不等价于 CK3 wrapper。

---

## Decisions Made

### Decision 1: 不把 FoamMap 坐标当作当前主因
**Context:** foam=0 热替换曾明显消黑，但那次使用的是完整替换 shader，不是单变量关 foam。
**Decision:** 继续归因为 waterFade/refraction/wrapper 输入，而不是改 FoamMap UV。
**Rationale:** mask 限定统计后，本地和 CK3 的 `1 - FoamMap.r` 是同量级。

### Decision 2: 后续 SDSL 修改必须补 CK3 surface wrapper
**Context:** 用户要求 surface/bottom shader 完全参考 CK3。
**Decision:** 不能只说 `CalcWater` 等价；`river_surface.shader` 的 pass 后处理也是同一个 CK3 surface PS 的一部分。
**Trade-offs:** 需要补充或模拟 CK3 的 FogOfWar、cloud shadow、terrain shadow tint 和 distance fog 输入；如果资源/scene 输入暂缺，应显式绑定 fallback，而不是省略该阶段。

---

## What Worked

1. **红色覆盖 mask 统计**
   - 避免全图背景污染 FoamMap 诊断。

2. **waterFade-only replacement**
   - 直接证明当前黑色区域和 WaterFade/depth 输入有关。

---

## What Didn't Work

1. **用全图均值判断 FoamMap**
   - CK3 背景是暗地形，本地背景是亮地形，全图均值会误导。
   - 后续比较纹理采样诊断图必须先限定 draw 覆盖 mask。

---

## Next Session

### Immediate Next Steps
1. 补 `RiverSurface.sdsl` 的 CK3 `river_surface.shader` wrapper 阶段，至少先以明确 fallback 参数实现 shadow/cloud/FOW/fog 的同结构路径。
2. 继续检查 `RefractionTexture` 在黑点处为什么给出极暗颜色；waterFade 为 0 时最终颜色完全依赖 refraction。
3. 分析 `RiverMeshService` 的宽度单位和 `RiverPixelType` 是否和 CK3 `Input.Width` 一致，避免用 shader 补偿掩盖 mesh 输入尺度错误。

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/2026/06/18/2026-06-18-river-target-shader-semantics.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 本轮没有正式修改 SDSL，只做 RenderDoc hot-replace。
- FoamMap 坐标主因已被 mask 统计排除。
- 本地黑点 surface PS 输出本身近黑，WaterFade/depth 和 refraction 输入是下一步重点。
- CK3 surface wrapper 后处理还没有完整移植，本地不能再声称 surface pass 完全等价。
