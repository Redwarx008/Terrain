# WaterColorTexture Linear Import Test
**Date**: 2026-06-19
**Session**: River rendering color-space experiment
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Temporarily import `WaterColorTexture` as linear data to test whether disabling sRGB sampling improves the river surface result.

---

## What We Did

### 1. Changed Water Color Texture Sampling Mode
**Files Changed:** `Terrain.Editor/Assets/River/Water/water-color.sdtex`

Changed:
```yaml
UseSRgbSampling: true
```

to:
```yaml
UseSRgbSampling: false
```

This makes the Stride asset compiler import `River/Water/water-color` without sRGB sampling/decode.

### 2. Rebuilt Editor Assets

Ran:
```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Result:
- `StrideCleanAsset` succeeded.
- `StrideCompileAsset` succeeded.
- Asset compiler reported `913 succeeded, 0 failed`.
- One existing shader warning appeared: `warning X3557: loop doesn't seem to do anything, forcing loop to unroll`.

---

## Next Session

### Immediate Next Steps
1. Run the editor and capture/inspect `debug.rdc` again.
2. Confirm the GPU resource format for `WaterColorTexture` is now non-sRGB.
3. Compare river surface color before/after at the same pixel.

### Risk
- CK3 uses the corresponding water color resource as sRGB in the inspected capture, so this is an experiment, not a confirmed parity fix.

---

## Quick Reference for Future Claude

**Current experiment state:**
- `Terrain.Editor/Assets/River/Water/water-color.sdtex` now has `UseSRgbSampling: false`.
- If this makes water too bright or diverges from CK3 parity, revert that single line to `true` and rebuild assets.
