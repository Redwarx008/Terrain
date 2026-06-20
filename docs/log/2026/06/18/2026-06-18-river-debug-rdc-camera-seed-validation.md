# River Debug RDC Camera Seed Validation
**Date**: 2026-06-18
**Session**: River RenderDoc follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Recheck `C:\Users\Redwa\Desktop\debug.rdc` after the `RiverSceneSeed` camera-distance revision.

**Success Criteria:**
- Confirm the seed shader in the capture uses `ProjectionInverse/ViewInverse/Eye`.
- Confirm seed alpha is no longer near-clip or raw zero payload.
- Confirm the seed is copied into `BottomColor` and read by the surface pass.
- Recheck bottom scene-driven shadow/cubemap usage in the same capture.

---

## Context & Background

**Previous Work:**
- The 03:00 capture confirmed `GraphicsDevice.Presenter.DepthStencilBuffer` fixed the constant near-clip alpha, but it was captured before the final camera-distance seed revision.
- Code was changed so `RiverSceneSeed` reconstructs world position from depth and writes `length(positionWS.xyz - Eye.xyz)`.

**Current State:**
- RenderDoc MCP tools were still not exposed through tool discovery, so this session used `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-cli.exe`.

---

## What We Did

### 1. Validated Seed Pass
**Capture:** `C:\Users\Redwa\Desktop\debug.rdc`

**Findings:**
- Capture timestamp: `2026-06-18 03:16:34`.
- API: D3D11.
- EID 248 is the half-resolution `RiverSceneSeed` pass.
- RT resource: `7829`, `836x498`, `R16G16B16A16_FLOAT`.
- Pixel shader declares `cb0[24] (PerView)`, samples `DepthStencil_id79`, multiplies by `ProjectionInverse_id83` and `ViewInverse_id81`, subtracts `Eye_id86`, then writes `sqrt` to `o0.w`.
- `tex-stats 7829 -e 248` reported alpha `4.82031..8.66406`.
- `pick-pixel 471 282 -e 248 --target 0` returned alpha `5.23047`.

**Conclusion:**
- The capture contains the new camera-distance seed path. This is no longer the old near-clip `0.1` failure and no longer the intermediate raw view-depth implementation.

### 2. Validated Copy and Surface Consumption
**Resources:**
- `7829`: `SceneSeedColor`.
- `7832`: working `BottomColor` / refraction buffer.

**Findings:**
- `usage 7829`: EID 248 `ColorTarget`, EID 249 `CopySrc`.
- `usage 7832`: EID 249 `CopyDst`, EID 276/290 `ColorTarget`, EID 319/337 `PS_Resource`.
- `tex-stats 7832 -e 249` matched `7829` at EID 248, confirming the copy preserved seed RGB/alpha.
- After bottom draw EID 290, `7832` alpha became `4.72656..9.30469`, showing bottom wrote its own camera-distance payload over covered river pixels.

**Conclusion:**
- The seed payload is correctly propagated into the working refraction buffer and is read by both surface draws.

### 3. Rechecked Bottom Scene-Driven Inputs

**Findings:**
- Bottom shader at EID 290 declares:
  - `EnvironmentMapTexture_id49` as texture cube.
  - `SceneShadowMapTexture_id50` as texture2D.
  - `SceneShadowSampler_id53` as comparison sampler.
- Disassembly includes 5x5 `SampleCmpLevelZero` shadow filtering.
- Disassembly uses `_EnvironmentIntensity_id40`, `_EnvironmentMipCount_id41`, and `_EnvironmentSkyMatrix_id39` before sampling the cubemap.
- Resource `7758` is the 4096x4096 `R32_TYPELESS` shared shadow atlas:
  - EID 37/57/77/97: `DepthStencilTarget`.
  - EID 276/290: `PS_Resource`.
  - Stats at EID 97: depth range `0.136622..1`.
- Resource `276` is the 256x256 `R16G16B16A16_FLOAT` environment texture array used by bottom:
  - EID 204/276/290: `PS_Resource`.
  - Stats at EID 290 slice 0: RGB max `3.23438 / 3.65234 / 3.29688`.

**Conclusion:**
- The bottom pass is not using the old river-only fallback constants in this capture. Real shadow atlas and scene environment cubemap data are bound and sampled.

---

## What Worked ✅

1. **Camera-distance seed verification**
   - The compiled shader and RT alpha stats both confirm the intended path.

2. **Resource-flow verification**
   - `usage` established the full `SceneSeedColor -> BottomColor -> RiverSurface` chain.

3. **Scene-driven bottom inputs**
   - Shadow and cubemap are both present as real GPU resources, not only shader symbols.

---

## What Didn't Work ❌

1. **RenderDoc MCP tools still unavailable**
   - `tool_search` returned no RenderDoc tools.
   - Used `renderdoc-cli.exe` fallback.

---

## Architecture Impact

**Documentation Updated:**
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Changed:**
- The previous "still needs new capture validation" note for camera-distance seed is now resolved for the 03:16 capture.

**Remaining Technical Debt:**
- This validates binding and payload semantics, not CK3 visual parity by itself.
- Remaining visual gap should now be investigated as bottom/surface material, attenuation, map data, or lighting energy behavior rather than seed depth binding.

---

## Verification

**Commands Run:**
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc info`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc pass-stats`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc pipeline -e 248`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc shader ps -e 248`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc tex-stats 7829 -e 248 --histogram`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc usage 7829`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc usage 7832`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc tex-stats 7832 -e 249 --histogram`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc tex-stats 7832 -e 290 --histogram`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc shader ps -e 290`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc usage 7758`
- `renderdoc-cli.exe C:\Users\Redwa\Desktop\debug.rdc usage 276`

---

## Next Session

### Immediate Next Steps
1. Compare the current bottom/surface output against CK3 again, now treating seed depth binding as ruled out.
2. If the river is still too dark or wrong near banks, inspect `RiverSurface` attenuation and refraction decode at specific pixels with pixel history/debug.
3. If CK3 comparison is needed, inspect the matching CK3 pass resource ranges for bottom color, alpha payload, and surface output at equivalent screen-space samples.

### Blocked Items
- Direct MCP workflow remains blocked until RenderDoc MCP tools are actually exposed in the tool list.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `debug.rdc` from `2026-06-18 03:16:34` validates the new `RiverSceneSeed` camera-distance path.
- EID 248 writes resource `7829`; EID 249 copies it into `7832`; EID 319/337 read `7832`.
- Bottom shader reads shadow atlas `7758` and environment texture array `276`.

**Gotchas for Next Session:**
- Do not reopen the command-list-depth hypothesis for this capture; it is ruled out.
- Do not claim CK3 parity from this validation alone.
