# River Surface Depth Alpha Hotmodify
**Date**: 2026-06-19
**Session**: 15
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续分析更新后的 `C:\Users\Redwa\Desktop\debug.rdc`：foam 白块已消失后，surface 仍发黑的原因。

**Success Criteria:**
- 在修改 SDSL 前用 RenderDoc MCP 热替换验证 surface 发黑来自哪个环节。
- 对照 CK3 shader 源码确认公式是否一致。
- 只落地已热验证的最小 shader delta。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-surface-foam-ramp-wrap-bleed-hotmodify.md](./2026-06-19-river-surface-foam-ramp-wrap-bleed-hotmodify.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- current capture: `C:\Users\Redwa\Desktop\debug.rdc`
- current surface draw: `event 315`
- current bottom/refraction draw: `event 276`
- CK3 source: `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river_surface.fxh`

---

## What We Did

### 1. Reproduced the updated surface darkening
**Files Changed:** none

**Implementation:**
- Opened updated `debug.rdc` through RenderDoc MCP.
- Confirmed surface PS hash `1535f58b-0bc9f754-5d71474d-2e6398c7`.
- Read pixel history for prior points.

**Result:**
- Old white-foam point `(720,700)` is now dark `[0.062,0.047,0.034]`, proving foam fix reached GPU.
- `(930,650)` is `[0.109,0.080,0.050]`.
- Both are surface shader output with alpha `1.0`, not a post-blend artifact.

### 2. Separated refraction RGB from surface shading
**Files Changed:** none

**Implementation:**
- Hot replaced surface PS with raw `RefractionTexture` output.

**Result:**
- `(930,650)` raw refraction is about `[0.293,0.210,0.127]`.
- `(720,700)` raw refraction is about `[0.311,0.213,0.137]`.
- Bottom/refraction RGB is not black; surface formula and alpha are responsible for the visible dark overlay.

### 3. Dumped depth and WaterFade inputs
**Files Changed:** none

**Implementation:**
- Hot replaced surface PS to output `R=waterFade, G=InputDepth, B=RefractionDepth`.
- Hot replaced again to output `R=riverUv.y, G=worldWidth, B=profile`.

**Result:**
- `(930,650)`: `waterFade=0`, `InputDepth=0.218`, `RefractionDepth=1.073`.
- `(720,700)`: `waterFade=0.132`, `InputDepth=0.326`, `RefractionDepth=1.828`.
- `worldWidth≈1.5267`; shallow/bank regions are expected to have low WaterFade under CK3 formula.

### 4. Found and hot-validated the alpha mismatch
**Files Changed:** none during hot test

**Implementation:**
- Compared CK3 `jomini_river_surface.fxh`: surface alpha is `saturate(Depth * 2.0 / _Depth) * Input.Transparency * connectionFade`.
- Current shader used bottom-style `_BankFade` edge alpha.
- Hot replacement output `R=CK3 alpha, G=current alpha, B=riverUv.y`.
- Hot replacement then output `raw refraction RGB + CK3 alpha` to verify blend behavior.

**Result:**
- Center points: both alpha formulas are `1.0`.
- Bank point `(1000,620)`: CK3 alpha `0.303`, current alpha `1.0`, `riverUv.y≈0.091`.
- Bank point `(1100,600)`: CK3 alpha `0.25`, current alpha `1.0`, `riverUv.y≈0.082`.
- With raw refraction RGB and CK3 alpha, `(1000,620)` post-blend becomes `[2.318,1.529,0.901]` instead of fully covering the bright scene with dark water.

### 5. Ported the validated delta to SDSL
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- Replaced surface final alpha:
```csharp
waterColor.a = saturate(depth * 2.0f / max(_Depth, 0.0001f)) * transparency * connectionFade;
```
- Added a text regression test to prevent returning to `edgeFade1 * edgeFade2 * connectionFade * transparency`.

---

## Decisions Made

### Decision 1: Surface alpha follows CK3 depth fade, not bottom bank fade
**Context:** Low `WaterFade` is target behavior in shallow river regions, but current alpha made those regions fully opaque.
**Decision:** Use CK3 surface alpha formula from `jomini_river_surface.fxh`.
**Rationale:** RenderDoc replacement proved alpha is the reason shallow dark RGB fully covers the bright scene.

---

## What Worked ✅

1. **Raw refraction replacement**
   - It ruled out black bottom/refraction RGB.

2. **Alpha diagnostic replacement**
   - `R=CK3 alpha, G=current alpha, B=riverUv.y` directly exposed the formula mismatch.

3. **Post-blend validation**
   - Returning raw refraction with CK3 alpha proved the blend state actually uses alpha and changes the visible result.

---

## What Didn't Work ❌

1. **Treating surface and bottom alpha as interchangeable**
   - Bottom advanced alpha uses bank/diffuse semantics; surface alpha is depth/profile based.

---

## Verification

- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`
  - Passed.
  - Existing NuGet vulnerability warnings remain.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - Passed.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - Passed.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
  - Passed: `913 succeeded`, `0 failed`.
  - Existing shader loop-unroll warning remains.
- `dotnet build Terrain.Editor\Terrain.Editor.csproj -c Debug`
  - Passed.
  - Existing NuGet/C# warnings remain.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh `debug.rdc` and confirm surface disasm contains the depth-based alpha formula.
2. Re-check bank pixels around `(1000,620)` / `(1100,600)` to verify alpha is no longer `1.0`.
3. Continue separate remaining mismatch: current pre-bottom/refraction source still comes from a bright transparent-stage HDR buffer, unlike CK3's darker `JominiRefraction` payload.

### Gotchas
- Do not re-enable `HeightLookupTexture`, `PackedHeightTexture`, or `FogOfWarAlpha` for this issue; they were not the alpha root cause.
- Do not reuse bottom `_BankFade` alpha on surface.
- For surface replacements, keep the input struct aligned with `POSITION_WS`, `SV_Position`, `TEXCOORD1`, `TEXCOORD4`, `TEXCOORD5`, `TEXCOORD0`, `TEXCOORD3`.

---

## Session Statistics

**Files Changed:** 5 tracked files plus this log
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Foam fix is valid; updated capture no longer has white foam blocks.
- Surface shallow-region dark RGB can be expected from CK3 `WaterFade`, but alpha must fade by river depth profile.
- Current root fixed here: `RiverSurface` used bottom-style `_BankFade` alpha; CK3 surface uses depth-based alpha.
