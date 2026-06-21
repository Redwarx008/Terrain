# River Surface Bank Transition Source Audit
**Date**: 2026-06-21
**Session**: 8
**Status**: Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Diagnose why `C:\Users\Redwa\Desktop\debug.rdc` shows the left half of the river with almost no visible bottom bank.

**Success Criteria:**
- Identify whether the loss happens in bottom, surface, or post-processing.
- Verify the alpha hypothesis with a live shader replacement before keeping source changes.
- Check CK3 source shader entry points instead of relying on similarly named helper functions.
- Rebuild Stride shader assets after source changes.

---

## Context & Background

**Previous Work:**
- Recent sessions had already aligned river bottom/surface/refraction, fixed tone mapping, depth bias, and river mesh smoothing.
- First pass on this issue incorrectly added a surface-only `_SurfaceBankFade=0.30` workaround after seeing surface alpha saturate quickly near the left bank.

**Current State:**
- River rendering uses `bottom -> refraction -> surface`.
- `RiverSurface` is intended to follow CK3 `river_surface.shader` through `CalcRiverAdvanced -> CalcWater`.

---

## What We Did

### 1. RenderDoc Diagnosis
**Files Inspected:** `C:\Users\Redwa\Desktop\debug.rdc`

- Opened the capture through `renderdoc-mcp`.
- Confirmed D3D11 capture with 78 events and 65 draw calls.
- Identified river bottom draw at event `276` and river surface draw at event `309`.
- Exported:
  - `rt_276_0.png`: bottom pass, half resolution `836x498`
  - `rt_309_0.png`: surface pass, full resolution `1672x996`
  - `rt_965_0.png`: final output
- Pixel history showed bottom data exists at the affected left-side bank.
- Surface pixel history showed the surface pass covers the same area and reaches alpha `1.0` quickly.

**Initial Conclusion:**
- Bottom is present.
- The visible loss happens after bottom, during surface composition.
- The first alpha-fade fix was still only a hypothesis and needed source/parity verification.

### 2. CK3 Source Audit
**Files Inspected:**
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\river_surface.shader`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\jomini\jomini_river_surface.fxh`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\jomini\jomini_water_default.fxh`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river.fxh`

**Findings:**
- `river_surface.shader` uses `PixelShader = "PixelShader"` and returns `CalcRiverAdvanced(Input)._Color`.
- The `CalcRiverSurface` helper contains `Color.a = saturate(Depth * 2.0f / _Depth) * Input.Transparency * connectionFade`, but that helper is not the active entry path for this effect.
- `Depth` comes from:
  - `CalcDepth(Input.UV)`
  - `return _Depth * (1.0f - pow(cos(UV.y * 2.0f * PI) * 0.5f + 0.5f, 2.0f));`
- In the active advanced path, that depth is assigned to `Params._Depth = Depth * Input.Width + 0.1f` and is consumed by `CalcWater`, `WaterFade`, and refraction.
- Final alpha in `CalcRiverAdvanced` is:
  - `Input.Transparency * saturate((Input.DistanceToMain - 0.1f) * 5.0f)` under refraction
  - multiplied by `_BankFade` edge fades on `Input.UV.y` and `1.0f - Input.UV.y`.

**Corrected Conclusion:**
- The earlier `_SurfaceBankFade` workaround was not CK3 parity.
- The active CK3 branch already uses `_BankFade`; the depth alpha belongs to a non-active helper path.

### 3. Hot-Replace Verification
**Surface draw:** event `309`

- Built a diagnostic replacement pixel shader that outputs advanced alpha as grayscale:
  - `connectionFade = saturate((DistanceToMain - 0.1f) * 5.0f)`
  - `edgeFade1/2 = smoothstep(0, 0.025, UV.y / 1-UV.y)`
  - `alpha = Transparency * connectionFade * edgeFade1 * edgeFade2`
- RenderDoc accepted the replacement and produced shader id `1000000000000000587`.
- Picked pixels after replacement:
  - `(500,204)`: `[1,1,1,1]`
  - `(500,205)`: `[1,1,1,1]`
  - `(500,206)`: `[1,1,1,1]`
  - `(500,212)`: `[1,1,1,1]`
  - `(500,219)`: `[1,1,1,1]`
- Pixel debug at `(500,204)` showed:
  - `RiverUV.y = 0.1321603`
  - `DistanceToMain = 1`
  - `Transparency = 1`

**Hot-Replace Conclusion:**
- At the problematic left-side pixels, CK3 advanced alpha is already fully opaque.
- `_BankFade=0.025` only affects a very thin screen-space edge because the first affected pixels already have `RiverUV.y` well above the fade range.
- This hot edit does not solve the bottom-bank visibility problem; it disproves the previous alpha-fade hypothesis.

### 4. Source Correction
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl.cs`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- Removed `_SurfaceBankFade`.
- Added/restored `_BankFade` in `RiverSurface`.
- Replaced the previous `ComputeSurfaceAlpha(...)` helper with `ComputeAdvancedSurfaceAlpha(...)`.
- Kept `worldDepth = CalcDepth(riverUv) * worldWidth + 0.1f` feeding `CalcWater`.
- Bound `RiverSurfaceKeys._BankFade` from `riverObject.BankFade`.
- Updated text tests to require the advanced alpha branch and reject `_SurfaceBankFade`.

### 5. See-Through Depth Hot-Replace
**Surface draw:** event `309`

- Replaced surface PS with raw `RefractionTexture` output.
- At bad left-bank pixels, raw refraction still contained visible brown bottom:
  - `(500,204)`: `[0.635, 0.409, 0.204]`
  - `(500,205)`: `[0.652, 0.418, 0.206]`
  - `(500,212)`: `[0.611, 0.401, 0.212]`
- Replaced surface PS with just `CalcTerrainUnderwaterSeeThrough`.
- The output matched the original surface RGB almost exactly:
  - original shaderOut `(500,204)`: `[0.09253, 0.27052, 0.26642]`
  - replacement `(500,204)`: `[0.09253, 0.27052, 0.26636]`
- Scalar diagnostic showed:
  - attenuation `0.02~0.03`
  - refractionDepth about `6~8`
  - shoreMask `0`
- This means the bottom existed, but see-through kept only 2-3% of it and lerped the rest to water-color map.

**Validated Delta:**
- Replaced see-through depth with `min(refractionDepth, inputDepth)`, where `inputDepth` is the advanced river profile depth already passed into `CalcWater`.
- Result:
  - `(500,204)` changed from `[0.0925, 0.2705, 0.2664]` to `[0.5815, 0.3953, 0.2098]`
  - `(500,205)` changed to `[0.5840, 0.4001, 0.2137]`
  - `(500,212)` changed to `[0.5137, 0.3770, 0.2225]`

**Conclusion:**
- The remaining left-bank invisibility is not alpha, bottom pass, or missing refraction input.
- The immediate cause is see-through attenuation using a deep refraction payload on a shallow river-profile bank.

### 6. Rejected See-Through Depth Cap
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- Initially tried `float seeThroughDepth = min(refractionDepth, Depth);` after final base/offset refraction selection.
- User verified this made the whole water surface lose its water color.
- Rechecked CK3 source and reverted the cap.
- Added a regression assertion that `RiverSurface` must call `CalcTerrainUnderwaterSeeThrough(refractionDepth, ...)` and must not keep `seeThroughDepth`.

**Correct CK3 Control Chain:**
- `CalcRefraction` samples/decompresses raw refraction payload.
- `Depth = min(Depth, RefractionDepth)` controls `RefractionShoreMask`.
- `CalcTerrainUnderwaterSeeThrough` is called with `RefractionDepth`, not the min depth.
- `CalcWater` later recomputes `Depth = min(Input._Depth, RefractionDepth)` for `WaterFade`.
- Final water color is controlled by lit water color times `WaterFade`, refraction/see-through, reflection, and `Fresnel * WaterFade`.

### 7. Hot-Replace Density Probe After Rejection
**Context:**
- User verified the source-side `min(refractionDepth, Depth)` cap made the whole water surface lose its water color.
- Reopened `C:\Users\Redwa\Desktop\debug.rdc`, restored all shader replacements, and tested only CK3's see-through attenuation control chain.

**Diagnostic Replacement:**
- Packed CK3-style controls into RGB at surface draw `309`:
  - `R = WaterFade`
  - `G = exp(-_WaterSeeThroughDensity * WaterDistance)`
  - `B = RefractionDepth / 20`
- Confirmed `(650,212)` was not written by draw `309` via pixel history, so only pixels written by the river surface draw are valid for this probe.

**Observed Values:**
- `(500,204)`: `WaterFade=0`, attenuation `0.01996`, `RefractionDepth≈6.78`.
- `(500,212)`: `WaterFade≈0.273`, attenuation `0.02931`, `RefractionDepth≈6.15`.
- `(500,219)`: `WaterFade=0`, attenuation `0.01070`, `RefractionDepth≈7.94`.
- This confirms the bottom is hidden primarily because CK3 see-through attenuation keeps only about 1-3% of raw bottom color at these pixels.

**Density Hot-Replace Results:**
- Kept the CK3 `CalcTerrainUnderwaterSeeThrough(refractionDepth, ...)` path and varied only `_WaterSeeThroughDensity`.
- `density=0.6`:
  - `(500,204)` -> `[0.1108, 0.2751, 0.2642]`
  - `(500,212)` -> `[0.1190, 0.2781, 0.2646]`
  - `(500,219)` -> `[0.0983, 0.2712, 0.2651]`
- `density=0.4`:
  - `(500,204)` -> `[0.1597, 0.2876, 0.2585]`
  - `(500,212)` -> `[0.1722, 0.2915, 0.2590]`
  - `(500,219)` -> `[0.1337, 0.2791, 0.2600]`
- `density=0.2`:
  - `(500,204)` -> `[0.2896, 0.3208, 0.2435]`
  - `(500,212)` -> `[0.3008, 0.3235, 0.2454]`
  - `(500,219)` -> `[0.2439, 0.3032, 0.2438]`
- `0.4` is the best current hot-replace candidate: it exposes more bank/bottom than CK3 default `0.8` while avoiding the full bottom-color takeover caused by the rejected depth cap.

**Exported Comparison Images:**
- Original: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_309_original.png`
- `density=0.6`: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_309_density_0_6.png`
- `density=0.4`: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_309_density_0_4.png`

**Conclusion:**
- Do not reintroduce `seeThroughDepth = min(refractionDepth, Depth)`.
- If this is turned into a source fix, make it an explicit parameter/tuning path around `_WaterSeeThroughDensity` or the map-unit distance scale; do not silently change CK3 formula structure.
- The capture was restored with `shader_restore_all` after the hot-replace probe.

### 8. `debug1.rdc` Pointed Bank Convergence Probe
**Context:**
- User provided a new clue from `C:\Users\Redwa\Desktop\debug1.rdc`: the transition from visible bottom bank to invisible bottom bank does not fade gradually; it converges to a pointed tip.
- Reopened `debug1.rdc` and inspected the river draws:
  - `276/290/304`: `RiverBottom`.
  - `337/357/377`: `RiverSurface`.

**RenderDoc Evidence:**
- Exported surface output at event `377`: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_377_0.png`.
- Exported bottom/refraction output at event `304`: `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_304_0.png`.
- Bottom RT still contains brown river-bottom/bank color in the area; the pointed loss appears after surface/refraction composition.

**Pixel History / Shader Inputs:**
- `(620,845)` was not written by the surface draw, showing part of the apparent transition is coverage/geometry, not color fading.
- Valid surface samples:
  - `(700,870)`: event `337`, primitive `867`, `RiverUV.y≈0.357`, output `[0.1428,0.3054,0.3019]`.
  - `(900,795)`: event `337`, primitive `880`, `RiverUV.y≈0.329`, output `[0.1895,0.2355,0.1980]`.
  - `(980,730)`: event `337`, primitive `888`, `RiverUV.y≈0.111`, output `[0.4917,0.3234,0.1641]`.
- Bottom pass for the corresponding half-res pixels uses the same primitive region and the same interpolated `RiverUV.y` pattern.

**Hot-Replaced Diagnostics:**
- Replaced the surface PS to visualize cross-section UV and near-bank mask:
  - `R = RiverUV.y`
  - `G = near-bank mask`
  - `B = edge distance / longitudinal stripe`
- Exported:
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_377_surface_uvy_bankmask.png`
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_377_surface_uvy_bankmask_crop.png`
- The diagnostic reproduces the same pointed convergence: the visible bank band itself enters/exits at that tip.

**Second Diagnostic:**
- Replaced surface PS with:
  - `R = RiverUV.y`
  - `G = CalcTerrainUnderwaterSeeThrough attenuation`
  - `B = RefractionDepth / 10`
- Exported:
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_377_uv_atten_depth.png`
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_377_uv_atten_depth_crop.png`
- Sample values:
  - `(700,870)`: `UV.y=0.357`, attenuation `0.0618`, `RefractionDepth≈6.71`; bottom is almost fully replaced by water color.
  - `(900,795)`: `UV.y=0.329`, attenuation `0.4958`, `RefractionDepth≈1.72`; partial bottom survives.
  - `(980,730)`: `UV.y=0.111`, attenuation `0.9487`, `RefractionDepth≈0.13`; bottom/bank survives strongly.

**Conclusion:**
- The pointed transition is real in shader inputs. It is not caused by `_BankFade`, not by final alpha, and not by a single global density value.
- The immediate mechanism is the shared river mesh/UV/refraction-depth field: the visible near-bank `RiverUV.y` band and the shallow `RefractionDepth` band both converge at the same screen-space tip.
- Further fixes should target river mesh/UV/coverage semantics, or an explicit bank visibility model, not another global color/density tweak.

### 9. Rejected Mesh Direction
**Temporary Files Changed, Then Reverted:**
- `Terrain.Editor/Services/RiverMeshService.cs`
- `Terrain.Editor.Tests/Program.cs`
- `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`

**What Was Tried:**
- Temporarily changed `BuildRiverMesh` from the original two-column ribbon to five cross-section lanes at `UV.y = 0, 0.15, 0.5, 0.85, 1`.
- After `debug2.rdc` still showed the pointed region, temporarily tried capping acute miter expansion at `halfWidth * sqrt(2)`.

**Why This Was Wrong:**
- User correctly pointed out that original mesh generation should not be assumed faulty.
- The RenderDoc evidence only showed `RiverUV.y`, see-through attenuation, and `RefractionDepth` diagnostics converging at the same screen-space tip. That proves the observed fields share the same pointed coverage, but it does not prove the source mesh generator is wrong.
- The five-lane and miter-clamp changes were therefore over-inferred from diagnostic signals and have been reverted.

**Current Direction:**
- Keep original mesh generation.
- The pointed tip has now been traced to filtered refraction alpha payload reads in the surface shader, not mesh generation.

### 10. `debug2.rdc` Refraction Payload Filtering Root Cause
**Context:**
- User provided `C:\Users\Redwa\Desktop\debug2.rdc` and noted that the pointed area did not disappear after the mesh-direction experiments.
- User also clarified that depth test was not the current target; the issue had to be in surface shader output/control.

**CK3 Cross-Check:**
- Opened `C:\Users\Redwa\Desktop\ck3-river.rdc`.
- CK3 surface draw `460/466` reads `RefractionTexture_Texture` from the bottom/refraction RT.
- CK3 bottom/refraction RT is also half resolution: `1280x720` feeding a `2560x1440` surface pass.
- Therefore the fix is not "make local refraction full resolution"; CK3 also uses half-res refraction.

**Local Capture Evidence:**
- Reopened `debug2.rdc`.
- Local `BottomColor/JominiRefraction` equivalent is `ResourceId::7769`, `836x498`, feeding surface RT `1672x996`.
- Surface event `377`, bottom event `304`.
- Local refraction RT alpha range at event `377`: about `0.002..72.9`; CK3 capture resource alpha range was about `41..195`. This initially suggested payload writer differences, but bottom shader reflection confirmed local RT0 alpha is still `RiverCompressWorldSpace(bottomWorldPosition, _CameraWorldPosition)`.

**Hot-Replaced Diagnostic:**
- Replaced surface PS at event `377` with:
  - `R = DecodeRefractionDepth(linear Sample alpha) / 12`
  - `G = DecodeRefractionDepth(point/Load alpha) / 12`
  - `B = abs(linearAlpha - pointAlpha) / 40`
- Valid pixels:
  - `(1000,600)`: linear `0.734`, point `0.724`, alpha delta `0.003`; normal area, no big mismatch.
  - `(1100,540)`: linear `1.0` saturated, point `0.277`, alpha delta `0.383`; bad pointed area.
  - `(1180,500)`: linear `0.0626`, point `0.0493`, alpha delta `0.004`; normal shallow area.

**Validated Candidate:**
- Replaced surface PS with a minimal visual candidate:
  - keep refraction RGB sampled linearly;
  - read only the alpha payload through `Texture2D.Load`;
  - decode world position/depth from that unfiltered payload.
- The bad pointed pixel `(1100,540)` changed to visible bottom-like color `[0.2407, 0.1993, 0.1561]`, while the previous linear-depth path had treated it like very deep water and swallowed the bottom.

**Root Cause:**
- `RefractionTexture.Sample(LinearClamp, screenUv).a` linearly blends camera-distance payloads across half-res texel boundaries.
- RGB can be linearly filtered, but alpha is not a color; it is a non-linear distance payload later passed to `DecompressWorldSpace`.
- At the pointed bank edge, one half-res texel can contain bottom payload while a neighbor contains scene/other distance payload. Linear interpolation creates a synthetic distance that decodes to a much deeper refraction point, so see-through attenuation collapses abruptly into the pointed shape.

**Source Fix:**
- `RiverSurface.sdsl` now separates refraction color sampling from payload sampling:
  - `RefractionTexture.Sample(RefractionSampler, uv)` remains for RGB.
  - `SampleRefractionPayload(uv)` uses `_RefractionTextureSize` and `RefractionTexture.Load(...)` for alpha.
  - base, offset, and water-fade refraction world-position decodes all use the unfiltered payload.
- `RiverRenderFeature` binds `_RefractionTextureSize` from the current `BottomColor` texture.
- `RiverShaderTextTests` now rejects decoding directly from `refractionSample.a` / `offsetRefractionSample.a`.

### 11. Height-Boundary Bottom Darkening Recheck
**Context:**
- User later observed that the remaining missing bottom happens at the boundary between low and high terrain, with the high side unable to show the bottom.
- Reopened `C:\Users\Redwa\Desktop\debug2.rdc` and shifted the investigation away from depth test, mesh, and surface alpha.

**RenderDoc Evidence:**
- Surface draw `377` was not suppressing an existing bottom color. At the bad high-side pixel `(1180,500)`, raw base `RefractionTexture.rgb` was already near black: about `[0.0012,0.0016,0.0021]`.
- The corresponding bottom half-res pixel `(590,250)` was written by bottom draw `304` with shader output about `[0.00124,0.00159,0.00216,2.15]`.
- A nearby good pixel `(580,240)` was written by the same bottom pass with about `[0.540,0.357,0.178,3.06]`.
- Raw `BottomDiffuse` diagnostics showed both good and bad pixels sample similarly dark albedo, so the difference is not simply `tangentUv` vs `worldUv`.

**CK3 Source Check:**
- CK3 `game/gfx/FX/river_bottom.shader` calls `CalcRiverBottom(Input)`, not `CalcRiverBottomAdvanced(Input)`.
- CK3 `CalcRiverBottom` still routes the material through `CalculateSunLighting`, whose IBL path includes `DiffuseIBL + SpecularIBL`.
- Current `RiverStrideLighting.sdsl` had `RiverStrideComputeEnvironmentDiffuse(...)` hardcoded to `float3(0,0,0)`.

**Root Cause:**
- At high/low terrain boundaries, the river bottom can legitimately fall into terrain shadow.
- With diffuse IBL disabled, shadowed bottom retains almost no diffuse environment light and collapses to near black; only weak specular IBL remains.
- This specifically explains why the issue correlates with high terrain while not requiring a mesh or surface-alpha failure.

**Source Fix:**
- `RiverStrideComputeEnvironmentDiffuse(...)` now samples the scene skybox cubemap at the lowest-frequency mip using the lit normal direction and multiplies by `diffuseColor * _EnvironmentIntensity * environmentIntensityScale`.
- `RiverShaderTextTests` now rejects the old zero diffuse-IBL assumption and requires the low-frequency cubemap diffuse term.

**Verification:**
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore` passed.
- `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` passed.
- `StrideCleanAsset` passed.
- `StrideCompileAsset` passed with the existing `X3557` shader warning and `891 succeeded, 0 failed`.
- Attempted automatic RenderDoc recapture of `Terrain.Editor.exe` to `C:\Users\Redwa\Desktop\river_ibl_fix_verify.rdc`, but `capture_frame` timed out without producing a capture. The launched editor process was closed cleanly.

---

## Verification

Commands run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Results:
- Updated tests first failed against the previous source, proving the guard was active.
- After the source correction, tests passed.
- The corrected see-through-depth test failed while `seeThroughDepth` was present and passed after reverting to the CK3 `RefractionDepth` path.
- RenderDoc hot replacement after the rejection validated `density=0.4` as a candidate and `density=0.2` as likely too bottom-heavy.
- A temporary five-lane mesh experiment and temporary acute miter clamp were tried, then rejected and reverted after user correction.
- `debug2.rdc` hot replacement proved the pointed bad pixel has a large linear-vs-point refraction payload mismatch; using point/Load alpha restores bottom color at the pointed pixel.
- Before the mesh experiment was rejected, `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore` had passed; after rejection the mesh source/tests were restored to the original two-column ribbon.
- Stride generated-file update passed.
- Stride asset clean passed.
- Stride asset compile passed with `891 succeeded, 0 failed`.
- Final post-fix `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore` passed.
- Follow-up diffuse-IBL fix tests passed, generated-file update passed, Stride clean passed, and Stride asset compile passed with `891 succeeded, 0 failed`.
- Automatic RenderDoc recapture after the diffuse-IBL fix timed out before writing a capture; runtime visual verification still needs a fresh manual capture.
- Existing NuGet/compiler warnings remain.
- Existing shader compiler warning `X3557: loop doesn't seem to do anything, forcing loop to unroll` remains.

---

## Architecture Impact

- Updated `docs/ARCHITECTURE_OVERVIEW.md`.
- Updated `docs/CURRENT_FEATURES.md`.
- Updated `docs/log/learnings/stride-river-rendering-patterns.md` with the CK3 entrypoint trap.
- No ADR created; this is a correction inside the existing ADR-014 river rendering scope.

---

## Quick Reference for Future Claude

**What Changed Since Last Doc Read:**
- `RiverSurface` no longer has `_SurfaceBankFade`.
- `RiverSurface` final alpha follows CK3 `CalcRiverAdvanced`: `Transparency * connectionFade * _BankFade` edge fades.
- `CalcDepth(UV) * Width + 0.1` is still present, but it feeds `CalcWater` depth/refraction semantics, not the final advanced alpha.
- `CalcRefraction` uses CK3's `RefractionDepth` argument for `CalcTerrainUnderwaterSeeThrough`; it must not cap see-through by profile depth.
- `CalcRefraction` no longer decodes world position from linearly filtered refraction alpha. RGB stays linear; alpha payload is read with `Texture2D.Load` using `_RefractionTextureSize`.
- `RiverStrideLighting` now includes diffuse IBL from the scene skybox's lowest-frequency mip, so shadowed high-terrain-side bottom pixels are not left with only specular IBL.
- `BuildRiverMesh` remains the original 2-column left/right ribbon after the rejected mesh experiment was reverted.

**Gotchas:**
- Do not infer active shader behavior from `jomini_river_surface.fxh` helper names alone; check `river_surface.shader` entrypoint and defines first.
- The left-side bottom-bank issue was not fixed by the hot-replaced advanced alpha formula. A depth-cap hot-replace exposed bottom locally but broke water color globally, so it is recorded as a rejected hypothesis.
- Do not resume the `RefractionDepth`/depth-test direction for the pointed tip unless a new capture explicitly proves that path.
- Do not infer a mesh source bug merely because `RiverUV.y`, attenuation, and `RefractionDepth` diagnostics converge at the same tip; that was the mistake behind the reverted mesh experiment.
- Do not linearly filter refraction alpha payloads. Distance payloads are not colors; filtering them before `DecompressWorldSpace` creates synthetic depths and pointed bank discontinuities.
- If visual tuning continues, recapture after rebuilding/rerunning the editor and check both the old left-side pixels near `x=500,y=204..219` and the `debug1.rdc` pointed-bank area near `x=700..980,y=730..870`.
