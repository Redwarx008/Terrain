# Stride Shader Source Item Registration

**Topic**: Stride SDSL/SDFX project item registration
**Date**: 2026-06-24
**Updated**: 2026-06-24
**Related Sessions**:
- [2026-06-24-vs-mcp-current-break-inspection](../2026/06/24/2026-06-24-vs-mcp-current-break-inspection.md)
- [2026-06-24-stride-shader-additionalfiles-fix](../2026/06/24/2026-06-24-stride-shader-additionalfiles-fix.md)

---

## Problem / Context

`Terrain.Windows.exe` broke in Visual Studio at:

```text
E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\RootEffectRenderFeature.cs:591
renderEffect.PendingEffect.Wait()
```

The inner shader compiler error was:

```text
E1202: The mixin [MaterialTerrainDiffuse] dependency is not in the module
E1202: The mixin [MaterialTerrainDisplacement] dependency is not in the module
```

The shader files existed and `Terrain.sdpkg` included `Effects`, but `Terrain/Terrain.csproj` had removed `Effects/**/*.sdsl` and `Effects/**/*.sdfx` from `None` and re-added them as `AdditionalFiles`.

`Terrain.Editor/Terrain.Editor.csproj` had the same registration pattern for editor-local shaders, so the same missing-module failure could occur for `EditorTerrain*` and brush decal shader permutations.

That let some asset-generation paths appear healthy, but runtime effect compilation could still miss the shader source module entries for fresh permutations.

---

## Correct Pattern

For this project and the current Stride targets, keep shader source files in the `None` item pipeline with `StrideShaderKeyGenerator`, and keep generated key files as compiled C# in every Stride project that owns local shader sources:

```xml
<ItemGroup>
  <Compile Update="Effects\Material\MaterialTerrainDisplacement.sdsl.cs">
    <DesignTime>True</DesignTime>
    <DesignTimeSharedInput>True</DesignTimeSharedInput>
    <AutoGen>True</AutoGen>
  </Compile>
</ItemGroup>

<ItemGroup>
  <None Update="Effects\**\*.sdsl" Generator="StrideShaderKeyGenerator" />
  <None Update="Effects\**\*.sdfx" Generator="StrideShaderKeyGenerator" />
</ItemGroup>
```

Do not remove `Effects/**/*.sdsl` from `None` and do not rely on `AdditionalFiles` for runtime shader source registration.

---

## Why This Was Confusing

An earlier session interpreted an asset-build-system message as requiring `AdditionalFiles`. That conclusion was incomplete for this repository: the final check must include runtime effect compilation, not only generated-file update or asset build.

Existing cached compiled effects can hide the problem. The failure may only appear when a fresh effect permutation is requested or the effect cache is invalidated.

---

## Verification

Run all of these after changing shader registration:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet build Terrain.sln --no-restore
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Then run `Terrain.Windows.exe` and `Terrain.Editor.exe` long enough to request their effect permutations. A 20-second smoke run should not emit `Could not compile shader` or `E1202`.

---

## Common Mistakes

### Mistake: Moving project shaders to AdditionalFiles

**What to avoid:**

```xml
<None Remove="Effects\**\*.sdsl" />
<None Remove="Effects\**\*.sdfx" />
<AdditionalFiles Include="Effects\**\*.sdsl" />
<AdditionalFiles Include="Effects\**\*.sdfx" />
```

**Why it is bad:**
- Runtime effect compilation can fail to resolve local mixins from the shader module.
- Existing shader cache may mask the failure until a new permutation is compiled.

**Correct approach:**
- Keep shader sources as `None Update="Effects\**\*.sdsl"` / `None Update="Effects\**\*.sdfx"` with `Generator="StrideShaderKeyGenerator"`.
- Keep `Compile Update="Effects\...\*.sdsl.cs"` entries for generated key files.
- Verify with both asset compile and runtime smoke.

---

*Learning Document Version: 2.0*
