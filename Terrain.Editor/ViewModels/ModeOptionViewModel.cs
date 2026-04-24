#nullable enable

using Terrain.Editor.Models;

namespace Terrain.Editor.ViewModels;

public sealed record ModeOptionViewModel(
    string Label,
    string Description,
    string Glyph,
    EditorMode Mode);
