# Stride Shader AdditionalFiles Build System

**Topic**: Stride SDSL/SDFX project item registration
**Date**: 2026-06-24
**Related Sessions**: [2026-06-24-stride-shader-additionalfiles-fix](../2026/06/24/2026-06-24-stride-shader-additionalfiles-fix.md)

---

## Problem / Context

Stride asset build failed with:

```text
Shader or Effect file is using old build system. Please use ItemType AdditionalFiles instead of None and remove the Generator metadata
```

The project still registered shader source files with `None Update="...sdsl"` and `LastGenOutput`. That was compatible with older guidance, but the current Stride targets reject it.

---

## Solution / Pattern

Remove shader source files from default `None` items, register them as `AdditionalFiles`, and keep generated key files as compiled C#:

```xml
<ItemGroup>
  <Compile Update="Effects\Ocean\OceanSurface.sdsl.cs">
    <DesignTime>True</DesignTime>
    <DesignTimeSharedInput>True</DesignTimeSharedInput>
    <AutoGen>True</AutoGen>
  </Compile>
</ItemGroup>

<ItemGroup>
  <None Remove="Effects\**\*.sdsl" />
  <None Remove="Effects\**\*.sdfx" />
  <AdditionalFiles Include="Effects\**\*.sdsl" />
  <AdditionalFiles Include="Effects\**\*.sdfx" />
</ItemGroup>
```

Do not put `Generator`, `LastGenOutput`, or shader-source `None` metadata on `.sdsl` / `.sdfx` files.

---

## Key Insights

### 1. Generated key files are still compiled

`*.sdsl.cs` files remain `Compile Update` items because runtime C# bindings use generated `*Keys` classes. The build-system change applies to shader source file items, not to generated C# key files.

### 2. Current Stride target error supersedes older templates

Older Stride examples and local notes may still mention `None Update` or `StrideShaderKeyGenerator`. If the asset target emits the AdditionalFiles error, follow the target error and update the project file.

---

## When to Use

- Adding or moving `.sdsl` / `.sdfx` files in this project.
- Fixing asset build errors that mention the old shader build system.
- Updating text tests that assert shader registration.

---

## Common Mistakes

### Mistake: Keeping `LastGenOutput` for shader source files

**What to avoid:**
- `None Update="Effects\...\*.sdsl"`
- `LastGenOutput`
- `Generator="StrideShaderKeyGenerator"`

**Why it's bad:**
- Current Stride targets classify that as the old build system and fail before shader compilation.

**Correct approach:**
- Remove `Effects\**\*.sdsl` / `Effects\**\*.sdfx` from `None`.
- Use `AdditionalFiles Include="Effects\**\*.sdsl"` / `AdditionalFiles Include="Effects\**\*.sdfx"` for shader source files.
- Keep `Compile Update="Effects\...\*.sdsl.cs"` for generated key files.

---

## Verification

Run:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
```

Also scan project files:

```powershell
rg -n '<None Update=".*\.(sdsl|sdfx)"|LastGenOutput|Generator="StrideShaderKeyGenerator"' Terrain/Terrain.csproj Terrain.Editor/Terrain.Editor.csproj
```

Expected: no matches.

---

*Learning Document Version: 1.0*
