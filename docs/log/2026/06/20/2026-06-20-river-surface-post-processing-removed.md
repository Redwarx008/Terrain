# River Surface Post Processing Removed
**Date**: 2026-06-20
**Session**: River surface wrapper removal
**Status**: ✅ Complete
**Priority**: High

---

## Superseded By

Later in the same day, `docs/log/2026/06/20/2026-06-20-river-surface-post-processing-restored.md` restored `ApplySurfacePostProcessing` after `debug5.rdc` showed that direct `CalcRiverAdvanced` output was darker than the previous wrapper path. The current source no longer has `_WaterColorSurfaceLift` or `visibleWaterColor`; it restores deterministic terrain shadow tint and map distance fog while keeping procedural cloud shadow removed.

---

## Session Goal

**Primary Objective:**
- Remove `ApplySurfacePostProcessing` from `RiverSurface.sdsl`.
- Make the surface pixel shader write `CalcRiverAdvanced` output directly.

---

## What We Did

### 1. Removed RiverSurface Wrapper Post Step
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- Removed `ApplySurfacePostProcessing`.
- Removed terrain-shadow/fog wrapper helpers:
  - `CalculateRiverTerrainNormal`
  - `ApplyOvercastContrast`
  - `ApplyTerrainShadowTintWithClouds`
  - `CalculateDistanceFogFactor`
  - `CalculateFogColor`
  - `ApplyMapDistanceFogWithoutFoW`
- Removed editor terrain height sampling helpers and inputs used only by the wrapper.
- Removed shadow tint texture/sampler inputs used only by the wrapper.
- `PSMain` now does:

```hlsl
float4 waterColor;
CalcRiverAdvanced(waterColor);
streams.ColorTarget = waterColor;
```

### 2. Removed Dead Surface Bindings
**Files Changed:**
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
- `Terrain.Editor/Assets/River/README.md`

**Implementation:**
- Removed `BindSurfaceRequiredInputs` / `TryBindEditorTerrainInputs`.
- Removed surface binding of `ShadowNoiseTexture`, `TerrainHeightSampler`, and editor height slices.
- Surface draw no longer returns early when editor terrain height slices are unavailable.
- Removed `shadow_color.dds` from river resource loading and required texture checks because no active shader consumes it.

### 3. Updated Tests and Shader Keys
**Files Changed:**
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `Terrain.Editor/Effects/RiverSurface.sdsl.cs`

**Implementation:**
- Updated text tests to require direct `CalcRiverAdvanced` output and reject post wrapper symbols.
- Regenerated `RiverSurface.sdsl.cs`.

### 4. Restored Visible Water-Color Contribution
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**RenderDoc Hot Edit:**
- In `C:\Users\Redwa\Desktop\debug5.rdc`, event `156` used `RiverSurface` PS `ResourceId::7822`.
- Original representative pixel `(840,560)` output was approximately `[0.059, 0.080, 0.070, 1]`.
- Hot-replaced the PS to output `WaterColorTexture.rgb * 8` using the surface world/map UV path.
- The same pixel became approximately `[0.089, 0.414, 0.439, 1]`, and the river became visibly blue-green.

**Implementation:**
- Added `_WaterColorSurfaceLift = 8.0f`.
- After `CalcWater` combines direct light, refraction, and reflection, it now applies:

```hlsl
float3 visibleWaterColor = waterColorAndSpec.rgb * _WaterColorSurfaceLift * waterFade;
waterColor = max(waterColor, visibleWaterColor);
```

This keeps the post wrapper deleted while preventing the raw `CalcWater` path from collapsing below the sampled `WaterColorTexture` contribution in the current editor output path.

---

## Verification

Ran:

```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Result:
- Shader key generation succeeded.
- Stride asset compile succeeded.
- Existing HLSL warning `X3557: loop doesn't seem to do anything, forcing loop to unroll` remains unrelated.

Attempted:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug
```

Initial run was blocked by:
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs(707,35)`: `ISceneRenderer` has no `PostEffects` member.

Follow-up fix:
- `ConfigureEditorToneMap` now walks the compositor renderer tree and reads `PostEffects` from the concrete `ForwardRenderer`.
- Traversal handles `SceneCameraRenderer`, `PresenterViewportSceneRenderer`, and `SceneRendererCollection`.

Follow-up verification:

```powershell
dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug
dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug
```

Result:
- `Terrain.Editor` build succeeded.
- `Terrain.Editor.Tests` build succeeded; Stride asset compiler reported `0 failed`.
- Full test run executed river shader, tonemap, and Stride effect compiler checks successfully.
- Full test run still fails on the unrelated repository hygiene check `repository does not track any game files`, because `git ls-files game` returns tracked `game/map/...` resources.

After the water-color lift:

```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug /p:UseAppHost=false
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug /p:UseAppHost=false
```

Result:
- Shader key generation succeeded; `RiverSurfaceKeys._WaterColorSurfaceLift` was generated.
- Stride asset compile succeeded: `889 succeeded`, `0 failed`.
- `Terrain.Editor` C# build succeeded with `/p:UseAppHost=false`.
- River shader text tests and Stride effect compiler tests passed.
- Full test run still fails only on the unrelated tracked `game/map/...` hygiene assertion.
- Normal `dotnet run` / apphost-copy build can be blocked while `Terrain.Editor.exe` is already running; observed lock was PID `11832`.

---

## Architecture Impact

- Historical state for this isolation session: river surface no longer ran the CK3-style wrapper after `CalcRiverAdvanced`.
- This was superseded later on 2026-06-20. Current source restores `ApplySurfacePostProcessing` with terrain shadow tint and map distance fog.
- Dynamic procedural cloud shadow remains removed; `cloudMask` is fixed to `0.0f`.
- The temporary water-color texture floor was removed when the wrapper was restored.

---

## Next Session

1. Decide whether to untrack existing `game/map/...` files or relax the repository hygiene test for this branch.
2. Capture a fresh frame and verify the restored deterministic wrapper output, especially terrain shadow tint and map distance fog with `cloudMask=0`.
