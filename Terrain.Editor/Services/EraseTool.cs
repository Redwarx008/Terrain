#nullable enable

namespace Terrain.Editor.Services;

/// <summary>
/// Erase uses the same transition model as paint, with slot 0 as target material.
/// </summary>
internal sealed class EraseTool : IPaintTool
{
    public string Name => "Erase";

    public void Apply(ref PaintEditContext context)
    {
        PaintBrushCore.Apply(ref context, 0);
    }
}
