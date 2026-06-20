# Stride Linear/sRGB Output Chain Investigation
**Date**: 2026-06-19
**Session**: River rendering color-space follow-up
**Status**: ⚠️ Partially superseded on 2026-06-20
**Priority**: High

---

## Superseded Note

The engine-level linear/sRGB output-chain findings remain valid. The `WaterColorTexture` decision in this log has been superseded: current river loading uses `water_color.dds` as UNorm/linear (`loadAsSrgb:false`) and does not manually decode in `RiverSurface`.

## Session Goal

**Primary Objective:**
- Confirm whether Stride performs linear-to-sRGB conversion for final output, and whether that conversion is shader-side or render-target/view-side.

**Secondary Objectives:**
- Explain why normal terrain sRGB textures can look correct while `WaterColorTexture` sampled values look very dark in RenderDoc.
- Verify runtime and editor paths separately.

---

## What We Did

### 1. Delegated Stride Source Investigation
**Files Changed:** none

Spawned a read-only subagent to inspect `E:\WorkSpace\stride` and this project. The subagent confirmed:
- Stride uses `ColorSpace.Linear` for this project.
- In linear color space, Stride normalizes the presenter backbuffer to an sRGB `ViewFormat`.
- Final linear-to-sRGB conversion is done by the GPU fixed-function path when writing to an sRGB RTV, not by `ToneMapHejl2OperatorShader`.

Key evidence:
- `E:\WorkSpace\stride\sources\engine\Stride.Games\GraphicsDeviceManager.cs:654` maps linear backbuffer formats through `ToSRgb()`.
- `E:\WorkSpace\stride\sources\engine\Stride.Graphics\GraphicsPresenter.cs:306` normalizes presenter backbuffer format to sRGB when `GraphicsDevice.ColorSpace == ColorSpace.Linear`.
- `E:\WorkSpace\stride\sources\engine\Stride.Graphics\Direct3D\SwapChainGraphicsPresenter.Direct3D.cs:147` initializes the swapchain backbuffer with `Description.BackBufferFormat.IsSRgb`.
- `E:\WorkSpace\stride\sources\engine\Stride.Graphics\Direct3D11\Texture.Direct3D11.cs:165` upgrades the Stride texture description to sRGB when `treatAsSrgb` is set.
- `E:\WorkSpace\stride\sources\engine\Stride.Graphics\Direct3D11\Texture.Direct3D11.cs:610` creates RTVs using `ViewFormat`.
- `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Images\ColorTransforms\ToneMap\ToneMapHejl2OperatorShader.sdsl:7` explicitly says the operator does not include gamma correction.

### 2. Verified Project Runtime and Editor Paths
**Files Changed:** none

Runtime:
- `Terrain\Assets\GameSettings.sdgamesettings:17` sets `ColorSpace: Linear`.
- `E:\WorkSpace\stride\sources\engine\Stride.Engine\Engine\Game.cs:284` loads default settings when `AutoLoadDefaultSettings` is true.
- `Terrain.Windows\TerrainApp.cs` does not override color space.

Editor:
- `Terrain.Editor\Rendering\NativeViewport\EmbeddedStrideViewportGame.cs:67` disables `AutoLoadDefaultSettings`, but Stride `GraphicsDeviceManager` defaults to `PreferredColorSpace = Linear`.
- `Terrain.Editor\Rendering\NativeViewport\PresenterViewportSceneRenderer.cs:26` forwards `Presenter.BackBuffer.ViewFormat` into `RenderOutput`.
- `Terrain.Editor\Rendering\NativeViewport\PresenterViewportSceneRenderer.cs:44` binds the presenter backbuffer as the final render target.

### 3. Separated Texture Decode from Output Encode

Confirmed the two fixed-function conversions are separate:
- sRGB texture sampling: `_SRgb` SRV/texture formats decode RGB to linear for shader reads.
- sRGB output writing: sRGB RTV/backbuffer encodes shader linear output to sRGB for display.

This means a sampled sRGB color appearing dark in shader/RenderDoc is expected. For example, a visible sRGB blue-green value around `0.25` becomes roughly `0.05` in linear shader space.

---

## Decisions Made

### Decision 1: Do Not Treat Dark `WaterColorTexture` Shader Values as Missing Gamma
**Context:** `WaterColorTexture` samples are very dark in the surface shader.

**Decision:** The dark sampled value is expected if the texture is imported as sRGB and sampled in a linear workflow.

**Rationale:** CK3 also uses sRGB `WaterColorTexture` sampling, and Stride correctly decodes sRGB textures to linear. Final display encode exists via sRGB backbuffer.

**Trade-offs:** The remaining river darkness must be diagnosed in river composition, local water-color use, CK3 final grading mismatch, or refraction/source timing, not as a missing engine-wide gamma pass.

---

## What Worked ✅

1. **Subagent source audit**
   - It quickly confirmed the engine-level color path and produced concrete file references.

2. **Checking render target view creation instead of only shaders**
   - The important conversion is not visible in SDSL; it is determined by `ViewFormat` and RTV creation.

---

## Problems Encountered & Solutions

### Problem 1: Confusing shader-visible linear values with display-space values
**Symptom:** `WaterColorTexture` values in shader were near black, despite the image looking blue-green.

**Root Cause:** sRGB texture sampling returns linear values to shader code.

**Solution:** Verify both ends of the pipeline:
- Texture import/SRV: `UseSRgbSampling: true` -> sRGB format -> sample decode to linear.
- Backbuffer/RTV: `ColorSpace.Linear` -> sRGB view -> write encode to display.

---

## Next Session

### Immediate Next Steps
1. Continue river-specific diagnosis without changing global postprocess.
2. If brightness needs compensation, validate it by RenderDoc hot edit inside `RiverSurface` only, limited to water-color contribution, not whole-screen tonemapping.
3. Current follow-up supersedes the old advice: keep `WaterColorTexture` as UNorm/linear unless a fresh RenderDoc comparison proves the target SRV should be sRGB.

### Questions to Resolve
1. Whether current river darkness is caused by CK3 final mapface grading being absent, surface local composition, or refraction source/timing.
2. Whether local river-only compensation is acceptable if the project intentionally does not replicate CK3's full-screen final pass.

---

## Session Statistics

**Files Changed:** 1 documentation file
**Code Files Changed:** 0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Stride does perform final linear-to-sRGB conversion through sRGB backbuffer/RTV fixed-function output.
- `ToneMapHejl2OperatorShader` does not include gamma correction.
- Editor and runtime both use linear color space in practice.
- Dark sRGB texture samples in shader space are expected and should not be corrected globally.

**Gotchas for Next Session:**
- Do not add manual `pow(color, 1/2.2)` to Stride tonemap or river shader as a global fix.
- Do not switch `WaterColorTexture` to linear import just because RenderDoc shader values look dark.
- Keep separating CK3 surface pass values from CK3 final composite event `1146`.
