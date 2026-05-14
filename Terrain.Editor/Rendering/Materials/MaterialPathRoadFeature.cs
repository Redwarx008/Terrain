#nullable enable

using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain.Editor.Rendering.Materials;

[DataContract]
[Display("Path Road")]
public sealed class MaterialPathRoadFeature : MaterialFeature, IMaterialDiffuseFeature, IMaterialMicroSurfaceFeature, IMaterialSpecularFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("PathRoadSurface"));
        context.AddShaderSource(MaterialShaderStage.Pixel, mixin);
    }
}
