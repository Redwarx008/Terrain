# Stride Engine Shader Color Conversion Audit
**Date**: 2026-06-19
**Session**: River rendering color-space follow-up
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Check whether Stride engine built-in shaders contain manual sRGB/gamma/linear conversions relevant to river or terrain rendering.

---

## What We Did

### 1. Read-Only Subagent Investigation
**Files Changed:** none

Spawned a read-only explorer subagent to inspect `E:\WorkSpace\stride` for shader-side color conversions.

Findings:
- `ColorUtility.sdsl` defines manual conversion helpers:
  - `E:\WorkSpace\stride\sources\engine\Stride.Graphics\Shaders\ColorUtility.sdsl:6` `ToLinear`
  - `E:\WorkSpace\stride\sources\engine\Stride.Graphics\Shaders\ColorUtility.sdsl:40` `SRgbToLinear`
  - `E:\WorkSpace\stride\sources\engine\Stride.Graphics\Shaders\ColorUtility.sdsl:47` `LinearToSRgb`
  - `E:\WorkSpace\stride\sources\engine\Stride.Graphics\Shaders\ColorUtility.sdsl:60` `LinearToSRgbPrecise`
- These helpers are used mainly by sprite/UI vertex-color paths, not by normal 3D material shading.
- `SpriteBatchShader.sdsl:18` and `UIEffectShader.sdsl:18` call `ColorUtility.ToLinear(streams.Color)` when the sprite/UI permutation says the color input is sRGB.
- `SpriteRenderFeature.cs:263` sets that sprite alpha-cutoff path based on `device.ColorSpace == ColorSpace.Gamma`, so this is a sprite/UI color-input concern, not a forward material concern.
- `SpriteEffectExtTexture.sdsl:83` performs `pow(color, Gamma)` for external/video texture copy paths.
- `ToneMapHejlDawsonOperatorShader.sdsl:18` uses `pow(color.rgb, 2.2)` to undo the original Hejl-Dawson formula's gamma behavior.
- `ToneMapHejl2OperatorShader.sdsl:7` explicitly states it does not include gamma correction; this project uses `ToneMapHejl2Operator`.
- `ImageEffectShader.sdsl` inherits `SpriteBase`, and `SpriteBase.sdsl:35` simply samples `Texture0`; there is no universal postprocess gamma shader.
- Material/albedo shader search found no `ColorUtility`, `SRgbToLinear`, or `LinearToSRgb` use in `Stride.Rendering\Rendering\Materials`.

---

## Decisions Made

### Decision 1: Do Not Attribute River/Terrain Color to Hidden Stride Shader Gamma
**Context:** River `WaterColorTexture` appears very dark when inspected in shader space.

**Decision:** Stride's built-in shader-side gamma conversions are not part of the river/terrain forward path.

**Rationale:**
- Terrain and river textures rely on resource format/SRV sRGB decode.
- Final display relies on sRGB RTV/backbuffer encode.
- Sprite/UI/video/old Hejl-Dawson special cases do not apply to `RiverSurface.sdsl` or terrain material shaders.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `ColorUtility` exists, but its relevant engine uses are Sprite/UI/video special cases.
- `ToneMapHejl2Operator` has no gamma correction.
- `ToneMapHejlDawsonOperator` is the exception with `pow(color.rgb, 2.2)`, but it is not the current project operator.
- River/terrain forward shading should not add manual `LinearToSRgb` or `SRgbToLinear` unless a specific input is proven to bypass SRV sRGB decode.

**Gotchas:**
- Do not confuse Sprite/UI `ColorIsSRgb` with material texture sampling.
- Do not treat `ColorUtility.LinearToSRgb` existence as evidence that postprocess uses it.
