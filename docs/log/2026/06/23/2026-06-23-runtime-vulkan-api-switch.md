# Runtime Vulkan API Switch
**Date**: 2026-06-23
**Session**: runtime graphics API follow-up
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Switch the Windows runtime project toward Vulkan for a minimize/restore rendering comparison.

**Success Criteria:**
- `Terrain.Windows` requests Vulkan through Stride's supported project property.
- Keep using local Stride source-built packages rather than a public/prebuilt package version.
- Verify the final runtime output actually copies the Vulkan backend DLL.

---

## Context & Background

**Current State:**
- The project previously used local Stride `4.3.0.1` packages built from `E:\WorkSpace\stride`.
- `SpaceEscape.Windows.csproj` was used as a local reference and sets `<StrideGraphicsApi>Vulkan</StrideGraphicsApi>`.

**Why Now:**
- The runtime restore/minimize path needs to be compared on another graphics backend.

---

## What We Did

### 1. Set Runtime API Request To Vulkan
**Files Changed:** `Terrain.Windows/Terrain.Windows.csproj`

**Implementation:**
```xml
<StrideGraphicsApi>Vulkan</StrideGraphicsApi>
```

**Rationale:**
- This matches Stride's documented API switch mechanism and the local `SpaceEscape` sample layout.

### 2. Rebuilt Local Stride Vulkan Packages
**Files Changed:** `E:\WorkSpace\stride\bin\packages\*.4.3.0.2743.nupkg`

**Implementation:**
```powershell
msbuild build\Stride.build /t:Package /p:StrideGraphicsApis=Vulkan /p:StrideBuildPrerequisitesInstaller=false /p:StrideSign=false /m /v:m
```

**Rationale:**
- The old local `4.3.0.1` package only contained the Direct3D11 backend.
- The new local package version generated from the same source tree is `4.3.0.2743` and includes Vulkan runtime assemblies.

### 3. Updated Terrain To The New Local Package Version
**Files Changed:** `Directory.Packages.props`

**Implementation:**
- Updated the Terrain Stride package versions from `4.3.0.1` to `4.3.0.2743`.

**Rationale:**
- `4.3.0.2743` is the package version produced by the current local `E:\WorkSpace\stride` package build.
- Switching to `4.3.0.2507` was avoided because that would stop testing the local engine build.

---

## What Worked ✅

1. **Project-level API switch**
   - `Terrain.Windows` now requests Vulkan using the same property as `SpaceEscape`.

2. **Local package inspection**
   - Confirmed the old `4.3.0.1` package contained only `Direct3D11` plus the default DLLs.
   - Confirmed `Stride.Graphics.4.3.0.2743.nupkg` contains `lib/net10.0/Vulkan/Stride.Graphics.dll`.

3. **Runtime output verification**
   - `Terrain.Windows` restore/build now resolves Stride runtime DLLs from `lib/net10.0/Vulkan`.
   - `Bin\Windows\Debug\win-x64\Stride.Graphics.dll` hash matches the Vulkan backend DLL from the local `4.3.0.2743` package.

---

## What Didn't Work ❌

1. **Only changing the project property**
   - What we tried: set `Terrain.Windows` to Vulkan and restore.
   - Why it is insufficient: the local `4.3.0.1` package does not contain a `Vulkan` backend folder for `Stride.Graphics`.
   - Lesson learned: Stride API selection has two layers: project requests an API, and the consumed NuGet package must contain that backend.

---

## Next Session

### Immediate Next Steps
1. Launch `Terrain.Windows` and manually test minimize/restore under Vulkan.
2. If the same `1x1` scaler exception still appears, continue diagnosing the restore frame lifecycle with the global backbuffer guard in place.

---

## Quick Reference For Future Claude

**What Claude Should Know:**
- `Terrain.Windows/Terrain.Windows.csproj` is the correct place for `<StrideGraphicsApi>Vulkan</StrideGraphicsApi>`.
- `Directory.Packages.props` now uses local source-built Stride `4.3.0.2743`.
- The final runtime output has been verified to use the Vulkan `Stride.Graphics.dll`.

**Gotchas For Next Session:**
- Verify the final copied `Stride.Graphics.dll` hash/path when changing Stride package versions or graphics API.
