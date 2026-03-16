#nullable enable

using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain;

[DataContract]
[Display("Terrain Displacement")]
public sealed class MaterialTerrainDisplacementFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("MaterialTerrainDisplacement"));
        context.AddShaderSource(MaterialShaderStage.Vertex, mixin);
    }
}
