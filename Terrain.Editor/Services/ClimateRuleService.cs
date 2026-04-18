#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Terrain.Editor.Services;

public enum ClimateSeason
{
    All,
    Summer,
    Winter
}

public sealed class ClimateDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Vector4 DebugColor { get; set; } = new(0.3f, 0.8f, 0.3f, 1.0f);
}

public sealed class ClimateRuleLayer
{
    public string Name { get; set; } = "Rule";
    public bool Enabled { get; set; } = true;
    public int ClimateId { get; set; }
    public ClimateSeason Season { get; set; } = ClimateSeason.All;
    public float MinAltitude { get; set; } = 0.0f;
    public float MaxAltitude { get; set; } = 1000.0f;
    public float MaxSlopeDegrees { get; set; } = 45.0f;
    public int MaterialSlotIndex { get; set; }
}

/// <summary>
/// Keeps the new Climate/Rule editor UI state in one place so both the viewport
/// and the side panels can react to the same source of truth.
/// </summary>
public sealed class ClimateRuleService
{
    private static readonly Lazy<ClimateRuleService> InstanceFactory = new(() => new());

    private readonly List<ClimateDefinition> climates = new();
    private readonly List<ClimateRuleLayer> rules = new();

    public static ClimateRuleService Instance => InstanceFactory.Value;

    public IReadOnlyList<ClimateDefinition> Climates => climates;
    public IReadOnlyList<ClimateRuleLayer> Rules => rules;

    public event EventHandler? StateChanged;

    private ClimateRuleService()
    {
        climates.Add(new ClimateDefinition
        {
            Id = 0,
            Name = "Default Climate",
            DebugColor = new Vector4(0.27f, 0.80f, 0.31f, 1.0f)
        });

        rules.Add(new ClimateRuleLayer
        {
            Name = "Default Base",
            ClimateId = 0,
            Season = ClimateSeason.All,
            MinAltitude = 0.0f,
            MaxAltitude = 1000.0f,
            MaxSlopeDegrees = 60.0f,
            MaterialSlotIndex = 0
        });
    }

    public ClimateDefinition AddClimate()
    {
        int nextId = climates.Count == 0 ? 0 : climates[^1].Id + 1;
        var climate = new ClimateDefinition
        {
            Id = nextId,
            Name = $"Climate {nextId}",
            DebugColor = BuildDebugColor(nextId)
        };
        climates.Add(climate);
        OnStateChanged();
        return climate;
    }

    public bool CanRemoveClimate(int climateId)
    {
        foreach (var rule in rules)
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

        int index = climates.FindIndex(climate => climate.Id >= 0 && climate.Id == climateId);
        if (index < 0)
            return false;

        climates.RemoveAt(index);
        OnStateChanged();
        return true;
    }

    public ClimateRuleLayer AddRule()
    {
        var rule = new ClimateRuleLayer
        {
            Name = $"Rule {rules.Count + 1}",
            ClimateId = climates.Count > 0 ? climates[0].Id : 0,
            MaterialSlotIndex = 0
        };
        rules.Add(rule);
        OnStateChanged();
        return rule;
    }

    public void RemoveRuleAt(int index)
    {
        if (index < 0 || index >= rules.Count)
            return;

        rules.RemoveAt(index);
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
        OnStateChanged();
    }

    public ClimateDefinition? FindClimate(int climateId)
    {
        foreach (var climate in climates)
        {
            if (climate.Id == climateId)
                return climate;
        }

        return null;
    }

    public void NotifyMutated()
    {
        OnStateChanged();
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
