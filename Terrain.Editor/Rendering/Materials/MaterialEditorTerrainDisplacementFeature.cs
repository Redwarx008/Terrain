#nullable enable

using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain.Editor.Rendering.Materials;

[DataContract]
[Display("Editor Terrain Displacement")]
public sealed class MaterialEditorTerrainDisplacementFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("EditorTerrainDisplacement"));
        context.AddShaderSource(MaterialShaderStage.Vertex, mixin);
    }
}
