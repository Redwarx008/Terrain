#nullable enable

namespace Terrain.Resources;

public sealed class RuntimeMapDefinition
{
    public string HeightmapPath { get; init; } = string.Empty;
    public string TerrainDataPath { get; init; } = string.Empty;
    public string? RiversPath { get; init; }
    public string? ProvincesPath { get; init; }
    public float HeightScale { get; init; }
    public float RiverMinWidth { get; init; } = 1.0f;
    public float RiverMaxWidth { get; init; } = 4.0f;
    public float RiverMaxVisibleCameraHeight { get; init; } = 3000.0f;
}
