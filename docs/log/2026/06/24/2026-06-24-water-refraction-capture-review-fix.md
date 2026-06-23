# Water Refraction Capture Review Fix
**Date**: 2026-06-24
**Session**: Task 3 code-quality review fix
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Address code review feedback for `WaterRefractionCapturePass` release-mode depth validation.

---

## What We Did

**Files Changed:** `Terrain/Rendering/Water/WaterRefractionCapturePass.cs`

- Replaced presenter/depth `Debug.Assert`-only checks with explicit `InvalidOperationException` failures for:
  - missing `GraphicsDevice.Presenter`
  - missing `Presenter.DepthStencilBuffer`
  - presenter depth size mismatch with scene color
- Changed `ResolveDepthStencil` null handling to throw before binding parameters or drawing.
- Kept `ReleaseDepthStenctilAsShaderResource` inside `finally`, but only after `sceneDepth` has been proven non-null.
- Changed `CameraKeys.ViewSize` binding to use `sceneColor.ViewWidth/ViewHeight`, matching resource allocation and depth validation.

---

## Verification

- `dotnet build Terrain.sln --no-restore`
  - Passed with existing package vulnerability warnings and existing code warnings.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
  - Failed only on expected future Task 4/5 checks:
    - `ocean render feature exposes renderer callable water draw`
    - `river render feature exposes renderer callable water chain`

---

## Next Session

Continue with Task 4/5 renderer-callable Ocean/River water draw wiring.

---

## Session Statistics

**Files Changed:** 2
**Commits:** 0
