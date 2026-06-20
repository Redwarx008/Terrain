# River Surface Refraction Depth Hotmodify Analysis
**Date**: 2026-06-19
**Session**: 12
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续按“先 RenderDoc 热修改，再改 SDSL”的顺序，复核 current 与 CK3 river bank 差异是否来自 width、bottom depth/profile，还是 surface refraction payload。

**Success Criteria:**
- 用 MCP 热替换确认 current bank 的实际 `worldWidth`。
- 对 CK3/current bottom 和 surface pass 做同像素证据对比。
- 在不改源码的前提下确认下一步应查的真实方向。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-current-vs-ck3-bank-input-width-mismatch.md](./2026-06-19-river-current-vs-ck3-bank-input-width-mismatch.md)
- See: [2026-06-19-river-hotmodify-alpha-threshold-scan.md](./2026-06-19-river-hotmodify-alpha-threshold-scan.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- current capture: `C:\Users\Redwa\Desktop\debug.rdc`
- CK3 capture: `C:\Users\Redwa\Desktop\ck3-river.rdc`
- current chain remains `248/249 -> 276 -> 305`.
- CK3 bottom `338` writes `ResourceId::49006`; CK3 surface `466` reads that same resource as `RefractionTexture`.

---

## What We Did

### 1. 用 hot replacement 复核 current bank width
**Files Changed:** none

**Implementation:**
- 在 current `event 276` 替换 bottom PS，输出：
  - `worldWidth = RiverWidth * max(MapExtent, 1) * 2`
  - `normalizedWidth * 10000`
  - `MapExtent / 4096`
- 第一次 replacement 的 HLSL 输入签名被编译器重新打包，错误地把 `RiverWidth` 读成了 `riverUv.y`。
- 修正方法是让 replacement 的 `POSITION_WS` 用 `float4` 占满 `reg0`，使 `DEPTH_VS/TEXCOORD1/TEXCOORD4` 回到原 shader 的 `reg2.x/yz/w`。

**Result:**
- current bank `(140,384)`:
  - `MapExtent = 9215.5`
  - `RiverWidth(normalized) = 8.2831459e-05`
  - `worldWidth = 1.5266666`
- CK3 bank previous value:
  - `Width = 1.46506`

**Rationale:**
- 上一轮日志里的 `current worldWidth≈0.339` 是错误推断。
- current 与 CK3 的 bank width 实际同量级，不应继续把 width mismatch 当成主因。

### 2. 复核 CK3 bottom 实际分支
**Files Changed:** none

**Implementation:**
- 读取 CK3 `event 338` PS disasm。

**Findings:**
- CK3 `338` bottom 实际走 non-advanced `CalcDepth(UV)`：
  - 没有 `_BankAmount`
  - 没有 `_DepthWidthPower`
  - 没有 `BottomNormal.b` sampled depth
- `Out.Blend` 逻辑是：
  - `saturate(Depth * 13) * FadeOut * FadeToConnection`
- 这与 current bottom 当前的 advanced-like `bottomDiffuse.a * edgeFade1 * edgeFade2 * connectionFade` 不同。

**Hot Test:**
- 在 current `276` 上把 `RT1 alpha` 改为 CK3 non-advanced blend 公式，同时固定 bank `RT0.rgb/a`。
- current `305` bank 从 baseline `[0.3335,0.2392,0.1545]` 变成约 `[1.127,0.996,0.807]`。

**Conclusion:**
- CK3-style lower `Out.Blend` 会让 current bottom RT 回到亮 seed，最终更亮。
- RT1 coverage 差异不是当前 bank 偏亮的修复方向。

### 3. 用 surface replacement 输出 current refraction depth
**Files Changed:** none

**Implementation:**
- 在 current `event 305` 替换 surface PS，输出：
  - `RefractionTexture.a`
  - `surfaceDistance = length(camera - waterSurfacePos)`
  - `refractionDepth = waterY - decompressedRefractionY`
  - `inputDepth = CalcRiverProfileDepth(UV) * worldWidth + 0.1`
- 先错误使用 `800x600` fallback view size，导致 x=30 的 refraction sample 采错；随后用 `ResourceId::4059` 确认 full RT 是 `1672x996`，重跑诊断。

**Results:**
- current x=280, y=768, `UV.y≈0.974`:
  - `RefractionTexture.a = 9.6777`
  - `surfaceDistance = 9.6727`
  - `refractionDepth = 0.0035`
  - `inputDepth = 0.1031`
  - baseline surface output `[0.3335,0.2392,0.1545]`
- current x=30, y=768, `UV.y≈0.083` (comparable to CK3 `UV.y≈0.093`):
  - `RefractionTexture.a = 10.8887`
  - `surfaceDistance = 10.5348`
  - `refractionDepth = 0.2296`
  - `inputDepth = 0.1294`
  - baseline surface output `[0.2924,0.2122,0.1432]`
- CK3 comparable surface point `event 466 (110,738)`, `UV.y≈0.093`:
  - pixel history shaderOut/post `[0.02231,0.02796,0.03049]`

**Conclusion:**
- current and CK3 differ strongly even at comparable `UV.y`.
- The remaining main gap is surface attenuation / see-through / refraction-depth interpretation, not `HeightLookupTexture`, `PackedHeightTexture`, or `FogOfWarAlpha`.

### 4. Tested profile-depth alpha floor
**Files Changed:** none

**Implementation:**
- Hot-replaced current bottom `276` to set `RT0.a = 9.82`, approximately enough for x=280 surface to decode `~0.1` refraction depth.

**Result:**
- current `305` bank changed only from `[0.3335,0.2392,0.1545]` to `[0.2848,0.2003,0.1265]`.

**Conclusion:**
- Merely raising alpha to match `inputDepth≈0.1` is insufficient.
- CK3-like dark bank requires either substantially deeper refraction payload or different see-through/water attenuation constants/formula.

---

## Decisions Made

### Decision 1: Stop pursuing width mismatch
**Context:** hot dump proved current `worldWidth=1.5267`, CK3 `Width=1.4651`.
**Decision:** width is no longer the leading suspect.
**Rationale:** the previous `0.339` estimate came from bad cbuffer inference.

### Decision 2: Do not port CK3 advanced bottom depth yet
**Context:** CK3 `event 338` disasm is non-advanced bottom.
**Decision:** do not write `_BankAmount/_DepthWidthPower/BottomNormal.b` advanced depth into current SDSL as a fix for this capture.
**Rationale:** it is not the branch used by the reference frame.

### Decision 3: Continue with surface attenuation / see-through cbuffer comparison
**Context:** current comparable bank `UV.y≈0.083` remains bright while CK3 `UV.y≈0.093` is dark.
**Decision:** next step should dump/compare CK3 and current surface water constants around see-through, shore masks, water fade, and water-color map.
**Rationale:** hot tests show bottom RT1 and small alpha floors do not reproduce CK3.

---

## What Worked ✅

1. **MCP hot replacement cbuffer/output probes**
   - What: replaced active shaders to output specific cbuffer/input/resource-derived values.
   - Why it worked: corrected the wrong `worldWidth≈0.339` hypothesis without touching source.
   - Reusable pattern: Yes.

2. **Pixel history over debug_pixel for overlapping surface fragments**
   - What: used pixel history to identify actual passing primitive and output.
   - Impact: avoided trusting CK3 `debug_pixel` output that did not match the pixel history fragment.

---

## What Didn't Work ❌

1. **Assuming HLSL replacement input packing matches original**
   - What we tried: declared `float3 PositionWS`, which allowed the compiler to pack `DEPTH_VS` into `reg0.w`.
   - Why it failed: original shader had `POSITION_WS` at `reg0` and `DEPTH_VS/TEXCOORD` at `reg2`.
   - Lesson learned: use replacement reflection to verify input registers before trusting output.

2. **Using fallback view size in surface replacement**
   - What we tried: used `800x600` fallback for screen UV.
   - Why it failed: actual current RT is `1672x996`, so refraction sampling was wrong.
   - Lesson learned: query render target size or `_ViewSize` before surface texture probes.

---

## Next Session

### Immediate Next Steps
1. Dump CK3 surface `466` water/see-through constants using replacement cbuffer probes, then compare to current `305`.
2. Hot-replace current surface attenuation terms only, one at a time, to identify why comparable `UV.y≈0.09` remains bright.
3. Only after a surface hot replacement matches CK3 should the validated delta be ported to `RiverSurface.sdsl`.

### Gotchas
- Do not reuse shader IDs across capture opens.
- Do not rely on `debug_pixel` alone when pixel history shows a different actual fragment.
- Do not compare absolute `RT0.a` values across captures without accounting for camera distance.

---

## Session Statistics

**Files Changed:** 0 runtime files, 2 documentation files
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- current bank `worldWidth` is `1.5267`, not `0.339`.
- CK3 bottom `338` is non-advanced `CalcDepth(UV)`.
- current comparable bank `UV.y≈0.083` outputs `[0.292,0.212,0.143]`; CK3 `UV.y≈0.093` outputs `[0.022,0.028,0.030]`.
- small bottom alpha/profile-depth fixes are insufficient; continue with surface attenuation constants/formula.

---
