#nullable enable

namespace Terrain.Editor.Services;

/// <summary>
/// Paint material index values into the terrain material index map.
/// </summary>
internal sealed class PaintMaterialTool : IPaintTool
{
    public string Name => "Paint";

    public void Apply(ref PaintEditContext context)
    {
        PaintBrushCore.Apply(ref context, context.TargetMaterialIndex);
    }
}
