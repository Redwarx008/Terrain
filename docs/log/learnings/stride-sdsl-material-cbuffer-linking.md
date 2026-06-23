# Stride SDSL Material CBuffer Linking

When custom SDSL shaders are inserted through Stride material features, cbuffer value parameters can fail to bind even when texture resources bind correctly.

## Symptom

- RenderDoc shows texture resources bound and non-empty.
- The same `PerMaterial` cbuffer contains some correct values from other shaders.
- Scalar values from a material diffuse/pixel shader remain at defaults such as `0`.
- Shader fallback branches such as `MaterialArraySize == 0` are taken.

## Fix Pattern

For cbuffer members set from C# using generated key classes, add explicit links:

```sdsl
cbuffer PerMaterial
{
    [Link("MyShader.MyValue")]
    stage float MyValue;
}
```

This keeps shader reflection key names aligned with generated C# keys such as `MyShaderKeys.MyValue`.

## Why

Stride's material shader composition can alter automatic link naming for value parameters. Stage texture resources may still bind, so do not assume a texture binding success proves scalar cbuffer bindings are correct.

## Verification

Use RenderDoc on the target draw and read the relevant cbuffer. The fix is validated only when the GPU cbuffer values match the values set in C#.

## Multi-Targeted Project Asset Targets

`Terrain/Terrain.csproj` uses `TargetFrameworks`, so invoking Stride asset targets on the outer project can fail with `MSB4057` even though `Stride.Core.Assets.CompilerApp.targets` contains the target. Run the same Stride target against the inner TFM:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
```

If the outer command reports the target does not exist, do not assume the Stride package is missing. First check whether the project is multi-targeted and whether the target appears in the NuGet package targets for the selected TFM.
