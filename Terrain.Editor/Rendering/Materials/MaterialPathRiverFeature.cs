#nullable enable

using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain.Editor.Rendering.Materials;

[DataContract]
[Display("Path River")]
public sealed class MaterialPathRiverFeature : MaterialFeature, IMaterialDiffuseFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("PathRiverSurface"));
        context.AddShaderSource(MaterialShaderStage.Pixel, mixin);
    }
}
