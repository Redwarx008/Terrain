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
