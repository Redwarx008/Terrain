# River Scene Seed Depth Payload
**Date**: 2026-06-18
**Session**: River pre-bottom seed follow-up
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Continue the CK3 river comparison work by replacing the previous river refraction scene seed path that used `ImageScaler` and cleared alpha to zero.

**Success Criteria:**
- `SceneSeedColor` is produced by a dedicated shader that can write a depth-derived payload.
- The new shader is registered through the Stride shader asset workflow.
- Existing river tests and asset compilation pass.

---

## Context & Background

**Previous Work:**
- `debug.rdc` and `ck3-river.rdc` comparison showed bottom shadow/cubemap binding was no longer the dominant mismatch.
- The larger remaining structural mismatch was the pre-bottom seed:
  - CK3 uses half-resolution terrain seed draws with RGB around `0..1.5` and alpha already carrying distance payload.
  - Current local build copied raw HDR main scene color through `ImageScaler` and explicitly cleared alpha to `0`.

**Current State:**
- RenderDoc MCP was checked again through tool discovery, but this session still did not expose RenderDoc MCP tools. Blender/GitHub tools were returned instead.
- Verification used code tests and Stride asset compilation. A new runtime capture is still required.

---

## What We Did

### 1. Added Dedicated Scene Seed Shader
**Files Changed:** `Terrain.Editor/Effects/RiverSceneSeed.sdsl`, `Terrain.Editor/Effects/RiverSceneSeed.sdsl.cs`

**Implementation:**
- Added `RiverSceneSeed : ImageEffectShader, DepthBase, Transformation`.
- Samples scene color, applies a simple Reinhard HDR compression, reconstructs world position from scene depth, and writes camera-relative distance into alpha.
- Generated shader keys for `_SceneSeedExposure` and `_SceneSeedColorScale`.

**Rationale:**
- CK3's bottom buffer seed is not just a color downsample. It also carries a non-zero distance payload before river bottom draws.
- This removes the known incorrect behavior where local seed alpha was forcibly cleared to zero.

### 2. Routed RiverRenderFeature Through RiverSceneSeed
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- Replaced the old `ImageScaler` seed path with `ImageEffectShader("RiverSceneSeed", delaySetRenderTargets: true)`.
- Resolved `GraphicsDevice.Presenter.DepthStencilBuffer` as the windowed editor/runtime scene depth shader resource.
- Bound `DepthBaseKeys.DepthStencil`, `CameraKeys.ZProjection`, near/far clip values, and linear clamp sampling.
- Kept the existing `SceneSeedColor -> BottomColor` `CopyRegion` handoff.
- Scene seed preconditions are direct `Debug.Assert(...)` checks; no selector, command-list depth fallback, or assertion helper is used.

**Rationale:**
- This keeps the existing two-buffer architecture while making the seed closer to CK3's scene-driven payload semantics.

### 3. Strengthened Tests
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`, `Terrain.Editor.Tests/RiverShaderCompileTests.cs`

**Implementation:**
- Updated the scene-seed separation text test to expect `seedEffect.SetOutput(seedTarget)`.
- Added text assertions that `RiverSceneSeed` uses `DepthBase`, resolves scene depth, and no longer uses the old `ImageScaler` alpha-clearing path.
- Added an actual Stride effect compiler test for `RiverSceneSeed`.
- Added the missing Stride `Rendering/Images` source directory to the test compiler include path so `ImageEffectShader.sdsl` resolves.

---

## What Worked ✅

1. **Stride shader asset workflow**
   - `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` processed `Effects/RiverSceneSeed`.
   - `StrideCleanAsset` and `StrideCompileAsset` completed successfully.

2. **Effect compiler regression test**
   - The new `RiverSceneSeed` compile test initially failed because the test harness did not include `Stride.Rendering/Rendering/Images`.
   - Adding that include path made the test match the real asset compiler environment.

---

## What Didn't Work ❌

1. **RenderDoc MCP availability**
   - The user indicated RenderDoc MCP should be available, but current tool discovery still did not expose it.
   - This session could not perform a new MCP-based capture inspection.

---

## Verification

**Commands Run:**
- `dotnet run --project 'E:\Stride Projects\Terrain\Terrain.Editor.Tests\Terrain.Editor.Tests.csproj' -c Debug`
- `dotnet msbuild 'E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj' "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild 'E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj' /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild 'E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj' /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet build 'E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj' -c Debug`

**Results:**
- Tests passed, including `river scene seed shader compiles through stride effect compiler`.
- Stride asset compile succeeded: 909 succeeded, 0 failed.
- Final editor build succeeded with existing warnings only.

---

## Architecture Impact

**Documentation Updated:**
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Changed:**
- River pre-bottom seed is no longer documented as simply non-equivalent due to zero alpha.
- It is now documented as `RiverSceneSeed` with HDR compression and scene-depth payload, pending RenderDoc validation.

**Remaining Technical Debt:**
- `RiverSceneSeed` alpha now uses camera-relative distance to match `RiverCompressWorldSpace` / surface decode semantics. A fresh capture after this shader revision is still required.
- RGB compression is a pragmatic approximation based on observed CK3/current RT ranges, not yet capture-validated.

---

## Next Session

### Immediate Next Steps
1. Capture a new `debug.rdc` after this change and compare:
   - seed texture after `RiverSceneSeed`
   - bottom RT after river bottom draws
   - surface sampling behavior near banks
2. Capture again after the camera-distance seed revision and confirm the alpha distribution and bank behavior.
3. If RenderDoc MCP becomes available, use it directly; otherwise keep using `renderdoc-cli.exe` fallback.

### Blocked Items
- New visual/capture verification is blocked on producing a fresh frame capture after the code change.

---

## Follow-up RenderDoc Recheck

- New `C:\Users\Redwa\Desktop\debug.rdc` showed `RiverSceneSeed` was active at EID 248, writing `SceneSeedColor` resource `7862`.
- Seed RGB range improved to CK3-like half-resolution terrain seed values: `7862` at EID 248 max was about `1.316 / 1.336 / 1.085`, compared to source scene RT `4055` max about `8.336 / 9.508 / 2.752`.
- Seed alpha was still wrong in that capture: `7862` alpha was constant `0.0999756`, and pixel debug at `(471,282)` returned output alpha `0.1`.
- Shader disassembly confirmed `RiverSceneSeed` sampled `DepthStencil` and applied `ZProjection.y / (depth - ZProjection.x)`, so the constant alpha means the selected depth source was not valid scene depth.
- Stride source review confirmed `Presenter.DepthStencilBuffer` is the default presenter/backbuffer depth while `CommandList.DepthStencilBuffer` is the currently bound output-merger depth. Runtime window presenters normally expose presenter depth, but render-target/offscreen presenters may not.
- `RiverRenderFeature` now directly uses `GraphicsDevice.Presenter.DepthStencilBuffer` for the current windowed editor/runtime scene seed path. Presenter/depth/size assumptions are expressed as direct `Debug.Assert(...)` checks, and there is no `SelectSceneDepthSource`, command-list depth fallback, custom assertion helper, or explicit assertion-to-exception conversion.
- New `C:\Users\Redwa\Desktop\debug.rdc` captured at 2026-06-18 03:00 confirmed the Presenter depth binding fixed the constant-alpha failure: EID 248 `RiverSceneSeed` wrote resource `7873`, alpha range was `12.3984..24.4375`, and pixel `(471,282)` output alpha was about `16.997`.
- That capture was taken before the final camera-distance seed revision, so it validates depth source binding but not the new `ProjectionInverse/ViewInverse/Eye` reconstruction path.
- Code review identified that raw `ComputeDepthFromUV` view-space depth was not semantically identical to `RiverCompressWorldSpace`; `RiverSceneSeed` now reconstructs world position from raw depth and writes `length(positionWS.xyz - Eye.xyz)` instead.

---

## Quick Reference for Future Claude

**What Changed Since Last Doc Read:**
- `Terrain.Editor/Effects/RiverSceneSeed.sdsl` is new and registered.
- `RiverRenderFeature` now seeds `SceneSeedColor` through `RiverSceneSeed`, not `ImageScaler`.
- `RiverSceneSeed` now writes camera-relative distance alpha, not raw view-space depth.
- `RiverShaderCompileTests` now includes `Rendering/Images` and compiles `RiverSceneSeed`.

**Gotchas for Next Session:**
- Do not claim CK3 visual parity from this session alone. The code compiles and tests pass, but the new seed needs a fresh RenderDoc capture.
- The workspace still has unrelated dirty changes and artifacts. Only stage river seed/test/doc files if committing this work.
