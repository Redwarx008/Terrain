# Water Refraction Capture Task 3
**Date**: 2026-06-24
**Session**: Ocean CK3 core water Task 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Implement the renderer-owned shared water refraction capture helper and shader shell without migrating River/Ocean internals and without adding `DrawWater` / `DrawWaterChain`.

---

## Context & Background

Task 1 added text tests for the intended water renderer architecture. Task 2 added `CustomForwardRenderer` as the Game/Editor shared renderer and left `waterRefractionCapturePass` as `object?`. This session completed the Task 3 slice only.

Relevant prior session:
- `docs/log/2026/06/24/2026-06-24-custom-forward-renderer-task2.md`

---

## What We Did

### 1. Added shared capture resources
**Files Changed:** `Terrain/Rendering/Water/WaterRefractionCaptureResources.cs`

- Added `WaterRefractionCaptureResources : IDisposable`.
- Allocates `RefractionTexture` as `PixelFormat.R16G16B16A16_Float` with render-target and shader-resource flags.
- Uses `RiverRenderResources.ComputeHalfResolutionSize` so the shared capture target keeps the existing river half-resolution convention.
- Added `ReleaseResources` and `Dispose`.

### 2. Added shared capture pass
**Files Changed:** `Terrain/Rendering/Water/WaterRefractionCapturePass.cs`

- Added `WaterRefractionCapturePass : IDisposable`.
- Owns `WaterRefractionCaptureResources` and `ImageEffectShader("WaterRefractionCapture", delaySetRenderTargets: true)`.
- `Capture(RenderDrawContext, RenderView, Texture, float)` binds:
  - Presenter depth resolved through `DepthBaseKeys.DepthStencil`
  - `CameraKeys.ViewSize`, `ZProjection`, `NearClipPlane`, `FarClipPlane`
  - `TransformationKeys.ViewInverse`, `Eye`, `ProjectionInverse`
  - `RiverCommonKeys._RefractionMaxCameraHeight`
  - `TexturingKeys.Sampler`
  - generated `WaterRefractionCaptureKeys` defaults
- Validates Presenter depth size against scene color size using the same assertion semantics as the old river scene capture path.
- Releases the resolved depth SRV in a `finally` block.

### 3. Added shader shell and key registration
**Files Changed:** `Terrain/Effects/Water/WaterRefractionCapture.sdsl`, `Terrain/Effects/Water/WaterRefractionCapture.sdsl.cs`, `Terrain/Terrain.csproj`

- Added `shader WaterRefractionCapture : ImageEffectShader, DepthBase, Transformation, RiverCommon`.
- Renamed the local exposure/color-scale parameters to capture semantics:
  - `_RefractionCaptureExposure`
  - `_RefractionCaptureColorScale`
- The shader compresses scene RGB and writes `RiverCompressWorldSpace(positionWS.xyz, Eye.xyz)` to alpha.
- Registered `Effects\Water\WaterRefractionCapture.sdsl.cs` as a generated compiled key file in `Terrain.csproj`.

### 4. Updated CustomForwardRenderer ownership
**Files Changed:** `Terrain/Rendering/CustomForwardRenderer.cs`

- Changed `waterRefractionCapturePass` from `object?` to `WaterRefractionCapturePass?`.
- Initializes the pass in `InitializeCore`.
- Disposes it in `Destroy`.
- Kept `DrawWaterRefractionCapture` no-op by design so Task 3 does not introduce an unused GPU pass or change current rendering.

---

## Problems Encountered & Solutions

### Stride generated-file target did not create the new key file
**Symptom:** `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` returned success but did not create `WaterRefractionCapture.sdsl.cs`.

**Solution:** Added the generated key file in the same shape as existing Stride-generated SDSL key files, then reran the required Stride generated-file and asset targets. The pass references `WaterRefractionCaptureKeys`, so build will catch any key drift.

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet build Terrain.sln --no-restore`
  - Passed with existing package vulnerability warnings and existing code warnings.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
  - Failed only on expected future Task 4/5 checks:
    - `ocean render feature exposes renderer callable water draw`
    - `river render feature exposes renderer callable water chain`

---

## Architecture Impact

Shared water refraction capture now exists as a renderer-owned helper, but it is not yet wired into the frame. River still owns its existing internal scene capture path, and Ocean/River transparent rendering remains unchanged until the later renderer-callable draw tasks.

No ADR was created; this implements an already planned architecture slice.

---

## Next Session

1. Task 4: expose renderer-callable Ocean water draw and bind the shared capture result.
2. Task 5: expose renderer-callable River water chain and remove direct ownership of the old river scene capture from `RiverRenderFeature`.
3. Remove transparent-stage double draw only after both renderer-callable paths exist.

---

## Session Statistics

**Files Changed:** 9
**Commits:** 0

---

## Quick Reference for Future Agents

- `DrawWaterRefractionCapture` intentionally does not call `Capture` yet.
- Do not add `DrawWater` / `DrawWaterChain` as part of this slice.
- Keep shader source files registered through `AdditionalFiles`; generated `.sdsl.cs` files remain compiled C#.
