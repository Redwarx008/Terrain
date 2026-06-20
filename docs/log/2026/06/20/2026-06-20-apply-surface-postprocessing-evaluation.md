# River Surface ApplySurfacePostProcessing Evaluation
**Date**: 2026-06-20
**Session**: ApplySurfacePostProcessing usefulness check
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Evaluate whether `RiverSurface.ApplySurfacePostProcessing` is useful enough to keep, using `C:\Users\Redwa\Desktop\debug4.rdc`.

**Success Criteria:**
- Confirm whether the wrapper is active on the captured river surface draw.
- Hot-replace the surface shader to bypass the wrapper and compare output.
- Decide whether deletion is safe.

---

## Context & Background

**Previous Work:**
- `2026-06-20-river-surface-debug4-wrapper-fully-restored.md` restored the full debug4-style wrapper.
- `docs/log/learnings/stride-river-rendering-patterns.md` records that previous direct-output experiments could make river surface darker in other captures.

**Current State:**
- `RiverSurface.sdsl` calls `ApplySurfacePostProcessing` after `CalcRiverAdvanced`.
- `RiverRenderFeature` binds `_InverseWorldSize`, `_HasCloudShadowEnabled`, shadow noise, and editor terrain inputs for this wrapper.

---

## What We Did

### 1. Inspected `debug4.rdc`
**Files Changed:** none

**Findings:**
- Capture opened successfully: D3D11, 62 draws, 72 events.
- River surface draws are event `149` and `176`, both using PS `ResourceId::7793`.
- The active PS disassembly contains:
  - `GetCloudShadowMask`
  - `_HasCloudShadowEnabled`
  - `_InverseWorldSize`
  - terrain shadow tint via `ShadowNoiseTexture`
  - map distance fog
  - `ApplySurfacePostProcessing`

### 2. Hot-Replaced Surface PS To Bypass Wrapper
**Files Changed:** diagnostic artifact only
- `artifacts/renderdoc-mcp/debug4_apply_post_eval_e149/shader_RiverSurface_no_apply_post.hlsl`
- `artifacts/renderdoc-mcp/debug4_apply_post_eval_e149/rt_149_original.png`
- `artifacts/renderdoc-mcp/debug4_apply_post_eval_e149/rt_149_no_apply_post.png`
- `artifacts/renderdoc-mcp/debug4_apply_post_eval_e149/rt_176_original.png`
- `artifacts/renderdoc-mcp/debug4_apply_post_eval_e149/rt_176_no_apply_post.png`

**Implementation:**
- Copied the matching Stride-generated HLSL from `Bin/Editor/Debug/win-x64/log`.
- Replaced only the `PSMain` post call:

```hlsl
// Diagnostic replacement: bypass the surface post wrapper and output CalcRiverAdvanced directly.
```

- Compiled it through RenderDoc MCP `shader_build`.
- Applied it through `shader_replace` to PS event `149` / `176`.

**Verification:**
- `debug_pixel` on `(836,498)` confirmed replacement was active: total PS steps dropped from `968` to `279`.

### 3. Compared Original vs Bypassed RTs

**Measured Results:**
- Event `149`: only `16` RGB pixels changed; max absolute RGBA delta was `[1, 0, 1, 0]` in exported PNG units.
- Event `176`: same result, only `16` RGB pixels changed; max absolute RGBA delta was `[1, 0, 1, 0]`.
- Representative surface pixel `(836,498)` remained effectively identical: shader output stayed about `[0.1529, 0.2546, 0.2410, 1.0]`.

---

## Decisions Made

### Decision 1: Do Not Delete The Wrapper Blindly
**Context:** In `debug4.rdc`, bypassing the wrapper is visually equivalent for the captured frame.

**Decision:** Do not treat that as proof that `ApplySurfacePostProcessing` is removable.

**Rationale:**
- The wrapper is active code and has real bindings.
- It still owns semantic behavior for cloud shadow, terrain shadow tint, map distance fog, flat-map alpha, zoom fade, and discard.
- In this exact capture, those terms are effectively zero or visually negligible.

**Trade-offs:**
- Keeping it preserves target CK3-style wrapper semantics but keeps time-phase sensitivity through `_GlobalTime`.
- Removing it would simplify shader/C# resource binding only if we intentionally drop those semantics.

---

## Architecture Impact

No architecture or feature-status document update required. This was an evaluation only; no runtime source changed.

---

## Next Session

### Immediate Next Steps
1. If the goal is deterministic debugging, prefer adding a runtime/debug toggle for `_HasCloudShadowEnabled` or cloud tint instead of deleting the full wrapper.
2. If the goal is permanent simplification, first decide explicitly to drop CK3-style terrain/cloud/fog wrapper semantics from river surface.

### Docs to Read Before Next Session
- `docs/log/2026/06/20/2026-06-20-river-surface-debug4-wrapper-fully-restored.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- On `debug4.rdc`, `ApplySurfacePostProcessing` is active but nearly inert for event `149` and `176`.
- Hot-replacing PS to bypass the wrapper changed only 16 exported PNG pixels by 1 LSB.
- This proves the wrapper is not visually important for this exact capture, not that it is dead code.

**Gotchas:**
- RenderDoc `export_render_target` overwrites the same `rt_<event>_0.png` path; copy it immediately if comparing variants.
- Stride shader logs contain multiple historical `RiverSurface` HLSL versions; match both resource/constant IDs and wrapper body before using a file for replacement.

