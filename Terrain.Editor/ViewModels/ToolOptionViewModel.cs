#nullable enable

using Terrain.Editor.Models;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

public enum EditorToolKind
{
    None,
    BiomeBrush,
    RoadPath,
    FoliagePlace,
    FoliageRemove,
}

public sealed record ToolOptionViewModel(
    string Label,
    string Description,
    string Glyph,
    EditorMode Mode,
    HeightTool? HeightTool = null,
    PaintTool? PaintTool = null,
    EditorToolKind ToolKind = EditorToolKind.None);
