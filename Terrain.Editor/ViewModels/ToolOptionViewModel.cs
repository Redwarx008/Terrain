#nullable enable

using Terrain.Editor.Models;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

public enum EditorToolKind
{
    None,
    BiomeBrush,
    RoadPath,
    RiverBrush,        // 保留旧值不删除，不注册使用
    RiverChannel,
    RiverSource,
    RiverConfluence,
    RiverBifurcation,
    RiverOcean,
    RiverEraser,
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
