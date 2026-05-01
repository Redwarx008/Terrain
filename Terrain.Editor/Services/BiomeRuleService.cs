#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Terrain.Editor.Services;

public enum BiomeModifierType
{
    HeightRange = 0,
    SlopeRange = 1,
    CurvatureRange = 2,
    DirectionRange = 3,
    Noise = 4,
    TextureMask = 5,
}

public enum BiomeModifierBlendMode
{
    Multiply = 0,
    Add = 1,
    Subtract = 2,
    Min = 3,
    Max = 4,
}

public sealed class BiomeDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Vector4 DebugColor { get; set; } = new(0.3f, 0.8f, 0.3f, 1.0f);
}

public sealed class BiomeModifier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public BiomeModifierType Type { get; set; }
    public BiomeModifierBlendMode BlendMode { get; set; } = BiomeModifierBlendMode.Multiply;
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public float Opacity { get; set; } = 1.0f;

    public float Min { get; set; }
    public float Max { get; set; } = 1.0f;
    public float MinFalloff { get; set; }
    public float MaxFalloff { get; set; }
    public float Radius { get; set; } = 1.0f;
    public float AngleDegrees { get; set; }
    public float AngleRangeDegrees { get; set; } = 180.0f;
    public float Scale { get; set; } = 1.0f;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Seed { get; set; }
    public float Octaves { get; set; } = 4.0f;
    public float Invert { get; set; }
    public string? TextureMaskPath { get; set; }
    public int TextureMaskChannel { get; set; }

    public BiomeModifier Clone()
    {
        return (BiomeModifier)MemberwiseClone();
    }
}

public sealed class BiomeRuleLayer
{
    private BiomeModifier? legacyHeightModifier;
    private BiomeModifier? legacySlopeModifier;

    public int Id { get; set; }
    public string Name { get; set; } = "Layer";
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public int BiomeId { get; set; }
    public int MaterialSlotIndex { get; set; }
    public int PriorityOrder { get; set; }
    public List<BiomeModifier> Modifiers { get; } = new();

    public float MinAltitude
    {
        get => GetOrCreateLegacyHeightModifier().Min;
        set => GetOrCreateLegacyHeightModifier().Min = value;
    }

    public float MaxAltitude
    {
        get => GetOrCreateLegacyHeightModifier().Max;
        set => GetOrCreateLegacyHeightModifier().Max = value;
    }

    public float MinSlopeDegrees
    {
        get => GetOrCreateLegacySlopeModifier().Min;
        set => GetOrCreateLegacySlopeModifier().Min = value;
    }

    public float MaxSlopeDegrees
    {
        get => GetOrCreateLegacySlopeModifier().Max;
        set => GetOrCreateLegacySlopeModifier().Max = value;
    }

    public float BlendRange
    {
        get => MathF.Max(GetOrCreateLegacyHeightModifier().MinFalloff, GetOrCreateLegacyHeightModifier().MaxFalloff);
        set
        {
            float clamped = Math.Clamp(value, 0.0f, 1.0f);
            BiomeModifier height = GetOrCreateLegacyHeightModifier();
            height.MinFalloff = clamped;
            height.MaxFalloff = clamped;

            BiomeModifier slope = GetOrCreateLegacySlopeModifier();
            slope.MinFalloff = clamped;
            slope.MaxFalloff = clamped;
        }
    }

    public BiomeModifier GetOrCreateLegacyHeightModifier()
    {
        if (legacyHeightModifier != null)
            return legacyHeightModifier;

        legacyHeightModifier = Modifiers.FirstOrDefault(static modifier => modifier.Type == BiomeModifierType.HeightRange)
            ?? CreateDefaultModifier(BiomeModifierType.HeightRange, "Height range");

        if (!Modifiers.Contains(legacyHeightModifier))
            Modifiers.Insert(0, legacyHeightModifier);

        return legacyHeightModifier;
    }

    public BiomeModifier GetOrCreateLegacySlopeModifier()
    {
        if (legacySlopeModifier != null)
            return legacySlopeModifier;

        legacySlopeModifier = Modifiers.FirstOrDefault(static modifier => modifier.Type == BiomeModifierType.SlopeRange)
            ?? CreateDefaultModifier(BiomeModifierType.SlopeRange, "Slope range");

        if (!Modifiers.Contains(legacySlopeModifier))
            Modifiers.Add(legacySlopeModifier);

        return legacySlopeModifier;
    }

    public void EnsureLegacyModifiers()
    {
        _ = GetOrCreateLegacyHeightModifier();
        _ = GetOrCreateLegacySlopeModifier();
    }

    public static BiomeModifier CreateDefaultModifier(BiomeModifierType type, string? name = null)
    {
        return type switch
        {
            BiomeModifierType.HeightRange => new BiomeModifier
            {
                Type = type,
                Name = name ?? "Height range",
                Min = BiomeRuleService.MinHeight,
                Max = BiomeRuleService.DefaultMaxHeight,
            },
            BiomeModifierType.SlopeRange => new BiomeModifier
            {
                Type = type,
                Name = name ?? "Slope range",
                Min = 0.0f,
                Max = 90.0f,
            },
            BiomeModifierType.CurvatureRange => new BiomeModifier
            {
                Type = type,
                Name = name ?? "Curvature range",
                Min = 0.0f,
                Max = 0.25f,
                MinFalloff = 0.001f,
                MaxFalloff = 0.001f,
                Radius = 2.0f,
            },
            BiomeModifierType.DirectionRange => new BiomeModifier
            {
                Type = type,
                Name = name ?? "Direction range",
                Min = -180.0f,
                Max = 180.0f,
                AngleRangeDegrees = 90.0f,
            },
            BiomeModifierType.Noise => new BiomeModifier
            {
                Type = type,
                Name = name ?? "Noise",
                Scale = 0.05f,
                Octaves = 4.0f,
                Max = 1.0f,
            },
            BiomeModifierType.TextureMask => new BiomeModifier
            {
                Type = type,
                Name = name ?? "Texture mask",
                Max = 1.0f,
            },
            _ => new BiomeModifier { Type = type, Name = name ?? type.ToString() }
        };
    }
}

/// <summary>
/// Keeps the terrain texturing authoring state in one place so compute generation,
/// viewport previews, and inspector UI react to the same source of truth.
/// Manages the Biome -> Layer -> Modifier Stack hierarchy.
/// </summary>
public sealed class BiomeRuleService
{
    public const float MinHeight = 0.0f;
    public const float DefaultMaxHeight = 1000.0f;

    private static readonly Lazy<BiomeRuleService> InstanceFactory = new(() => new());

    private readonly List<BiomeDefinition> biomes = new();
    private readonly List<BiomeRuleLayer> layers = new();
    private int nextLayerId = 1;
    private int nextModifierId = 1;

    public static BiomeRuleService Instance => InstanceFactory.Value;

    public IReadOnlyList<BiomeDefinition> Biomes => biomes;
    public IReadOnlyList<BiomeRuleLayer> Layers => layers;

    public event EventHandler? StateChanged;

    private BiomeRuleService()
    {
        BiomeDefinition biome = AddBiomeCore("Default Biome");
        BiomeRuleLayer layer = AddLayerCore(biome.Id, "Default Base");
        layer.MaterialSlotIndex = 0;
        layer.MaxSlopeDegrees = 60.0f;
    }

    public BiomeDefinition AddBiome()
    {
        BiomeDefinition biome = AddBiomeCore();
        OnStateChanged();
        return biome;
    }

    public bool CanRemoveBiome(int biomeId)
    {
        foreach (BiomeRuleLayer layer in layers)
        {
            if (layer.BiomeId == biomeId)
                return false;
        }

        return biomes.Count > 1;
    }

    public bool RemoveBiome(int biomeId)
    {
        if (!CanRemoveBiome(biomeId))
            return false;

        int index = biomes.FindIndex(biome => biome.Id == biomeId);
        if (index < 0)
            return false;

        biomes.RemoveAt(index);
        OnStateChanged();
        return true;
    }

    public BiomeRuleLayer AddLayer(int biomeId)
    {
        BiomeRuleLayer layer = AddLayerCore(biomeId);
        OnStateChanged();
        return layer;
    }

    public void RemoveLayerAt(int index)
    {
        if (index < 0 || index >= layers.Count)
            return;

        layers.RemoveAt(index);
        RecomputePriorities();
        OnStateChanged();
    }

    public void MoveLayer(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= layers.Count)
            return;
        if (toIndex < 0 || toIndex >= layers.Count)
            return;
        if (fromIndex == toIndex)
            return;

        BiomeRuleLayer layer = layers[fromIndex];
        layers.RemoveAt(fromIndex);
        layers.Insert(toIndex, layer);
        RecomputePriorities();
        OnStateChanged();
    }

    public BiomeModifier AddModifier(BiomeRuleLayer layer, BiomeModifierType type)
    {
        ArgumentNullException.ThrowIfNull(layer);

        BiomeModifier modifier = BiomeRuleLayer.CreateDefaultModifier(type);
        modifier.Id = nextModifierId++;
        layer.Modifiers.Add(modifier);
        layer.EnsureLegacyModifiers();
        OnStateChanged();
        return modifier;
    }

    public void RemoveModifier(BiomeRuleLayer layer, int modifierIndex)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if ((uint)modifierIndex >= (uint)layer.Modifiers.Count)
            return;

        layer.Modifiers.RemoveAt(modifierIndex);
        layer.EnsureLegacyModifiers();
        OnStateChanged();
    }

    public void MoveModifier(BiomeRuleLayer layer, int fromIndex, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if ((uint)fromIndex >= (uint)layer.Modifiers.Count || (uint)toIndex >= (uint)layer.Modifiers.Count || fromIndex == toIndex)
            return;

        BiomeModifier modifier = layer.Modifiers[fromIndex];
        layer.Modifiers.RemoveAt(fromIndex);
        layer.Modifiers.Insert(toIndex, modifier);
        OnStateChanged();
    }

    public BiomeDefinition? FindBiome(int biomeId)
    {
        foreach (BiomeDefinition biome in biomes)
        {
            if (biome.Id == biomeId)
                return biome;
        }

        return null;
    }

    public IReadOnlyList<BiomeRuleLayer> GetLayersForBiome(int biomeId)
    {
        return layers.Where(l => l.BiomeId == biomeId).ToList();
    }

    public BiomeRuleLayer? GetLayerByGlobalIndex(int index)
    {
        return index >= 0 && index < layers.Count ? layers[index] : null;
    }

    public int GetLayerGlobalIndex(BiomeRuleLayer? layer)
    {
        if (layer == null)
            return -1;

        for (int i = 0; i < layers.Count; i++)
        {
            if (ReferenceEquals(layers[i], layer))
                return i;
        }

        return -1;
    }

    public int GetLayerLocalIndex(BiomeRuleLayer? layer)
    {
        if (layer == null)
            return -1;

        IReadOnlyList<BiomeRuleLayer> sameBiomeLayers = GetLayersForBiome(layer.BiomeId);
        for (int i = 0; i < sameBiomeLayers.Count; i++)
        {
            if (ReferenceEquals(sameBiomeLayers[i], layer))
                return i;
        }

        return -1;
    }

    public void SetLayerAltitude(int layerIndex, float? newMin, float? newMax)
    {
        if (layerIndex < 0 || layerIndex >= layers.Count)
            return;

        BiomeRuleLayer layer = layers[layerIndex];
        BiomeModifier modifier = layer.GetOrCreateLegacyHeightModifier();

        float minValue = newMin ?? modifier.Min;
        float maxValue = newMax ?? modifier.Max;
        modifier.Min = Math.Clamp(MathF.Min(minValue, maxValue), MinHeight, DefaultMaxHeight);
        modifier.Max = Math.Clamp(MathF.Max(minValue, maxValue), modifier.Min, DefaultMaxHeight);
        OnStateChanged();
    }

    public void SetLayerSlope(int layerIndex, float? newMin, float? newMax)
    {
        if (layerIndex < 0 || layerIndex >= layers.Count)
            return;

        BiomeRuleLayer layer = layers[layerIndex];
        BiomeModifier modifier = layer.GetOrCreateLegacySlopeModifier();

        float minValue = newMin ?? modifier.Min;
        float maxValue = newMax ?? modifier.Max;
        modifier.Min = Math.Clamp(MathF.Min(minValue, maxValue), 0.0f, 90.0f);
        modifier.Max = Math.Clamp(MathF.Max(minValue, maxValue), modifier.Min, 90.0f);
        OnStateChanged();
    }

    public void ClearAll()
    {
        biomes.Clear();
        layers.Clear();
        nextLayerId = 1;
        nextModifierId = 1;
    }

    public void AddBiomeFromConfig(int id, string name, Vector4 debugColor)
    {
        BiomeDefinition biome = new()
        {
            Id = id,
            Name = name,
            DebugColor = debugColor,
        };
        biomes.Add(biome);
    }

    public void AddLayerFromConfig(
        int biomeId,
        string name,
        bool enabled,
        float minAltitude,
        float maxAltitude,
        float minSlopeDegrees,
        float maxSlopeDegrees,
        float blendRange,
        int materialSlotIndex)
    {
        BiomeRuleLayer layer = AddLayerCore(biomeId, name);
        layer.Enabled = enabled;
        layer.MaterialSlotIndex = materialSlotIndex;
        layer.MinAltitude = minAltitude;
        layer.MaxAltitude = maxAltitude;
        layer.MinSlopeDegrees = minSlopeDegrees;
        layer.MaxSlopeDegrees = maxSlopeDegrees;
        layer.BlendRange = blendRange;
    }

    public void NormalizeAllRanges()
    {
        foreach (BiomeRuleLayer layer in layers)
        {
            layer.MinAltitude = Math.Clamp(layer.MinAltitude, MinHeight, DefaultMaxHeight);
            layer.MaxAltitude = Math.Clamp(layer.MaxAltitude, layer.MinAltitude, DefaultMaxHeight);
            layer.MinSlopeDegrees = Math.Clamp(layer.MinSlopeDegrees, 0.0f, 90.0f);
            layer.MaxSlopeDegrees = Math.Clamp(layer.MaxSlopeDegrees, layer.MinSlopeDegrees, 90.0f);
            layer.BlendRange = Math.Clamp(layer.BlendRange, 0.0f, 1.0f);
        }

        RecomputePriorities();
    }

    public void NormalizeBiomeRanges(int biomeId)
    {
        foreach (BiomeRuleLayer layer in layers.Where(static l => l.Enabled))
        {
            if (layer.BiomeId != biomeId)
                continue;

            layer.MinAltitude = Math.Clamp(layer.MinAltitude, MinHeight, DefaultMaxHeight);
            layer.MaxAltitude = Math.Clamp(layer.MaxAltitude, layer.MinAltitude, DefaultMaxHeight);
            layer.MinSlopeDegrees = Math.Clamp(layer.MinSlopeDegrees, 0.0f, 90.0f);
            layer.MaxSlopeDegrees = Math.Clamp(layer.MaxSlopeDegrees, layer.MinSlopeDegrees, 90.0f);
        }
    }

    public void NotifyMutated()
    {
        RecomputePriorities();
        OnStateChanged();
    }

    private BiomeDefinition AddBiomeCore(string? explicitName = null)
    {
        int nextId = biomes.Count == 0 ? 0 : biomes[^1].Id + 1;
        BiomeDefinition biome = new()
        {
            Id = nextId,
            Name = explicitName ?? $"Biome {nextId}",
            DebugColor = BuildDebugColor(nextId),
        };
        biomes.Add(biome);
        return biome;
    }

    private BiomeRuleLayer AddLayerCore(int biomeId, string? explicitName = null)
    {
        BiomeRuleLayer layer = new()
        {
            Id = nextLayerId++,
            Name = explicitName ?? $"Layer {layers.Count(l => l.BiomeId == biomeId) + 1}",
            BiomeId = biomeId,
            PriorityOrder = layers.Count,
            MaterialSlotIndex = 0,
        };

        BiomeModifier height = BiomeRuleLayer.CreateDefaultModifier(BiomeModifierType.HeightRange, "Height range");
        height.Id = nextModifierId++;
        height.Min = MinHeight;
        height.Max = DefaultMaxHeight;
        layer.Modifiers.Add(height);

        BiomeModifier slope = BiomeRuleLayer.CreateDefaultModifier(BiomeModifierType.SlopeRange, "Slope range");
        slope.Id = nextModifierId++;
        layer.Modifiers.Add(slope);

        layer.EnsureLegacyModifiers();
        layers.Add(layer);
        return layer;
    }

    private void RecomputePriorities()
    {
        for (int i = 0; i < layers.Count; i++)
            layers[i].PriorityOrder = i;
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Vector4 BuildDebugColor(int index)
    {
        Vector4[] palette =
        {
            new(0.27f, 0.80f, 0.31f, 1.0f),
            new(0.90f, 0.77f, 0.38f, 1.0f),
            new(0.34f, 0.63f, 0.95f, 1.0f),
            new(0.88f, 0.52f, 0.82f, 1.0f),
            new(0.72f, 0.85f, 0.96f, 1.0f),
        };

        return palette[index % palette.Length];
    }
}
