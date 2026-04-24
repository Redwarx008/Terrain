#nullable enable

namespace Terrain.Editor.ViewModels;

public sealed record AssetBrowserItemViewModel(
    string Name,
    string Category,
    string Kind,
    string PreviewBackground,
    string PreviewForeground,
    string PreviewGlyph);
