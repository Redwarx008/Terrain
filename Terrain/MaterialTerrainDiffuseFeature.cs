#nullable enable

using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain;

[DataContract]
[Display("Terrain Diffuse")]
public sealed class MaterialTerrainDiffuseFeature : MaterialFeature, IMaterialDiffuseFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("MaterialTerrainDiffuse"));
        context.AddShaderSource(MaterialShaderStage.Pixel, mixin);
        context.AddStreamInitializer(MaterialShaderStage.Pixel, "TerrainMaterialStreamInitializer");
    }
}
