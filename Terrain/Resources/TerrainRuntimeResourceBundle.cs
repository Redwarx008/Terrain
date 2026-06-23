#nullable enable

using System.Collections.Generic;

namespace Terrain.Resources;

public sealed class TerrainRuntimeResourceBundle
{
    public string TerrainDataPath { get; init; } = string.Empty;
    public string MaterialDescriptorPath { get; init; } = string.Empty;
    public string MaterialsDirectory { get; init; } = string.Empty;
    public string? RiversPath { get; init; }
    public bool HasDeclaredProvinces { get; init; }
    public float HeightScale { get; init; }
    public float RiverMinWidth { get; init; } = 1.0f;
    public float RiverMaxWidth { get; init; } = 4.0f;
    public float RiverMaxVisibleCameraHeight { get; init; } = 3000.0f;
    public float SeaLevel { get; init; } = 3.8f;
    public RuntimeMaterialDescriptor MaterialDescriptor { get; init; } = new();
    public List<RuntimeMaterialTextureSlot> MaterialTextureSlots { get; init; } = new();
    public List<string> Diagnostics { get; init; } = new();
}
