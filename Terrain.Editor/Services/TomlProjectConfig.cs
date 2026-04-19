#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Tommy;

namespace Terrain.Editor.Services;

/// <summary>
/// TOML 项目配置数据模型。
/// 所有文件路径相对于 .toml 文件所在目录存储。
/// </summary>
public class TomlProjectConfig
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Untitled";
    public string? HeightmapPath { get; set; }
    public string? ClimateMaskPath { get; set; }
    public float HeightScale { get; set; } = 100.0f;
    public List<TomlMaterialSlotConfig> MaterialSlots { get; set; } = new();
    public List<TomlClimateDefinitionConfig> Climates { get; set; } = new();
    public List<TomlClimateRuleConfig> ClimateRules { get; set; } = new();

    /// <summary>
    /// 从 .toml 文件读取配置。路径自动解析为绝对路径。
    /// </summary>
    public static TomlProjectConfig ReadFrom(string tomlFilePath)
    {
        using var reader = File.OpenText(tomlFilePath);
        var root = TOML.Parse(reader);

        var config = new TomlProjectConfig();
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(tomlFilePath)) ?? "";

        config.Version = root.HasKey("version") && root["version"].IsInteger
            ? (int)root["version"].AsInteger : 1;
        config.Name = root.HasKey("name") && root["name"].IsString
            ? root["name"].AsString.Value : "Untitled";

        if (root.HasKey("terrain") && root["terrain"].IsTable)
        {
            var terrain = root["terrain"];
            config.HeightmapPath = terrain.HasKey("heightmap") && terrain["heightmap"].IsString
                ? ResolvePath(terrain["heightmap"].AsString.Value, baseDir) : null;
            config.ClimateMaskPath = terrain.HasKey("climate_mask") && terrain["climate_mask"].IsString
                ? ResolvePath(terrain["climate_mask"].AsString.Value, baseDir) : null;
            if (terrain.HasKey("height_scale"))
            {
                var hsNode = terrain["height_scale"];
                config.HeightScale = hsNode.IsFloat ? (float)hsNode.AsFloat.Value
                    : hsNode.IsInteger ? (float)hsNode.AsInteger.Value
                    : 100.0f;
            }
        }

        if (root.HasKey("material_slots") && root["material_slots"].IsArray)
        {
            foreach (TomlNode slotNode in root["material_slots"].AsArray)
            {
                if (!slotNode.IsTable)
                    continue;

                int index = slotNode.HasKey("index") && slotNode["index"].IsInteger
                    ? (int)slotNode["index"].AsInteger
                    : -1;

                if (index < 0)
                    continue;

                string name = slotNode.HasKey("name") && slotNode["name"].IsString
                    ? slotNode["name"].AsString.Value
                    : $"Slot {index}";

                var slot = new TomlMaterialSlotConfig
                {
                    Index = index,
                    Name = name,
                    AlbedoPath = slotNode.HasKey("albedo") && slotNode["albedo"].IsString
                        ? ResolvePath(slotNode["albedo"].AsString.Value, baseDir)
                        : null,
                    NormalPath = slotNode.HasKey("normal") && slotNode["normal"].IsString
                        ? ResolvePath(slotNode["normal"].AsString.Value, baseDir)
                        : null,
                };
                config.MaterialSlots.Add(slot);
            }
        }

        if (root.HasKey("climates") && root["climates"].IsArray)
        {
            foreach (TomlNode climateNode in root["climates"].AsArray)
            {
                if (!climateNode.IsTable)
                    continue;

                int id = climateNode.HasKey("id") && climateNode["id"].IsInteger
                    ? (int)climateNode["id"].AsInteger : 0;
                string name = climateNode.HasKey("name") && climateNode["name"].IsString
                    ? climateNode["name"].AsString.Value : "";
                float r = climateNode.HasKey("debug_color_r") && climateNode["debug_color_r"].IsFloat
                    ? (float)climateNode["debug_color_r"].AsFloat.Value
                    : climateNode.HasKey("debug_color_r") && climateNode["debug_color_r"].IsInteger
                    ? (float)climateNode["debug_color_r"].AsInteger.Value : 0.3f;
                float g = climateNode.HasKey("debug_color_g") && climateNode["debug_color_g"].IsFloat
                    ? (float)climateNode["debug_color_g"].AsFloat.Value
                    : climateNode.HasKey("debug_color_g") && climateNode["debug_color_g"].IsInteger
                    ? (float)climateNode["debug_color_g"].AsInteger.Value : 0.8f;
                float b = climateNode.HasKey("debug_color_b") && climateNode["debug_color_b"].IsFloat
                    ? (float)climateNode["debug_color_b"].AsFloat.Value
                    : climateNode.HasKey("debug_color_b") && climateNode["debug_color_b"].IsInteger
                    ? (float)climateNode["debug_color_b"].AsInteger.Value : 0.3f;
                float a = climateNode.HasKey("debug_color_a") && climateNode["debug_color_a"].IsFloat
                    ? (float)climateNode["debug_color_a"].AsFloat.Value
                    : climateNode.HasKey("debug_color_a") && climateNode["debug_color_a"].IsInteger
                    ? (float)climateNode["debug_color_a"].AsInteger.Value : 1.0f;

                config.Climates.Add(new TomlClimateDefinitionConfig
                {
                    Id = id, Name = name,
                    DebugColorR = r, DebugColorG = g, DebugColorB = b, DebugColorA = a
                });
            }
        }

        if (root.HasKey("climate_rules") && root["climate_rules"].IsArray)
        {
            foreach (TomlNode ruleNode in root["climate_rules"].AsArray)
            {
                if (!ruleNode.IsTable)
                    continue;

                int climateId = ruleNode.HasKey("climate_id") && ruleNode["climate_id"].IsInteger
                    ? (int)ruleNode["climate_id"].AsInteger : 0;
                string ruleName = ruleNode.HasKey("name") && ruleNode["name"].IsString
                    ? ruleNode["name"].AsString.Value : "Rule";
                bool enabled = ruleNode.HasKey("enabled") && ruleNode["enabled"].IsBoolean
                    ? ruleNode["enabled"].AsBoolean.Value : true;
                float minAlt = ruleNode.HasKey("min_altitude") && ruleNode["min_altitude"].IsFloat
                    ? (float)ruleNode["min_altitude"].AsFloat.Value
                    : ruleNode.HasKey("min_altitude") && ruleNode["min_altitude"].IsInteger
                    ? (float)ruleNode["min_altitude"].AsInteger.Value : 0.0f;
                float maxAlt = ruleNode.HasKey("max_altitude") && ruleNode["max_altitude"].IsFloat
                    ? (float)ruleNode["max_altitude"].AsFloat.Value
                    : ruleNode.HasKey("max_altitude") && ruleNode["max_altitude"].IsInteger
                    ? (float)ruleNode["max_altitude"].AsInteger.Value : 1000.0f;
                float minSlope = ruleNode.HasKey("min_slope") && ruleNode["min_slope"].IsFloat
                    ? (float)ruleNode["min_slope"].AsFloat.Value
                    : ruleNode.HasKey("min_slope") && ruleNode["min_slope"].IsInteger
                    ? (float)ruleNode["min_slope"].AsInteger.Value : 0.0f;
                float maxSlope = ruleNode.HasKey("max_slope") && ruleNode["max_slope"].IsFloat
                    ? (float)ruleNode["max_slope"].AsFloat.Value
                    : ruleNode.HasKey("max_slope") && ruleNode["max_slope"].IsInteger
                    ? (float)ruleNode["max_slope"].AsInteger.Value : 45.0f;
                float blend = ruleNode.HasKey("blend_range") && ruleNode["blend_range"].IsFloat
                    ? (float)ruleNode["blend_range"].AsFloat.Value
                    : ruleNode.HasKey("blend_range") && ruleNode["blend_range"].IsInteger
                    ? (float)ruleNode["blend_range"].AsInteger.Value : 0.0f;
                int matSlot = ruleNode.HasKey("material_slot") && ruleNode["material_slot"].IsInteger
                    ? (int)ruleNode["material_slot"].AsInteger : 0;

                config.ClimateRules.Add(new TomlClimateRuleConfig
                {
                    ClimateId = climateId, Name = ruleName, Enabled = enabled,
                    MinAltitude = minAlt, MaxAltitude = maxAlt,
                    MinSlopeDegrees = minSlope, MaxSlopeDegrees = maxSlope,
                    BlendRange = blend, MaterialSlotIndex = matSlot
                });
            }
        }

        return config;
    }

    /// <summary>
    /// 将配置写入 .toml 文件。绝对路径自动转换为相对路径。
    /// </summary>
    public void WriteTo(string tomlFilePath)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(tomlFilePath)) ?? "";

        var root = new TomlTable();
        root["version"] = Version;
        root["name"] = Name;

        var terrain = new TomlTable();
        if (!string.IsNullOrEmpty(HeightmapPath))
            terrain["heightmap"] = MakeRelative(HeightmapPath, baseDir);
        if (!string.IsNullOrEmpty(ClimateMaskPath))
            terrain["climate_mask"] = MakeRelative(ClimateMaskPath, baseDir);
        terrain["height_scale"] = HeightScale;
        root["terrain"] = terrain;

        if (MaterialSlots.Count > 0)
        {
            var slotsArray = new TomlArray();
            foreach (var slot in MaterialSlots)
            {
                var slotTable = new TomlTable();
                slotTable["index"] = slot.Index;
                slotTable["name"] = slot.Name;
                if (!string.IsNullOrEmpty(slot.AlbedoPath))
                    slotTable["albedo"] = MakeRelative(slot.AlbedoPath, baseDir);
                if (!string.IsNullOrEmpty(slot.NormalPath))
                    slotTable["normal"] = MakeRelative(slot.NormalPath, baseDir);
                slotsArray.Add(slotTable);
            }
            root["material_slots"] = slotsArray;
        }

        if (Climates.Count > 0)
        {
            var climatesArray = new TomlArray();
            foreach (var climate in Climates)
            {
                var climateTable = new TomlTable();
                climateTable["id"] = climate.Id;
                climateTable["name"] = climate.Name;
                climateTable["debug_color_r"] = climate.DebugColorR;
                climateTable["debug_color_g"] = climate.DebugColorG;
                climateTable["debug_color_b"] = climate.DebugColorB;
                climateTable["debug_color_a"] = climate.DebugColorA;
                climatesArray.Add(climateTable);
            }
            root["climates"] = climatesArray;
        }

        if (ClimateRules.Count > 0)
        {
            var rulesArray = new TomlArray();
            foreach (var rule in ClimateRules)
            {
                var ruleTable = new TomlTable();
                ruleTable["climate_id"] = rule.ClimateId;
                ruleTable["name"] = rule.Name;
                ruleTable["enabled"] = rule.Enabled;
                ruleTable["min_altitude"] = rule.MinAltitude;
                ruleTable["max_altitude"] = rule.MaxAltitude;
                ruleTable["min_slope"] = rule.MinSlopeDegrees;
                ruleTable["max_slope"] = rule.MaxSlopeDegrees;
                ruleTable["blend_range"] = rule.BlendRange;
                ruleTable["material_slot"] = rule.MaterialSlotIndex;
                rulesArray.Add(ruleTable);
            }
            root["climate_rules"] = rulesArray;
        }

        // 确保目录存在

        using var writer = File.CreateText(tomlFilePath);
        root.WriteTo(writer);
    }

    /// <summary>
    /// 将绝对路径转换为相对于 baseDir 的路径，使用 / 分隔符。
    /// </summary>
    internal static string MakeRelative(string absPath, string baseDir)
    {
        if (string.IsNullOrEmpty(absPath) || string.IsNullOrEmpty(baseDir))
            return absPath ?? "";

        try
        {
            string relative = Path.GetRelativePath(baseDir, absPath);
            return relative.Replace('\\', '/');
        }
        catch (Exception)
        {
            return absPath;
        }
    }

    /// <summary>
    /// 将相对路径解析为绝对路径。如果已经是绝对路径则直接返回。
    /// </summary>
    internal static string? ResolvePath(string? relativeOrAbsolute, string baseDir)
    {
        if (string.IsNullOrEmpty(relativeOrAbsolute))
            return null;

        if (Path.IsPathRooted(relativeOrAbsolute))
            return relativeOrAbsolute;

        return Path.GetFullPath(Path.Combine(baseDir, relativeOrAbsolute.Replace('/', Path.DirectorySeparatorChar)));
    }
}

/// <summary>
/// 材质槽位配置。
/// </summary>
public class TomlMaterialSlotConfig
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string? AlbedoPath { get; set; }
    public string? NormalPath { get; set; }
}

public class TomlClimateDefinitionConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public float DebugColorR { get; set; }
    public float DebugColorG { get; set; }
    public float DebugColorB { get; set; }
    public float DebugColorA { get; set; } = 1.0f;
}

public class TomlClimateRuleConfig
{
    public int ClimateId { get; set; }
    public string Name { get; set; } = "Rule";
    public bool Enabled { get; set; } = true;
    public float MinAltitude { get; set; }
    public float MaxAltitude { get; set; } = 1000.0f;
    public float MinSlopeDegrees { get; set; }
    public float MaxSlopeDegrees { get; set; } = 45.0f;
    public float BlendRange { get; set; }
    public int MaterialSlotIndex { get; set; }
}
