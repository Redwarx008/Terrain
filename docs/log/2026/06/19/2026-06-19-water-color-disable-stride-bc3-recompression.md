# WaterColorTexture Disable Stride BC3 Recompression
**Date**: 2026-06-19
**Session**: River rendering texture parity experiment
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Test avoiding Stride's BC3 recompression for `WaterColorTexture`, because RenderDoc showed the project-imported texture was darker than CK3's resource despite identical source DDS files.

---

## What We Found

The source DDS files are identical:
- `Terrain.Editor/Assets/River/Water/water-color.dds`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\watercolor_rgb_waterspec_a.dds`

Both have the same SHA256 and DDS header:
- `4608x2304`
- `13 mips`
- DX10 format `98`, i.e. BC7 container data.

RenderDoc comparison:
- CK3 capture resource: `BC7_SRGB`
- Project original import: `BC3_SRGB`
- Project linear experiment: `BC3_UNORM`

Stride source reason:
- `TextureHelper.DetermineOutputFormat` selects BC1/BC2/BC3 for compressed Windows color textures depending on alpha mode.
- The same source file notes BC7 support is not currently used because it is considered too slow to compile.

---

## What We Did

### 1. Disabled Stride Recompression for Water Color
**Files Changed:** `Terrain.Editor/Assets/River/Water/water-color.sdtex`

Changed the asset to:
```yaml
Source: !file water-color.dds
IsCompressed: false
Type: !ColorTextureType
    UseSRgbSampling: true
```

This restores CK3-like sRGB sampling semantics while preventing Stride from recompressing the BC7 DDS into BC3.

### 2. Rebuilt Editor Assets

Ran:
```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Result:
- Asset compiler succeeded.
- `913 succeeded, 0 failed`.
- Existing warning remained: `X3557 loop doesn't seem to do anything, forcing loop to unroll`.

---

## Next Session

### Immediate Next Steps
1. Run the editor and capture a new `debug.rdc`.
2. Confirm `WaterColorTexture` is no longer `BC3_*`.
3. Check whether it becomes uncompressed SRGB or preserves BC7/SRGB.
4. Compare texture stats against CK3 `BC7_SRGB` resource.

### Risk
- `IsCompressed: false` may increase GPU memory if Stride decompresses the BC7 DDS to uncompressed RGBA instead of preserving BC7.
- If memory becomes a concern, the better long-term fix is a custom path that preserves the original BC7 DDS and creates the desired SRGB view.

---

## Quick Reference for Future Claude

**Current experiment state:**
- `water-color.sdtex` has `IsCompressed: false` and `UseSRgbSampling: true`.
- This is intended to bypass Stride's BC3 recompression, not to change color-space semantics.

**Do not:**
- Re-disable sRGB sampling for this texture unless explicitly testing linear import again.
- Assume the source DDS differs from CK3; it has already been verified identical.
