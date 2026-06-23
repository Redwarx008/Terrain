# Runtime Direct3D11 API Switch
**Date**: 2026-06-23
**Session**: runtime graphics API switch
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Switch `Terrain.Windows` back to Direct3D11.

**Success Criteria:**
- `Terrain.Windows` requests Direct3D11.
- Local Stride package contains a Direct3D11 backend.
- Runtime output copies the Direct3D11 `Stride.Graphics.dll`.
- Runtime starts without immediately exiting.

---

## Context & Background

**Current State Before Work:**
- `Terrain.Windows.csproj` was set to `Direct3D12`.
- The local `Stride.Graphics.4.3.0.2743.nupkg` and NuGet global package cache contained Direct3D12 backend DLLs.

**Why Now:**
- User requested switching runtime back to DX11 after D3D12 startup/debugging work.

---

## What We Did

### 1. Changed Runtime Graphics API
**Files Changed:** `Terrain.Windows/Terrain.Windows.csproj`

**Implementation:**
```xml
<StrideGraphicsApi>Direct3D11</StrideGraphicsApi>
```

### 2. Rebuilt Local Stride Graphics Package For DX11
**Command:**
```powershell
msbuild build\Stride.build /t:Package /p:StrideGraphicsApis=Direct3D11 /p:StrideBuildPrerequisitesInstaller=false /p:StrideSign=false /m /v:m
```

**Result:**
- `E:\WorkSpace\stride\bin\packages\Stride.Graphics.4.3.0.2743.nupkg` now contains:
  - `lib/net10.0/Direct3D11/Stride.Graphics.dll`
  - `lib/net10.0/Stride.Graphics.dll`

### 3. Refreshed NuGet Package Cache
**Action:**
- Removed only `C:\Users\Redwa\.nuget\packages\stride.graphics\4.3.0.2743`.
- Ran `dotnet restore Terrain.Windows\Terrain.Windows.csproj`.

**Rationale:**
- NuGet would otherwise reuse the old same-version Direct3D12 package from the global cache.

---

## Verification

- `dotnet restore Terrain.Windows\Terrain.Windows.csproj` passed.
- `dotnet build Terrain.Windows\Terrain.Windows.csproj --no-restore` passed.
- Output `Bin\Windows\Debug\win-x64\Stride.Graphics.dll` SHA256 matched both:
  - `C:\Users\Redwa\.nuget\packages\stride.graphics\4.3.0.2743\lib\net10.0\Direct3D11\Stride.Graphics.dll`
  - `C:\Users\Redwa\.nuget\packages\stride.graphics\4.3.0.2743\lib\net10.0\Stride.Graphics.dll`
- Runtime smoke launched `Terrain.Windows.exe` for 10 seconds; process stayed running and stderr was empty.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `Terrain.Windows` currently targets Direct3D11.
- The package version remains `4.3.0.2743`, but the local package/cache contents now contain Direct3D11 backend DLLs.
- If switching backend again without changing package version, clear the exact `stride.graphics\4.3.0.2743` NuGet cache directory before restore.

