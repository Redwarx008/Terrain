#nullable enable

using Terrain.Editor.Models;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

public sealed record ToolOptionViewModel(
    string Label,
    string Description,
    string Glyph,
    EditorMode Mode,
    HeightTool? HeightTool = null,
    PaintTool? PaintTool = null);
