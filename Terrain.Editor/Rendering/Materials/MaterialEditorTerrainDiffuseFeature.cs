#nullable enable

using Stride.Core;
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain.Editor.Rendering.Materials;

[DataContract]
[Display("Editor Terrain Diffuse")]
public sealed class MaterialEditorTerrainDiffuseFeature : MaterialFeature, IMaterialDiffuseFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        var mixin = new ShaderMixinSource();
        mixin.Mixins.Add(new ShaderClassSource("EditorTerrainDiffuse"));
        context.AddShaderSource(MaterialShaderStage.Pixel, mixin);
    }
}
