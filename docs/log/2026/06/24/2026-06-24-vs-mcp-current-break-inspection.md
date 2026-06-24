# VS MCP Terrain Shader Break Fix
**Date**: 2026-06-24
**Session**: VS MCP debugger inspection and runtime shader module fix
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

Inspect the Visual Studio debugger break, identify why `Terrain.Windows.exe` stopped in Stride shader compilation, and fix the project-side registration without adding a `Terrain.Windows.sdpkg` dependency on `Terrain/Effects`. After the runtime fix, apply the same shader registration correction to the Editor project.

---

## Context & Background

The workspace had `Terrain.sln` open in Visual Studio and `Terrain.Windows.exe` paused in the debugger.

Important project constraint confirmed during the session:
- `Terrain.Windows` should only depend on `Terrain`.
- `Terrain.Windows.sdpkg` should not include `../Terrain/Effects`.

---

## What We Did

### 1. Checked Visual Studio Debugger State

VS MCP reported:
- Mode: `Break`
- Break reason: `ExceptionNotHandled`
- Process: `E:\Stride Projects\Terrain\Bin\Windows\Debug\win-x64\Terrain.Windows.exe`
- Current source: `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\RootEffectRenderFeature.cs:591`
- Current function: `Stride.Rendering.RootEffectRenderFeature.PrepareEffectPermutations`

The throw happened at:

```csharp
renderEffect.PendingEffect.Wait();
```

The outer exception was `System.AggregateException`; the inner exception was `System.InvalidOperationException`:

```text
Could not compile shader.
E1202: The mixin [MaterialTerrainDiffuse] dependency is not in the module
E1202: The mixin [MaterialTerrainDisplacement] dependency is not in the module
```

### 2. Rejected the Wrong Windows-Package Direction

I briefly tested adding `../Terrain/Effects` to `Terrain.Windows.sdpkg`, but this was the wrong ownership boundary. The user correctly pointed out that `Terrain.Windows` has no independent shader package responsibility.

That temporary package change was reverted; `Terrain.Windows.sdpkg` has no final content change.

### 3. Found the Real Project Registration Gap

Confirmed:
- `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` exists.
- `Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl` exists.
- `Terrain/Terrain.sdpkg` includes `Effects`.
- Stride package targets in `Stride.Core.Assets.CompilerApp.targets` register `.sdsl` as:

```xml
<None Update="**\*.sdsl" Generator="StrideShaderKeyGenerator" />
```

The active `Terrain/Terrain.csproj` was removing all `Effects/**/*.sdsl` / `Effects/**/*.sdfx` from `None` and re-adding them as `AdditionalFiles`. That can let asset generation appear healthy while runtime effect compilation fails to find mixins in the shader source module.

### 4. Fixed Terrain Shader Source Registration

**Files Changed:**
- `Terrain/Terrain.csproj`
- `Terrain.Editor/Terrain.Editor.csproj`
- `Terrain.Editor.Tests/EditorShaderTextTests.cs`
- `Terrain.Editor.Tests/Program.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/log/learnings/stride-shader-additionalfiles-build-system.md`

Changes:
- Added missing generated metadata for `MaterialTerrainDisplacement.sdsl.cs`.
- Removed active `None Remove` / `AdditionalFiles Include` shader registration.
- Registered shader sources with:

```xml
<None Update="Effects\**\*.sdsl" Generator="StrideShaderKeyGenerator" />
<None Update="Effects\**\*.sdfx" Generator="StrideShaderKeyGenerator" />
```

Updated shader text regression tests so they now protect the correct Stride item pipeline.

### 5. Applied the Same Fix to Terrain.Editor

`Terrain.Editor/Terrain.Editor.csproj` used the same `None Remove` / `AdditionalFiles Include` pattern for editor-local shader sources.

Changes:
- Restored generated metadata for existing editor shader key files:
  - `BrushDecalShader.sdsl.cs`
  - `EditorTerrainBuildSplatMap.sdsl.cs`
  - `EditorTerrainDiffuse.sdsl.cs`
  - `EditorTerrainDisplacement.sdsl.cs`
  - `EditorTerrainForwardShadingEffect.sdfx.cs`
  - `EditorTerrainHeightParameters.sdsl.cs`
  - `EditorTerrainHeightStream.sdsl.cs`
- Registered editor shader sources with:

```xml
<None Update="Effects\**\*.sdsl" Generator="StrideShaderKeyGenerator" />
<None Update="Effects\**\*.sdfx" Generator="StrideShaderKeyGenerator" />
```

Added `EditorShaderTextTests` to prevent the Editor project from regressing back to `AdditionalFiles`.

---

## Why It Worked Before

The most likely reason is shader cache masking.

The runtime cache already contained older compiled `TerrainForwardShadingEffect` variants from previous runs. As long as the requested terrain permutation was already cached, the runtime did not need to compile a fresh permutation and therefore did not hit the missing-module path.

After a cache change, new permutation, or asset/build invalidation, Stride had to compile the effect again. At that point the local material mixins were not in the shader module because the `.sdsl` files had been moved out of the expected `None` item pipeline.

The same cache masking explanation applies to Editor-local shaders: old editor permutations could keep working until a fresh `EditorTerrain*` or brush shader permutation needed runtime compilation.

---

## Problems Encountered & Solutions

### Problem 1: Runtime Shader Module Could Not Resolve Terrain Material Mixins

**Symptom:** `Terrain.Windows.exe` stopped in `RootEffectRenderFeature.PrepareEffectPermutations` with E1202 for `MaterialTerrainDiffuse` and `MaterialTerrainDisplacement`.

**Root Cause:** `Terrain/Terrain.csproj` registered shader sources as `AdditionalFiles` after removing them from `None`; runtime effect compilation did not receive the local mixins as module dependencies for fresh permutations.

**Solution:** Keep `Effects/**/*.sdsl` and `Effects/**/*.sdfx` in the Stride `None` item pipeline with `StrideShaderKeyGenerator`, and keep generated `.sdsl.cs` files as compiled key files.

### Problem 2: Incomplete Generated Key Metadata

**Symptom:** `MaterialTerrainDiffuse.sdsl.cs` had explicit generated metadata, but `MaterialTerrainDisplacement.sdsl.cs` did not.

**Solution:** Added the missing `Compile Update` block for `MaterialTerrainDisplacement.sdsl.cs`.

---

## Architecture Impact

No feature architecture change.

Build-system learning updated:
- `docs/log/learnings/stride-shader-additionalfiles-build-system.md`

No update needed for:
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

---

## Code Quality Notes

### Testing

Passed:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
```

Runtime smoke:
- Launched `Terrain.Windows.exe` from `Bin\Windows\Debug\win-x64`.
- It stayed running after 20 seconds and was stopped manually.
- Output had river data warnings and one HLSL loop warning.
- Output did not contain `Could not compile shader` or `E1202`.

Editor runtime smoke:
- Launched `Terrain.Editor.exe` from `Bin\Editor\Debug\win-x64`.
- It stayed running after 20 seconds and was stopped manually.
- Shader error matches were 0 for `Could not compile shader`, `E1202`, `dependency is not in the module`, and editor shader names.

Existing NuGet vulnerability warnings and `TerrainRenderFeature.cs` nullable warning remain unchanged.

---

## Next Session

1. If Visual Studio still has an old debug session, restart debugging so it loads the newly built assemblies.
2. If another shader-module E1202 appears, inspect project item registration and runtime shader module contents before editing Windows package roots.

---

## Session Statistics

**Files Changed:** 5 code/project/test files, 2 documentation files
**Commits:** 0

---

## Quick Reference for Future Claude

- `Terrain.Windows.sdpkg` should remain package-local; do not add `../Terrain/Effects`.
- For this project, `Terrain` owns its shader sources through `Terrain/Terrain.sdpkg` and `Terrain/Terrain.csproj`.
- `Terrain.Editor` owns its editor-local shaders through `Terrain.Editor/Terrain.Editor.sdpkg` and `Terrain.Editor/Terrain.Editor.csproj`.
- Keep `.sdsl` / `.sdfx` sources as `None Update="Effects\**\*"` with `Generator="StrideShaderKeyGenerator"`.
- Keep generated `.sdsl.cs` files as `Compile Update` entries.
- Cache can hide shader registration problems until a fresh effect permutation is compiled.
