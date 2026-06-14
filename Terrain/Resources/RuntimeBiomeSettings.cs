#nullable enable

using System.Collections.Generic;

namespace Terrain.Resources;

public sealed class RuntimeBiomeSettings
{
    public List<RuntimeBiomeEntry> Biomes { get; } = new();
    public List<RuntimeBiomeLayerEntry> Layers { get; } = new();
    public List<RuntimeBiomeModifierEntry> Modifiers { get; } = new();
}

public sealed class RuntimeBiomeEntry
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class RuntimeBiomeLayerEntry
{
    public int Id { get; init; }
    public int BiomeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MaterialId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public bool Enabled { get; init; }
    public bool Visible { get; init; }
}

public sealed class RuntimeBiomeModifierEntry
{
    public int Id { get; init; }
    public int LayerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string BlendMode { get; init; } = string.Empty;
    public float Min { get; init; }
    public float Max { get; init; }
    public float MinFalloff { get; init; }
    public float MaxFalloff { get; init; }
    public float Radius { get; init; } = 1.0f;
    public float AngleDegrees { get; init; }
    public float AngleRangeDegrees { get; init; } = 180.0f;
    public float Scale { get; init; } = 1.0f;
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float Seed { get; init; }
    public float Octaves { get; init; } = 4.0f;
    public float Invert { get; init; }
    public string? TextureMaskPath { get; init; }
    public int TextureMaskChannel { get; init; }
    public float Opacity { get; init; }
    public bool Enabled { get; init; }
    public bool Visible { get; init; }
}
