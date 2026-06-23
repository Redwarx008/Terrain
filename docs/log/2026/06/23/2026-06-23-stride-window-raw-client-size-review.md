# Stride Window Raw Client Size Review
**Date**: 2026-06-23
**Session**: Subagent review follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Review and harden the Stride engine window resize fix for minimized WinForms windows.

**Success Criteria:**
- Keep the fix in the window/device resize layer, not in terrain, river, or post-processing render passes.
- Ensure minimized windows do not feed invalid client sizes into swapchain/presenter resize decisions.
- Rebuild the local Stride package and verify Terrain loads the rebuilt assembly.

---

## What We Did

### 1. Subagent Review
**Files Reviewed:** `E:\WorkSpace\stride\sources\engine\Stride.Games\*.cs`

- Started a focused code review subagent for the current Stride window resize diff.
- Accepted one concrete risk: WinForms minimized state can still report a non-zero tiny client size, so `RawClientSize` must treat minimized state as invalid.
- Accepted the platform-contract concern without making `RawClientSize` abstract; SDL now has its own raw-size override, while the base fallback remains for compatibility.

### 2. Engine Follow-Up Fixes
**Files Changed:**
- `E:\WorkSpace\stride\sources\engine\Stride.Games\Desktop\GameWindowWinforms.cs`
- `E:\WorkSpace\stride\sources\engine\Stride.Games\SDL\GameWindowSDL.cs`
- `E:\WorkSpace\stride\sources\engine\Stride.Games\GameWindow.cs`

**Implementation:**
- `GameWindowWinforms.RawClientSize` now returns `(0, 0)` while minimized.
- `GameWindowSDL.RawClientSize` now reads `window.ClientSize` directly instead of inheriting clamped `ClientBounds`.
- `GameWindow.RawClientSize` documentation now states that platforms with native size access should override it.

---

## Verification

- `dotnet build E:\WorkSpace\stride\sources\engine\Stride.Games\Stride.Games.csproj -c Debug`
  - Result: 0 errors, existing warnings only.
- `msbuild E:\WorkSpace\stride\build\Stride.build /t:Package /p:StrideGraphicsApis=Direct3D11 /p:StrideBuildPrerequisitesInstaller=false /p:StrideSign=false /m /v:m`
  - Result: package build completed.
- Cleared `C:\Users\Redwa\.nuget\packages\stride*\4.3.0.2743`.
- `dotnet build E:\Stride Projects\Terrain\Terrain.Windows\Terrain.Windows.csproj -c Debug`
  - Result: 0 errors, existing warnings only.
- Verified runtime and NuGet cache assemblies match:
  - `E:\Stride Projects\Terrain\Bin\Windows\Debug\win-x64\Stride.Games.dll`
  - `C:\Users\Redwa\.nuget\packages\stride.games\4.3.0.2743\lib\net10.0-windows7.0\Direct3D11\Stride.Games.dll`
  - SHA256: `843301C36338A08C0F6856BCDB6F7E4BC25BE96CA8B0F559AA1954E5E0C1A1D9`

---

## Next Session

### Immediate Next Steps
1. Manually run Terrain and verify minimize/restore behavior in the actual game window.
2. If the issue recurs, capture the raw client size, `WindowState`, presenter size, and backbuffer size across minimize and restore.

---

## Quick Reference for Future Claude

**Current status:**
- The local Direct3D11 Stride package has been rebuilt and Terrain is loading the rebuilt `Stride.Games.dll`.

**Key implementation:**
- `E:\WorkSpace\stride\sources\engine\Stride.Games\Desktop\GameWindowWinforms.cs:383`
- `E:\WorkSpace\stride\sources\engine\Stride.Games\SDL\GameWindowSDL.cs:349`
- `E:\WorkSpace\stride\sources\engine\Stride.Games\GameWindowRenderer.cs:207`
- `E:\WorkSpace\stride\sources\engine\Stride.Games\GraphicsDeviceManager.cs:996`

