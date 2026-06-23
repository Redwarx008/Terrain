# Stride Shader AdditionalFiles Build-System Fix
**Date**: 2026-06-24
**Session**: Stride shader project item fix
**Status**: Complete
**Priority**: High

---

## Session Goal

Fix Stride asset build error:

```text
Shader or Effect file is using old build system. Please use ItemType AdditionalFiles instead of None and remove the Generator metadata
```

---

## What We Did

### 1. Updated shader source item registration

**Files Changed:** `Terrain/Terrain.csproj`, `Terrain.Editor/Terrain.Editor.csproj`

- Replaced active `.sdsl` / `.sdfx` shader source entries from `None Update` plus `LastGenOutput` to `None Remove` plus `AdditionalFiles Include` wildcard entries.
- Kept generated `*.sdsl.cs` files as `Compile Update` items because runtime C# still depends on generated `*Keys` classes.
- Updated disabled XML comment snippets so they no longer document the old shader item pattern.

### 2. Updated regression tests

**Files Changed:** `Terrain.Editor.Tests/OceanShaderTextTests.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

- Ocean and shared river lighting shader registration tests now assert `AdditionalFiles`.
- Tests now reject legacy shader-source `None` and `LastGenOutput` metadata for the covered shaders.

### 3. Captured reusable learning

**Files Changed:** `docs/log/learnings/stride-shader-additionalfiles-build-system.md`, `docs/log/learnings/README.md`

- Documented the current Stride target requirement: shader source files use `AdditionalFiles`; generated shader key files remain compiled C#.

---

## Problems Encountered & Solutions

### Current Stride target rejects older project item metadata

**Symptom:** Asset build reports old shader build system usage.

**Root Cause:** Project files still used `None Update="Effects\...\*.sdsl"` and `LastGenOutput`.

**Solution:** Convert shader source items to:

```xml
<None Remove="Effects\**\*.sdsl" />
<None Remove="Effects\**\*.sdfx" />
<AdditionalFiles Include="Effects\**\*.sdsl" />
<AdditionalFiles Include="Effects\**\*.sdfx" />
```

and remove `LastGenOutput` / `Generator` metadata.

---

## Verification

- `rg -n '<None Update=".*\.(sdsl|sdfx)"|LastGenOutput|Generator="StrideShaderKeyGenerator"' Terrain/Terrain.csproj Terrain.Editor/Terrain.Editor.csproj Terrain.Windows/Terrain.Windows.csproj Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`
  - Result: no matches.
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed, with pre-existing shader loop warning only.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
  - Passed, with existing package vulnerability and code warnings.
- `dotnet build Terrain.sln --no-restore`
  - Passed, with existing package vulnerability warnings.

---

## Quick Reference for Future Agents

- In this project, `.sdsl` and `.sdfx` source files are removed from `None` and explicitly included as `AdditionalFiles`.
- Do not reintroduce `None Update` / `LastGenOutput` / `Generator` for shader source files.
- Keep generated `*.sdsl.cs` / `*.sdfx.cs` key files compiled when C# references generated key classes.
