# Restore Stride 4.3.0.2743 Packages
**Date**: 2026-06-23
**Session**: Stride package rollback after 4.4 source-generator IDE issue
**Status**: Complete
**Priority**: High

---

## Session Goal

Return the Terrain workspace to the local Stride package version that was previously used after the ImageMultiScaler crash work.

## Context & Background

- Testing Stride `4.4.0` packages exposed a Visual Studio design-time issue where shader source generator output was not visible to the active C# analysis.
- The prior working local package version documented for the ImageMultiScaler crash period was `4.3.0.2743`.
- `4.3.0.2743` uses the older generated `*.sdsl.cs` / `*.sdfx.cs` key-file workflow.

## What We Did

1. Restored direct Stride package versions in `Directory.Packages.props` to `4.3.0.2743`.
2. Removed transient `#error` generated shader files produced while probing the wrong package path.
3. Restored generated shader key files required by the `4.3.0.2743` build workflow.
4. Verified:
   - `dotnet build Terrain\Terrain.csproj -c Debug --no-incremental`
   - `dotnet build Terrain.Windows\Terrain.Windows.csproj -c Debug --no-incremental`
   - `dotnet build Terrain.Editor\Terrain.Editor.csproj -c Debug --no-incremental`

## Problems Encountered & Solutions

### Wrong 4.3 patch level

`4.3.0.1` restored but did not match the documented local ImageMultiScaler-crash work. It also exposed API differences and missing generated shader keys because the project had been moved toward the newer generator workflow.

**Solution:** Use `4.3.0.2743`, the documented local package version.

### Generated shader keys

`4.3.0.2743` expects checked-in/generated `*.sdsl.cs` and `*.sdfx.cs` files. With those files deleted, C# references such as `TerrainHeightParametersKeys` and `RiverSurfaceKeys` fail.

**Solution:** Restore the generated key files for the 4.3 workflow.

## Current State

- Runtime and editor builds both pass on `4.3.0.2743`.
- Asset compiler succeeds for both Windows runtime and Editor.
- Remaining warnings are existing NuGet advisory warnings, shader compiler loop warnings, and existing nullable/unused-field warnings.

## Gotchas

- Do not delete `Terrain/Effects/**/*.sdsl.cs`, `Terrain/Effects/**/*.sdfx.cs`, `Terrain.Editor/Effects/**/*.sdsl.cs`, or `Terrain.Editor/Effects/**/*.sdfx.cs` while using `4.3.0.2743`.
- If switching back to `4.4.0`, those generated key files must be removed again to avoid the new generator's old-build-system `#error`.

