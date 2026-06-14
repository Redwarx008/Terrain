#nullable enable

using System.Collections.Generic;

namespace Terrain.Resources;

public sealed class RuntimeMaterialDescriptor
{
    public List<RuntimeMaterialEntry> Materials { get; } = new();
}

public sealed class RuntimeMaterialEntry
{
    public string Id { get; init; } = string.Empty;
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? AlbedoPath { get; init; }
    public string? NormalPath { get; init; }
    public string? PropertiesPath { get; init; }
}

public sealed class RuntimeMaterialTextureSlot
{
    public string Id { get; init; } = string.Empty;
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? AlbedoPath { get; init; }
    public string? NormalPath { get; init; }
    public string? PropertiesPath { get; init; }
}
