# 河流 debug.rdc 仍偏黑热修改复核
**Date**: 2026-06-19
**Session**: RenderDoc `debug.rdc` still-black follow-up
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 复核用户新截帧 `C:\Users\Redwa\Desktop\debug.rdc` 中河流仍偏黑的问题，优先用 RenderDoc 热替换确认原因。

**Success Criteria:**
- 判断黑色来自 bottom、surface composition、还是后续 present/post。
- 对照 CK3 `ck3-river.rdc` 与 CK3 loose shader 找出仍缺失的 pass 语义。

---

## Context & Background

**Previous Work:**
- `docs/log/2026/06/19/2026-06-19-river-surface-lighting-cbuffer-parity.md`
- `docs/log/2026/06/18/2026-06-18-river-target-shader-semantics.md`

**Current State:**
- 当前帧：`C:\Users\Redwa\Desktop\debug.rdc`
- 目标帧：`C:\Users\Redwa\Desktop\ck3-river.rdc`

---

## What We Did

### 1. 本地 bottom 逐像素复核
**Files Changed:** none

**Findings:**
- 新 `debug.rdc` 为 D3D11，65 draws，bottom EID `274`，surface EID `302`。
- bottom 暗点的 `shaderOut` 本身很低，不是 dual-source blend 或后处理导致：
  - `(538,306)` bottom 输出约 `[0.0189, 0.0213, 0.0217]`
  - `(414,298)` 邻近亮点输出约 `[0.1988, 0.1536, 0.0909]`
- 热替换 bottom 为 raw diffuse 采样后，同一批点底图本来就是低能量棕色：
  - `PositionWS.xz * 0.5` 采样约 `0.04..0.06` red
  - `PositionWS.xz * 1.0` 更暗，因此 `_WorldToMapUnitScale = 0.5` 不是当前黑源。

### 2. CK3 bottom / surface 对照
**Files Changed:** none

**Findings:**
- CK3 `game/gfx/map/rivers/rivers.settings` 实际绑定 `gfx/map/rivers/river_bottom_diffuse.dds`、`river_bottom_normal.dds`、`river_bottom_gloss.dds`。
- 本地 `Terrain.Editor/Assets/River/Bottom` 三张 bottom 贴图 SHA256 与 CK3 `gfx/map/rivers` 版本一致，没有拿错 `gfx/map/textures` 版本。
- CK3 bottom EID `338` 输出 RT `ResourceId::49006` min 也很低，约 `[0.0069, 0.0073, 0.0039]`；bottom 存在黑点本身不是不等价。
- CK3 surface EID `460` disasm 明确在 `CalcWater` 后继续执行 cloud shadow、terrain shadow tint、fog-of-war、distance/relative fog，最后才写 `o0`。

### 3. 本地 surface 热替换验证
**Files Changed:** none

**Findings:**
- 本地 surface shader 已包含 `_WaterZoomedInZoomedOutFactor`，且旧 `sunIntensityMask` 搜索不到，说明上一轮 shader 进入 GPU。
- 本地 surface shader 搜索不到 `FogOfWar` / `ApplyTerrainShadowTint`，bindings 也只有水面资源；CK3 surface 绑定额外 `HeightLookupTexture`、`PackedHeightTexture`、`ShadowNoiseTexture`、`FogOfWarAlpha`、`ShadowMap` 等。
- 将本地 surface EID `302` 热替换为 direct refraction 后，代表暗点明显变亮：
  - `(1023,578)` 原 surface 约 `[0.0253,0.0341,0.0315]`；direct refraction 约 `[0.0827,0.0668,0.0483]`
  - `(828,596)` 原 surface 约 `[0.0765,0.0686,0.0448]`；direct refraction 约 `[0.2070,0.1570,0.0954]`
- 结论：当前黑不是 present/post；surface composition 仍会把 bottom/refraction 压暗，并且缺失 CK3 surface 后段。

---

## Decisions Made

### Decision 1: 不再把 bottom 低 min 当作单独根因
**Context:** 本地 bottom 个别像素黑，CK3 bottom RT 也有类似低 min。
**Decision:** 后续优先补 surface 后段语义和资源绑定，而不是继续用全局增益或任意调亮 bottom。
**Rationale:** 这符合“完全参考 CK3”的要求，避免用非目标补偿掩盖问题。
**Trade-offs:** 需要 C# RenderFeature 绑定更多资源与参数，改动比单个 SDSL 函数大。

---

## What Worked

1. **用 hot replacement 验证 surface 压暗**
   - direct refraction 替换能立刻提升暗点亮度，证明问题在 surface composition/缺失后段。

2. **先校验资源 hash**
   - 排除了拿错 CK3 bottom 贴图来源的问题。

---

## What Didn't Work

1. **只检查 bottom 黑点**
   - CK3 bottom 也有很低 min，单看 bottom 会误导。
   - 后续应同时看 bottom 输入、surface 输出和 CK3 surface 后段。

---

## Next Session

### Immediate Next Steps
1. 补 surface pass 的 CK3 后段资源和参数边界：FogOfWarAlpha、terrain height/normal lookup、shadow noise、cloud shadow、terrain shadow tint、distance/relative fog。
2. 在 RenderDoc 热替换中先做可控的 surface 后段近似，确认视觉方向，再落地 SDSL/C#。
3. 若继续报告流向错误，再查 mesh centerline/RiverUV 方向；不要优先改 flow formula，当前 CK3 advanced flow UV 公式已经对齐。

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/2026/06/19/2026-06-19-river-surface-lighting-cbuffer-parity.md`

---

## Session Statistics

**Files Changed:** 1 log only
**Code Changes:** 0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `debug.rdc` bottom EID `274` 的暗点 shaderOut 低，但 CK3 bottom RT 也有低 min，不能直接判定 bottom 错。
- 当前最明确缺口是 surface EID `302` 缺 CK3 `CalcWater` 后的 cloud / terrain shadow tint / fog-of-war / fog 后段，以及相应资源绑定。
- surface direct-refraction 热替换能让代表暗点变亮，证明 present/post 不是黑源。
