# River Shared Refraction Capture Task 4
**Date**: 2026-06-24
**Session**: Task 4 implementation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Move River bottom/surface rendering from its private `RiverSceneSeed` capture path to the renderer-owned shared `WaterRefractionCapturePass`, without changing Ocean internals.

---

## What We Did

**Files Changed:**
- `Terrain/Rendering/River/RiverRenderResources.cs`
- `Terrain/Rendering/River/RiverRenderFeature.cs`
- `Terrain/Rendering/CustomForwardRenderer.cs`
- `Terrain/Assets/GraphicsCompositor.sdgfxcomp`
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `Terrain.Editor.Tests/RuntimeRiverAssetTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Implementation:**
- Removed River-owned `SceneSeedColor` resource allocation.
- Added `EnsureResourcesForCaptureSize(...)` so River bottom targets match the shared half-resolution capture size directly.
- Removed `RiverRenderFeature` ownership of `ImageEffectShader("RiverSceneSeed")`, `SeedSceneColorFromScene`, and presenter-depth selection.
- Added renderer-callable `RiverRenderFeature.DrawWaterChain(...)`.
- Kept `RiverRenderFeature.Draw(...)` as a no-op to prevent generic Transparent double draw if a selector is left behind.
- `CustomForwardRenderer.DrawRiverWaterChain` now captures shared refraction after opaque rendering and invokes River bottom/surface chain before generic transparent rendering.
- Added a dedicated `Water` RenderStage. River selector now targets Water instead of Transparent, so Stride still performs collect/culling/sort while generic Transparent does not draw River.
- `WaterRefractionCapturePass.Capture(...)` restores caller render targets internally after writing the capture target.
- Runtime Game/Editor/SingleView and editor fallback compositor use `CustomForwardRenderer` with `WaterRenderStage` assigned. Ocean selector remains unchanged.
- `CustomForwardRenderer.DrawRiverWaterChain` now queries River draw-range `RiverMaxVisibleCameraHeight` before shared capture and skips the capture entirely for high-altitude cameras.

---

## Key Decision

River keeps a selector, but it routes to the dedicated Water stage instead of Transparent. This preserves Stride's normal collect/culling/sort pipeline and avoids the raw RenderObjects fallback, while letting `CustomForwardRenderer` own the actual water draw order and shared refraction capture.

---

## Verification

- `dotnet build Terrain.sln --no-restore`
  - Passed.
  - Existing warnings remain: NuGet vulnerability warnings, one existing nullable warning in `TerrainRenderFeature`, editor unused-field/event warnings, WinForms DPI manifest warning.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
  - River-related tests passed.
  - Failed only on expected Task 5 red light: `ocean render feature exposes renderer callable water draw`.

---

## Next Session

Task 5 should migrate Ocean to the same shared capture/order path and remove the expected `OceanRenderFeature.DrawWater(` failure.

---

## Session Statistics

**Files Changed:** 10
**Commits:** 0
