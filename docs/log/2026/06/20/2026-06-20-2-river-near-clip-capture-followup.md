# River Near Clip Capture Follow-Up
**Date**: 2026-06-20
**Session**: river hidden-through-mountain follow-up
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Re-check updated `C:\Users\Redwa\Desktop\debug.rdc` after the editor near-clip change because the foreground mountain still shows a river line.

**Success Criteria:**
- Identify whether the new capture actually uses near clip `10`.
- If not, harden the editor camera path so the next capture can verify the CK3-depth-distribution hypothesis.

---

## What We Found

The updated capture still has a visible river line across the foreground mountain. It is already present at river surface event `3346`, before fullscreen post-processing.

Pixel history on line pixels such as `(500,452)`, `(700,426)`, `(800,413)`, and `(1000,369)` shows:
- terrain draw `204` writes the occluding mountain first;
- sky/background `223` fails depth;
- river surface draw `3346` then passes depth and overwrites the color target;
- the depth value in history remains the terrain depth, so surface is still read-only depth.

The important new finding is that the capture did **not** use near clip `10`. At `(800,413)`, terrain debug shows:
- `SV_Position.w = 134.076309`
- `SV_Position.z = 0.999255538`

That depth matches a D3D perspective projection with near about `0.1`, not `10`. With near `10` and far `100000`, the same `w` would be around `0.9255`, not `0.99925`.

Therefore the remaining artifact in this capture is not evidence that CK3's raw `DepthBias=-50000` fails with near `10`; it proves the actual RenderView was still using the old near distribution.

A later updated `debug.rdc` changed the camera/view and frame structure again:
- total draws: `137`
- terrain draw: `204`
- second river surface pass: `810..1458`
- fullscreen post-processing: `1474..2114`

The visible river on the mountain is already present at surface event `1458`. Pixel history at `(974,416)` shows terrain draw `204` wrote depth `0.9982602`, then surface draw `1458` passed depth and overwrote the color. Terrain debug for the same pixel had `SV_Position.w=57.4945`; terrain debug at `(1030,468)` had `w=54.2849`, depth `0.9981650`. Both still imply near about `0.1`, not `10`.

For `(974,416)`, the surface shader input had biased `SV_Position.z=0.9962459`. Adding back the D24 `-50000` bias (`~0.00298`) puts the un-biased surface around `0.999226`, which is actually behind the terrain depth `0.998260`. So the river is only visible because the full CK3 raw bias is too strong for the actual near `0.1` depth distribution.

---

## Changes Made

- `Terrain/Assets/MainScene.sdscene`
  - Serialized `NearClipPlane: 10.0` on the MainScene camera asset, instead of relying only on runtime override.
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
  - Asset-loaded editor scene now removes non-editor components from cloned camera/light/background entities.
  - This strips the runtime `Terrain.BasicCameraController` script from the cloned camera entity, leaving the embedded editor `CameraController` as the only camera controller.
  - Transform, camera, light, and background components are preserved.
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
  - Expanded the near-clip regression test to assert MainScene serializes `NearClipPlane: 10.0`.
  - Added checks that asset scene cloning strips non-editor components while preserving transform/camera/light/background.
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
  - Kept CK3 raw `DepthBias=-50000` for render views whose actual near clip is at least `10`.
  - Replaced the temporary legacy-near branch with a continuous near-scaled bias: `abs(-50000) * pow(actualNear / 10, 0.5)`, clamped to the CK3 target magnitude.
  - With the current captured near `0.1`, the formula derives `-5000`; this is still much larger than the original `1e-5` near-coplanar terrain/water race, but it is not large enough to push the sampled hidden river pixels in front of the mountain.
- `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`
  - Added assertions that near `0.1` resolves to derived `-5000`, near `2.5` resolves to `-25000`, and near `10` resolves to `-50000`.

---

## Verification

- `git diff --check -- Terrain/Assets/MainScene.sdscene Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs Terrain.Editor.Tests/RiverShaderTextTests.cs`
  - Passed; Git reported only LF-to-CRLF normalization warnings.
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug /p:UseAppHost=false /p:OutDir="artifacts/verify-test/"`
  - Passed with existing warnings only.
- `dotnet Terrain.Editor.Tests/artifacts/verify-test/Terrain.Editor.Tests.dll`
  - River-related tests passed, including `editor camera uses target near clip for river depth bias`.
  - Final exit code is still `1` due to known unrelated failures: tracked `game/map/...` files and isolated `OutDir` source-path assumptions.

---

## Next Session

1. Rebuild/run the editor from the updated code and capture a fresh `debug.rdc`.
2. Confirm the surface rasterizer state in the new capture:
   - if near is still `0.1`, expected surface bias is derived `-5000`;
   - if near becomes `10`, expected surface bias is `-50000`.
3. If near is still `0.1`, the foreground hidden river should fail depth because the bad samples need roughly `0.0009` depth shift to punch through, while `-5000` only shifts about `0.000298`.
4. If near is `10` and the river still passes through the mountain, then the next hypothesis is that our river surface mesh/ribbon still rasterizes hidden pixels where CK3's visible water draw does not.

---

## Quick Reference

- Bad current capture event: surface draw `3346`.
- Later bad capture event: surface draw `1458`.
- Representative bad pixel: `(800,413)`.
- Later representative bad pixel: `(974,416)`.
- Capture-derived actual near before this fix: about `0.1`.
- Intended CK3-compatible editor near: `10.0`.
- Actual-near `0.1` surface bias from near-scaled formula: `-5000`.
