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

public sealed class ClimateDefinition
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

public sealed class ClimateRuleLayer
{
    private BiomeModifier? legacyHeightModifier;
    private BiomeModifier? legacySlopeModifier;

    public int Id { get; set; }
    public string Name { get; set; } = "Layer";
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public int ClimateId { get; set; }
    public int MaterialSlotIndex { get; set; }
    public int PriorityOrder { get; set; }
    public List<BiomeModifier> Modifiers { get; } = new();

    // Compatibility with the previous rule-based UI/persistence.
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
                Min = ClimateRuleService.MinHeight,
                Max = ClimateRuleService.DefaultMaxHeight,
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
                Min = -1.0f,
                Max = 1.0f,
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
/// Historical "Climate" naming is retained in the type for compatibility, but the
/// workflow semantics now map to Biome -> Layer -> Modifier Stack.
/// </summary>
public sealed class ClimateRuleService
{
    public const float MinHeight = 0.0f;
    public const float DefaultMaxHeight = 1000.0f;

    private static readonly Lazy<ClimateRuleService> InstanceFactory = new(() => new());

    private readonly List<ClimateDefinition> climates = new();
    private readonly List<ClimateRuleLayer> rules = new();
    private int nextLayerId = 1;
    private int nextModifierId = 1;

    public static ClimateRuleService Instance => InstanceFactory.Value;

    public IReadOnlyList<ClimateDefinition> Climates => climates;
    public IReadOnlyList<ClimateDefinition> Biomes => climates;
    public IReadOnlyList<ClimateRuleLayer> Rules => rules;
    public IReadOnlyList<ClimateRuleLayer> Layers => rules;

    public event EventHandler? StateChanged;

    private ClimateRuleService()
    {
        ClimateDefinition biome = AddClimateCore("Default Biome");
        ClimateRuleLayer layer = AddRuleCore(biome.Id, "Default Base");
        layer.MaterialSlotIndex = 0;
        layer.MaxSlopeDegrees = 60.0f;
    }

    public ClimateDefinition AddClimate()
    {
        ClimateDefinition climate = AddClimateCore();
        OnStateChanged();
        return climate;
    }

    public ClimateDefinition AddBiome() => AddClimate();

    public bool CanRemoveClimate(int climateId)
    {
        foreach (ClimateRuleLayer rule in rules)
        {
            if (rule.ClimateId == climateId)
                return false;
        }

        return climates.Count > 1;
    }

    public bool RemoveClimate(int climateId)
    {
        if (!CanRemoveClimate(climateId))
            return false;

        int index = climates.FindIndex(climate => climate.Id == climateId);
        if (index < 0)
            return false;

        climates.RemoveAt(index);
        OnStateChanged();
        return true;
    }

    public ClimateRuleLayer AddRule()
    {
        return AddRule(climates.Count > 0 ? climates[0].Id : 0);
    }

    public ClimateRuleLayer AddRule(int climateId)
    {
        ClimateRuleLayer layer = AddRuleCore(climateId);
        OnStateChanged();
        return layer;
    }

    public ClimateRuleLayer AddLayer(int biomeId) => AddRule(biomeId);

    public void RemoveRuleAt(int index)
    {
        if (index < 0 || index >= rules.Count)
            return;

        rules.RemoveAt(index);
        RecomputePriorities();
        OnStateChanged();
    }

    public void MoveRule(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= rules.Count)
            return;
        if (toIndex < 0 || toIndex >= rules.Count)
            return;
        if (fromIndex == toIndex)
            return;

        ClimateRuleLayer rule = rules[fromIndex];
        rules.RemoveAt(fromIndex);
        rules.Insert(toIndex, rule);
        RecomputePriorities();
        OnStateChanged();
    }

    public BiomeModifier AddModifier(ClimateRuleLayer layer, BiomeModifierType type)
    {
        ArgumentNullException.ThrowIfNull(layer);

        BiomeModifier modifier = ClimateRuleLayer.CreateDefaultModifier(type);
        modifier.Id = nextModifierId++;
        layer.Modifiers.Add(modifier);
        layer.EnsureLegacyModifiers();
        OnStateChanged();
        return modifier;
    }

    public void RemoveModifier(ClimateRuleLayer layer, int modifierIndex)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if ((uint)modifierIndex >= (uint)layer.Modifiers.Count)
            return;

        layer.Modifiers.RemoveAt(modifierIndex);
        layer.EnsureLegacyModifiers();
        OnStateChanged();
    }

    public void MoveModifier(ClimateRuleLayer layer, int fromIndex, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if ((uint)fromIndex >= (uint)layer.Modifiers.Count || (uint)toIndex >= (uint)layer.Modifiers.Count || fromIndex == toIndex)
            return;

        BiomeModifier modifier = layer.Modifiers[fromIndex];
        layer.Modifiers.RemoveAt(fromIndex);
        layer.Modifiers.Insert(toIndex, modifier);
        OnStateChanged();
    }

    public ClimateDefinition? FindClimate(int climateId)
    {
        foreach (ClimateDefinition climate in climates)
        {
            if (climate.Id == climateId)
                return climate;
        }

        return null;
    }

    public ClimateDefinition? FindBiome(int biomeId) => FindClimate(biomeId);

    public IReadOnlyList<ClimateRuleLayer> GetRulesForClimate(int climateId)
    {
        return rules.Where(r => r.ClimateId == climateId).ToList();
    }

    public IReadOnlyList<ClimateRuleLayer> GetLayersForBiome(int biomeId) => GetRulesForClimate(biomeId);

    public ClimateRuleLayer? GetRuleByGlobalIndex(int index)
    {
        return index >= 0 && index < rules.Count ? rules[index] : null;
    }

    public int GetRuleGlobalIndex(ClimateRuleLayer? rule)
    {
        if (rule == null)
            return -1;

        for (int i = 0; i < rules.Count; i++)
        {
            if (ReferenceEquals(rules[i], rule))
                return i;
        }

        return -1;
    }

    public int GetRuleLocalIndex(ClimateRuleLayer? rule)
    {
        if (rule == null)
            return -1;

        IReadOnlyList<ClimateRuleLayer> sameClimateRules = GetRulesForClimate(rule.ClimateId);
        for (int i = 0; i < sameClimateRules.Count; i++)
        {
            if (ReferenceEquals(sameClimateRules[i], rule))
                return i;
        }

        return -1;
    }

    public void SetRuleAltitude(int ruleIndex, float? newMin, float? newMax)
    {
        if (ruleIndex < 0 || ruleIndex >= rules.Count)
            return;

        ClimateRuleLayer rule = rules[ruleIndex];
        BiomeModifier modifier = rule.GetOrCreateLegacyHeightModifier();

        float minValue = newMin ?? modifier.Min;
        float maxValue = newMax ?? modifier.Max;
        modifier.Min = Math.Clamp(MathF.Min(minValue, maxValue), MinHeight, DefaultMaxHeight);
        modifier.Max = Math.Clamp(MathF.Max(minValue, maxValue), modifier.Min, DefaultMaxHeight);
        OnStateChanged();
    }

    public void SetRuleSlope(int ruleIndex, float? newMin, float? newMax)
    {
        if (ruleIndex < 0 || ruleIndex >= rules.Count)
            return;

        ClimateRuleLayer rule = rules[ruleIndex];
        BiomeModifier modifier = rule.GetOrCreateLegacySlopeModifier();

        float minValue = newMin ?? modifier.Min;
        float maxValue = newMax ?? modifier.Max;
        modifier.Min = Math.Clamp(MathF.Min(minValue, maxValue), 0.0f, 90.0f);
        modifier.Max = Math.Clamp(MathF.Max(minValue, maxValue), modifier.Min, 90.0f);
        OnStateChanged();
    }

    public void ClearAll()
    {
        climates.Clear();
        rules.Clear();
        nextLayerId = 1;
        nextModifierId = 1;
    }

    public void AddClimateFromConfig(int id, string name, Vector4 debugColor)
    {
        ClimateDefinition climate = new()
        {
            Id = id,
            Name = name,
            DebugColor = debugColor,
        };
        climates.Add(climate);
    }

    public void AddRuleFromConfig(
        int climateId,
        string name,
        bool enabled,
        float minAltitude,
        float maxAltitude,
        float minSlopeDegrees,
        float maxSlopeDegrees,
        float blendRange,
        int materialSlotIndex)
    {
        ClimateRuleLayer layer = AddRuleCore(climateId, name);
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
        foreach (ClimateRuleLayer rule in rules)
        {
            rule.MinAltitude = Math.Clamp(rule.MinAltitude, MinHeight, DefaultMaxHeight);
            rule.MaxAltitude = Math.Clamp(rule.MaxAltitude, rule.MinAltitude, DefaultMaxHeight);
            rule.MinSlopeDegrees = Math.Clamp(rule.MinSlopeDegrees, 0.0f, 90.0f);
            rule.MaxSlopeDegrees = Math.Clamp(rule.MaxSlopeDegrees, rule.MinSlopeDegrees, 90.0f);
            rule.BlendRange = Math.Clamp(rule.BlendRange, 0.0f, 1.0f);
        }

        RecomputePriorities();
    }

    public void NormalizeClimateRanges(int climateId)
    {
        foreach (ClimateRuleLayer rule in rules.Where(static rule => rule.Enabled))
        {
            if (rule.ClimateId != climateId)
                continue;

            rule.MinAltitude = Math.Clamp(rule.MinAltitude, MinHeight, DefaultMaxHeight);
            rule.MaxAltitude = Math.Clamp(rule.MaxAltitude, rule.MinAltitude, DefaultMaxHeight);
            rule.MinSlopeDegrees = Math.Clamp(rule.MinSlopeDegrees, 0.0f, 90.0f);
            rule.MaxSlopeDegrees = Math.Clamp(rule.MaxSlopeDegrees, rule.MinSlopeDegrees, 90.0f);
        }
    }

    public void NotifyMutated()
    {
        RecomputePriorities();
        OnStateChanged();
    }

    private ClimateDefinition AddClimateCore(string? explicitName = null)
    {
        int nextId = climates.Count == 0 ? 0 : climates[^1].Id + 1;
        ClimateDefinition climate = new()
        {
            Id = nextId,
            Name = explicitName ?? $"Biome {nextId}",
            DebugColor = BuildDebugColor(nextId),
        };
        climates.Add(climate);
        return climate;
    }

    private ClimateRuleLayer AddRuleCore(int climateId, string? explicitName = null)
    {
        ClimateRuleLayer layer = new()
        {
            Id = nextLayerId++,
            Name = explicitName ?? $"Layer {rules.Count(r => r.ClimateId == climateId) + 1}",
            ClimateId = climateId,
            PriorityOrder = rules.Count,
            MaterialSlotIndex = 0,
        };

        BiomeModifier height = ClimateRuleLayer.CreateDefaultModifier(BiomeModifierType.HeightRange, "Height range");
        height.Id = nextModifierId++;
        height.Min = MinHeight;
        height.Max = DefaultMaxHeight;
        layer.Modifiers.Add(height);

        BiomeModifier slope = ClimateRuleLayer.CreateDefaultModifier(BiomeModifierType.SlopeRange, "Slope range");
        slope.Id = nextModifierId++;
        layer.Modifiers.Add(slope);

        layer.EnsureLegacyModifiers();
        rules.Add(layer);
        return layer;
    }

    private void RecomputePriorities()
    {
        for (int i = 0; i < rules.Count; i++)
            rules[i].PriorityOrder = i;
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
