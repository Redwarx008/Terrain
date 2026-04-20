#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Terrain.Editor.Services;

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
    public float MinAltitude { get; set; } = 0.0f;
    public float MaxAltitude { get; set; } = 1000.0f;
    public float MinSlopeDegrees { get; set; } = 0.0f;
    public float MaxSlopeDegrees { get; set; } = 45.0f;
    public float BlendRange { get; set; } = 0.0f;
    public int MaterialSlotIndex { get; set; }
}

/// <summary>
/// Keeps the new Climate/Rule editor UI state in one place so both the viewport
/// and the side panels can react to the same source of truth.
/// </summary>
public sealed class ClimateRuleService
{
    public const float MinHeight = 0.0f;
    public const float DefaultMaxHeight = 1000.0f;

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
            MinAltitude = MinHeight,
            MaxAltitude = DefaultMaxHeight,
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
        return AddRule(climates.Count > 0 ? climates[0].Id : 0);
    }

    public ClimateRuleLayer AddRule(int climateId)
    {
        var existingRules = GetRulesForClimate(climateId);

        if (existingRules.Count == 0)
        {
            var firstRule = new ClimateRuleLayer
            {
                Name = "Rule 1",
                ClimateId = climateId,
                MinAltitude = MinHeight,
                MaxAltitude = DefaultMaxHeight,
                MaterialSlotIndex = 0
            };
            rules.Add(firstRule);
            NormalizeClimateRanges(climateId);
            OnStateChanged();
            return firstRule;
        }

        var rule = new ClimateRuleLayer
        {
            Name = $"Rule {existingRules.Count + 1}",
            ClimateId = climateId,
            MinAltitude = MinHeight,
            MaxAltitude = DefaultMaxHeight,
            MaterialSlotIndex = 0
        };

        rules.Add(rule);
        NormalizeClimateRanges(climateId);
        OnStateChanged();
        return rule;
    }

    public void RemoveRuleAt(int index)
    {
        if (index < 0 || index >= rules.Count)
            return;

        ClimateRuleLayer removedRule = rules[index];
        rules.RemoveAt(index);
        NormalizeClimateRanges(removedRule.ClimateId);
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
        NormalizeClimateRanges(rule.ClimateId);
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

    public IReadOnlyList<ClimateRuleLayer> GetRulesForClimate(int climateId)
    {
        return rules
            .Where(r => r.ClimateId == climateId)
            .ToList();
    }

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

        var sameClimateRules = GetRulesForClimate(rule.ClimateId);
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

        var rule = rules[ruleIndex];
        var sameClimateRules = GetRulesForClimate(rule.ClimateId);
        int localRuleIndex = GetLocalRuleIndex(sameClimateRules, rule);

        if (localRuleIndex < 0)
            return;

        ClimateRuleLayer? previousRule = localRuleIndex > 0 ? sameClimateRules[localRuleIndex - 1] : null;
        ClimateRuleLayer? nextRule = localRuleIndex < sameClimateRules.Count - 1 ? sameClimateRules[localRuleIndex + 1] : null;

        if (newMin.HasValue)
        {
            float upperBound = newMax ?? rule.MaxAltitude;
            float clampedMin = Math.Clamp(newMin.Value, MinHeight, upperBound);
            rule.MinAltitude = clampedMin;

            if (previousRule != null)
                previousRule.MaxAltitude = clampedMin;
        }

        if (newMax.HasValue)
        {
            float lowerBound = newMin ?? rule.MinAltitude;
            float clampedMax = MathF.Max(newMax.Value, lowerBound);
            rule.MaxAltitude = clampedMax;

            if (nextRule != null)
                nextRule.MinAltitude = clampedMax;
        }

        NormalizeClimateRanges(rule.ClimateId);
        OnStateChanged();
    }

    public void SetRuleSlope(int ruleIndex, float? newMin, float? newMax)
    {
        if (ruleIndex < 0 || ruleIndex >= rules.Count)
            return;

        var rule = rules[ruleIndex];
        var sameClimateRules = GetRulesForClimate(rule.ClimateId);
        int localRuleIndex = GetLocalRuleIndex(sameClimateRules, rule);

        if (localRuleIndex < 0)
            return;

        ClimateRuleLayer? previousRule = localRuleIndex > 0 ? sameClimateRules[localRuleIndex - 1] : null;
        ClimateRuleLayer? nextRule = localRuleIndex < sameClimateRules.Count - 1 ? sameClimateRules[localRuleIndex + 1] : null;

        if (newMin.HasValue)
        {
            float upperBound = newMax ?? rule.MaxSlopeDegrees;
            float clampedMin = Math.Clamp(newMin.Value, 0.0f, upperBound);
            rule.MinSlopeDegrees = clampedMin;

            if (previousRule != null)
                previousRule.MaxSlopeDegrees = clampedMin;
        }

        if (newMax.HasValue)
        {
            float lowerBound = newMin ?? rule.MinSlopeDegrees;
            float clampedMax = Math.Clamp(newMax.Value, lowerBound, 90.0f);
            rule.MaxSlopeDegrees = clampedMax;

            if (nextRule != null)
                nextRule.MinSlopeDegrees = clampedMax;
        }

        NormalizeClimateRanges(rule.ClimateId);
        OnStateChanged();
    }

    public void ClearAll()
    {
        climates.Clear();
        rules.Clear();
    }

    public void AddClimateFromConfig(int id, string name, Vector4 debugColor)
    {
        var climate = new ClimateDefinition
        {
            Id = id,
            Name = name,
            DebugColor = debugColor
        };
        climates.Add(climate);
    }

    public void AddRuleFromConfig(int climateId, string name, bool enabled,
        float minAltitude, float maxAltitude, float minSlopeDegrees, float maxSlopeDegrees,
        float blendRange, int materialSlotIndex)
    {
        var rule = new ClimateRuleLayer
        {
            ClimateId = climateId,
            Name = name,
            Enabled = enabled,
            MinAltitude = minAltitude,
            MaxAltitude = maxAltitude,
            MinSlopeDegrees = minSlopeDegrees,
            MaxSlopeDegrees = maxSlopeDegrees,
            BlendRange = blendRange,
            MaterialSlotIndex = materialSlotIndex
        };
        rules.Add(rule);
    }

    public void NormalizeAllRanges()
    {
        foreach (int climateId in climates.Select(static climate => climate.Id))
        {
            NormalizeClimateRanges(climateId);
        }
    }

    public void NormalizeClimateRanges(int climateId)
    {
        var sameClimateRules = GetRulesForClimate(climateId).ToList();
        if (sameClimateRules.Count == 0)
            return;

        for (int i = 0; i < sameClimateRules.Count; i++)
        {
            ClimateRuleLayer climateRule = sameClimateRules[i];

            climateRule.MinAltitude = MathF.Max(climateRule.MinAltitude, MinHeight);
            climateRule.MaxAltitude = MathF.Max(climateRule.MaxAltitude, climateRule.MinAltitude);
            climateRule.MinSlopeDegrees = Math.Clamp(climateRule.MinSlopeDegrees, 0.0f, 90.0f);
            climateRule.MaxSlopeDegrees = Math.Clamp(climateRule.MaxSlopeDegrees, climateRule.MinSlopeDegrees, 90.0f);
            climateRule.BlendRange = Math.Clamp(climateRule.BlendRange, 0.0f, 1.0f);

            if (i == 0)
                continue;

            ClimateRuleLayer previousRule = sameClimateRules[i - 1];
            climateRule.MinAltitude = previousRule.MaxAltitude;
            climateRule.MaxAltitude = MathF.Max(climateRule.MaxAltitude, climateRule.MinAltitude);
            climateRule.MinSlopeDegrees = previousRule.MaxSlopeDegrees;
            climateRule.MaxSlopeDegrees = Math.Clamp(climateRule.MaxSlopeDegrees, climateRule.MinSlopeDegrees, 90.0f);
        }
    }

    public void NotifyMutated()
    {
        OnStateChanged();
    }

    private int GetLocalRuleIndex(IReadOnlyList<ClimateRuleLayer> sameClimateRules, ClimateRuleLayer rule)
    {
        for (int i = 0; i < sameClimateRules.Count; i++)
        {
            if (ReferenceEquals(sameClimateRules[i], rule))
                return i;
        }

        return -1;
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
