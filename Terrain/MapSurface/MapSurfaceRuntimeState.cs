#nullable enable

using Terrain.Resources;

namespace Terrain.MapSurface;

internal sealed class MapSurfaceRuntimeState
{
    public TerrainRuntimeResourceBundle? Resources { get; set; }
    public bool ResourcesLoaded { get; set; }
    public bool ResourceLoadFailed { get; set; }
    public string? ResourceLoadFailureDiagnostic { get; set; }
    public bool ContextApplied { get; set; }
    public bool MissingReferencesLogged { get; set; }
    public MapSurfaceRuntimeContext? Context { get; set; }
}
