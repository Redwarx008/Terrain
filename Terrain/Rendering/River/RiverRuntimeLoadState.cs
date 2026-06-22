#nullable enable

namespace Terrain.Rendering.River;

public enum RiverRuntimeLoadState
{
    NotAttempted,
    Loaded,
    NoRiverResource,
    Failed,
}

public readonly record struct RiverRuntimeLoadConfig(
    string? RiversPath,
    float RiverMinWidth,
    float RiverMaxWidth,
    float RiverMaxVisibleCameraHeight,
    float HeightScale,
    int HeightmapWidth,
    int HeightmapHeight);
