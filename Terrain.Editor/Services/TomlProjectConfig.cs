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
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "Untitled";
    public string? HeightmapPath { get; set; }
    public string? BiomeMaskPath { get; set; }
    public string? RiverMapImagePath { get; set; }
    public float HeightScale { get; set; } = 100.0f;
    public List<TomlMaterialSlotConfig> MaterialSlots { get; set; } = new();
    public List<TomlBiomeDefinitionConfig> Biomes { get; set; } = new();
    public List<TomlBiomeLayerConfig> BiomeLayers { get; set; } = new();
    public List<TomlBiomeModifierConfig> BiomeModifiers { get; set; } = new();
    public List<TomlPathNodeConfig> PathNodes { get; set; } = new();
    public List<TomlPathFeatureConfig> PathFeatures { get; set; } = new();

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
            config.BiomeMaskPath = terrain.HasKey("biome_mask") && terrain["biome_mask"].IsString
                ? ResolvePath(terrain["biome_mask"].AsString.Value, baseDir) : null;
            config.RiverMapImagePath = terrain.HasKey("river_map") && terrain["river_map"].IsString
                ? ResolvePath(terrain["river_map"].AsString.Value, baseDir) : null;
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
                    PropertiesPath = slotNode.HasKey("properties") && slotNode["properties"].IsString
                        ? ResolvePath(slotNode["properties"].AsString.Value, baseDir)
                        : null,
                };
                config.MaterialSlots.Add(slot);
            }
        }

        if (root.HasKey("biomes") && root["biomes"].IsArray)
        {
            foreach (TomlNode biomeNode in root["biomes"].AsArray)
            {
                if (!biomeNode.IsTable)
                    continue;

                int id = biomeNode.HasKey("id") && biomeNode["id"].IsInteger
                    ? (int)biomeNode["id"].AsInteger : 0;
                string name = biomeNode.HasKey("name") && biomeNode["name"].IsString
                    ? biomeNode["name"].AsString.Value : "";
                float r = biomeNode.HasKey("debug_color_r") && biomeNode["debug_color_r"].IsFloat
                    ? (float)biomeNode["debug_color_r"].AsFloat.Value
                    : biomeNode.HasKey("debug_color_r") && biomeNode["debug_color_r"].IsInteger
                    ? (float)biomeNode["debug_color_r"].AsInteger.Value : 0.3f;
                float g = biomeNode.HasKey("debug_color_g") && biomeNode["debug_color_g"].IsFloat
                    ? (float)biomeNode["debug_color_g"].AsFloat.Value
                    : biomeNode.HasKey("debug_color_g") && biomeNode["debug_color_g"].IsInteger
                    ? (float)biomeNode["debug_color_g"].AsInteger.Value : 0.8f;
                float b = biomeNode.HasKey("debug_color_b") && biomeNode["debug_color_b"].IsFloat
                    ? (float)biomeNode["debug_color_b"].AsFloat.Value
                    : biomeNode.HasKey("debug_color_b") && biomeNode["debug_color_b"].IsInteger
                    ? (float)biomeNode["debug_color_b"].AsInteger.Value : 0.3f;
                float a = biomeNode.HasKey("debug_color_a") && biomeNode["debug_color_a"].IsFloat
                    ? (float)biomeNode["debug_color_a"].AsFloat.Value
                    : biomeNode.HasKey("debug_color_a") && biomeNode["debug_color_a"].IsInteger
                    ? (float)biomeNode["debug_color_a"].AsInteger.Value : 1.0f;

                config.Biomes.Add(new TomlBiomeDefinitionConfig
                {
                    Id = id,
                    Name = name,
                    DebugColorR = r,
                    DebugColorG = g,
                    DebugColorB = b,
                    DebugColorA = a
                });
            }
        }

        if (root.HasKey("biome_layers") && root["biome_layers"].IsArray)
        {
            foreach (TomlNode layerNode in root["biome_layers"].AsArray)
            {
                if (!layerNode.IsTable)
                    continue;

                config.BiomeLayers.Add(new TomlBiomeLayerConfig
                {
                    Id = layerNode.HasKey("id") && layerNode["id"].IsInteger ? (int)layerNode["id"].AsInteger : 0,
                    BiomeId = layerNode.HasKey("biome_id") && layerNode["biome_id"].IsInteger ? (int)layerNode["biome_id"].AsInteger : 0,
                    Name = layerNode.HasKey("name") && layerNode["name"].IsString ? layerNode["name"].AsString.Value : "Layer",
                    Enabled = layerNode.HasKey("enabled") && layerNode["enabled"].IsBoolean ? layerNode["enabled"].AsBoolean.Value : true,
                    Visible = layerNode.HasKey("visible") && layerNode["visible"].IsBoolean ? layerNode["visible"].AsBoolean.Value : true,
                    MaterialSlotIndex = layerNode.HasKey("material_slot") && layerNode["material_slot"].IsInteger ? (int)layerNode["material_slot"].AsInteger : 0,
                    PriorityOrder = layerNode.HasKey("priority") && layerNode["priority"].IsInteger ? (int)layerNode["priority"].AsInteger : 0,
                });
            }
        }

        if (root.HasKey("biome_modifiers") && root["biome_modifiers"].IsArray)
        {
            foreach (TomlNode modifierNode in root["biome_modifiers"].AsArray)
            {
                if (!modifierNode.IsTable)
                    continue;

                config.BiomeModifiers.Add(new TomlBiomeModifierConfig
                {
                    Id = modifierNode.HasKey("id") && modifierNode["id"].IsInteger ? (int)modifierNode["id"].AsInteger : 0,
                    LayerId = modifierNode.HasKey("layer_id") && modifierNode["layer_id"].IsInteger ? (int)modifierNode["layer_id"].AsInteger : 0,
                    Name = modifierNode.HasKey("name") && modifierNode["name"].IsString ? modifierNode["name"].AsString.Value : "Modifier",
                    Type = modifierNode.HasKey("type") && modifierNode["type"].IsString ? modifierNode["type"].AsString.Value : "HeightRange",
                    BlendMode = modifierNode.HasKey("blend_mode") && modifierNode["blend_mode"].IsString ? modifierNode["blend_mode"].AsString.Value : "Multiply",
                    Enabled = modifierNode.HasKey("enabled") && modifierNode["enabled"].IsBoolean ? modifierNode["enabled"].AsBoolean.Value : true,
                    Visible = modifierNode.HasKey("visible") && modifierNode["visible"].IsBoolean ? modifierNode["visible"].AsBoolean.Value : true,
                    Opacity = ReadFloat(modifierNode, "opacity", 1.0f),
                    Min = ReadFloat(modifierNode, "min", 0.0f),
                    Max = ReadFloat(modifierNode, "max", 1.0f),
                    MinFalloff = ReadFloat(modifierNode, "min_falloff", 0.001f),
                    MaxFalloff = ReadFloat(modifierNode, "max_falloff", 0.001f),
                    Radius = ReadFloat(modifierNode, "radius", 1.0f),
                    AngleDegrees = ReadFloat(modifierNode, "angle_degrees", 0.0f),
                    AngleRangeDegrees = ReadFloat(modifierNode, "angle_range_degrees", 180.0f),
                    Scale = ReadFloat(modifierNode, "scale", 1.0f),
                    OffsetX = ReadFloat(modifierNode, "offset_x", 0.0f),
                    OffsetY = ReadFloat(modifierNode, "offset_y", 0.0f),
                    Seed = ReadFloat(modifierNode, "seed", 0.0f),
                    Octaves = ReadFloat(modifierNode, "octaves", 4.0f),
                    Invert = ReadFloat(modifierNode, "invert", 0.0f),
                    TextureMaskPath = modifierNode.HasKey("texture_mask") && modifierNode["texture_mask"].IsString
                        ? ResolvePath(modifierNode["texture_mask"].AsString.Value, baseDir)
                        : null,
                    TextureMaskChannel = modifierNode.HasKey("texture_mask_channel") && modifierNode["texture_mask_channel"].IsInteger
                        ? (int)modifierNode["texture_mask_channel"].AsInteger
                        : 0,
                });
            }
        }

        if (root.HasKey("path_nodes") && root["path_nodes"].IsArray)
        {
            foreach (TomlNode node in root["path_nodes"].AsArray)
            {
                if (!node.IsTable)
                    continue;

                string id = node.HasKey("id") && node["id"].IsString ? node["id"].AsString.Value : "";
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                config.PathNodes.Add(new TomlPathNodeConfig
                {
                    Id = id,
                    X = ReadFloat(node, "x", 0.0f),
                    Y = ReadFloat(node, "y", 0.0f),
                    Z = ReadFloat(node, "z", 0.0f),
                });
            }
        }

        if (root.HasKey("path_features") && root["path_features"].IsArray)
        {
            foreach (TomlNode featureNode in root["path_features"].AsArray)
            {
                if (!featureNode.IsTable)
                    continue;

                string id = featureNode.HasKey("id") && featureNode["id"].IsString ? featureNode["id"].AsString.Value : "";
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var feature = new TomlPathFeatureConfig
                {
                    Id = id,
                    Name = featureNode.HasKey("name") && featureNode["name"].IsString ? featureNode["name"].AsString.Value : "Path",
                    Kind = featureNode.HasKey("kind") && featureNode["kind"].IsString ? featureNode["kind"].AsString.Value : "Road",
                    Width = ReadFloat(featureNode, "width", 8.0f),
                    Depth = ReadFloat(featureNode, "depth", 0.0f),
                    SideSlope = ReadFloat(featureNode, "side_slope", 4.0f),
                    CornerSpan = ReadFloat(featureNode, "corner_span", 0.35f),
                    RoadStyle = featureNode.HasKey("road_style") && featureNode["road_style"].IsString
                        ? featureNode["road_style"].AsString.Value
                        : "Dirt",
                };

                if (featureNode.HasKey("nodes") && featureNode["nodes"].IsArray)
                {
                    foreach (TomlNode nodeId in featureNode["nodes"].AsArray)
                    {
                        if (nodeId.IsString && !string.IsNullOrWhiteSpace(nodeId.AsString.Value))
                            feature.NodeIds.Add(nodeId.AsString.Value);
                    }
                }

                config.PathFeatures.Add(feature);
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
        if (!string.IsNullOrEmpty(BiomeMaskPath))
            terrain["biome_mask"] = MakeRelative(BiomeMaskPath, baseDir);
        if (!string.IsNullOrEmpty(RiverMapImagePath))
            terrain["river_map"] = MakeRelative(RiverMapImagePath, baseDir);
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
                if (!string.IsNullOrEmpty(slot.PropertiesPath))
                    slotTable["properties"] = MakeRelative(slot.PropertiesPath, baseDir);
                slotsArray.Add(slotTable);
            }
            root["material_slots"] = slotsArray;
        }

        if (Biomes.Count > 0)
        {
            var biomesArray = new TomlArray();
            foreach (var biome in Biomes)
            {
                var biomeTable = new TomlTable();
                biomeTable["id"] = biome.Id;
                biomeTable["name"] = biome.Name;
                biomeTable["debug_color_r"] = biome.DebugColorR;
                biomeTable["debug_color_g"] = biome.DebugColorG;
                biomeTable["debug_color_b"] = biome.DebugColorB;
                biomeTable["debug_color_a"] = biome.DebugColorA;
                biomesArray.Add(biomeTable);
            }
            root["biomes"] = biomesArray;
        }

        if (BiomeLayers.Count > 0)
        {
            var layersArray = new TomlArray();
            foreach (TomlBiomeLayerConfig layer in BiomeLayers)
            {
                var layerTable = new TomlTable();
                layerTable["id"] = layer.Id;
                layerTable["biome_id"] = layer.BiomeId;
                layerTable["name"] = layer.Name;
                layerTable["enabled"] = layer.Enabled;
                layerTable["visible"] = layer.Visible;
                layerTable["material_slot"] = layer.MaterialSlotIndex;
                layerTable["priority"] = layer.PriorityOrder;
                layersArray.Add(layerTable);
            }
            root["biome_layers"] = layersArray;
        }

        if (BiomeModifiers.Count > 0)
        {
            var modifiersArray = new TomlArray();
            foreach (TomlBiomeModifierConfig modifier in BiomeModifiers)
            {
                var modifierTable = new TomlTable();
                modifierTable["id"] = modifier.Id;
                modifierTable["layer_id"] = modifier.LayerId;
                modifierTable["name"] = modifier.Name;
                modifierTable["type"] = modifier.Type;
                modifierTable["blend_mode"] = modifier.BlendMode;
                modifierTable["enabled"] = modifier.Enabled;
                modifierTable["visible"] = modifier.Visible;
                modifierTable["opacity"] = modifier.Opacity;
                modifierTable["min"] = modifier.Min;
                modifierTable["max"] = modifier.Max;
                modifierTable["min_falloff"] = modifier.MinFalloff;
                modifierTable["max_falloff"] = modifier.MaxFalloff;
                modifierTable["radius"] = modifier.Radius;
                modifierTable["angle_degrees"] = modifier.AngleDegrees;
                modifierTable["angle_range_degrees"] = modifier.AngleRangeDegrees;
                modifierTable["scale"] = modifier.Scale;
                modifierTable["offset_x"] = modifier.OffsetX;
                modifierTable["offset_y"] = modifier.OffsetY;
                modifierTable["seed"] = modifier.Seed;
                modifierTable["octaves"] = modifier.Octaves;
                modifierTable["invert"] = modifier.Invert;
                modifierTable["texture_mask_channel"] = modifier.TextureMaskChannel;
                if (!string.IsNullOrEmpty(modifier.TextureMaskPath))
                    modifierTable["texture_mask"] = MakeRelative(modifier.TextureMaskPath, baseDir);
                modifiersArray.Add(modifierTable);
            }
            root["biome_modifiers"] = modifiersArray;
        }

        if (PathNodes.Count > 0)
        {
            var nodesArray = new TomlArray();
            foreach (TomlPathNodeConfig node in PathNodes)
            {
                var nodeTable = new TomlTable();
                nodeTable["id"] = node.Id;
                nodeTable["x"] = node.X;
                nodeTable["y"] = node.Y;
                nodeTable["z"] = node.Z;
                nodesArray.Add(nodeTable);
            }
            root["path_nodes"] = nodesArray;
        }

        if (PathFeatures.Count > 0)
        {
            var featuresArray = new TomlArray();
            foreach (TomlPathFeatureConfig feature in PathFeatures)
            {
                var featureTable = new TomlTable();
                featureTable["id"] = feature.Id;
                featureTable["name"] = feature.Name;
                featureTable["kind"] = feature.Kind;
                featureTable["width"] = feature.Width;
                featureTable["depth"] = feature.Depth;
                featureTable["side_slope"] = feature.SideSlope;
                featureTable["corner_span"] = feature.CornerSpan;
                featureTable["road_style"] = feature.RoadStyle;

                var nodesArray = new TomlArray();
                foreach (string nodeId in feature.NodeIds)
                    nodesArray.Add(nodeId);
                featureTable["nodes"] = nodesArray;
                featuresArray.Add(featureTable);
            }
            root["path_features"] = featuresArray;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(tomlFilePath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

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

    private static float ReadFloat(TomlNode node, string key, float fallback)
    {
        if (!node.HasKey(key))
            return fallback;

        TomlNode value = node[key];
        if (value.IsFloat)
            return (float)value.AsFloat.Value;
        if (value.IsInteger)
            return (float)value.AsInteger.Value;
        return fallback;
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
    public string? PropertiesPath { get; set; }
}

public class TomlBiomeDefinitionConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public float DebugColorR { get; set; }
    public float DebugColorG { get; set; }
    public float DebugColorB { get; set; }
    public float DebugColorA { get; set; } = 1.0f;
}

public class TomlBiomeLayerConfig
{
    public int Id { get; set; }
    public int BiomeId { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public int MaterialSlotIndex { get; set; }
    public int PriorityOrder { get; set; }
}

public class TomlBiomeModifierConfig
{
    public int Id { get; set; }
    public int LayerId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string BlendMode { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public float Opacity { get; set; } = 1.0f;
    public float Min { get; set; }
    public float Max { get; set; } = 1.0f;
    public float MinFalloff { get; set; } = 0.001f;
    public float MaxFalloff { get; set; } = 0.001f;
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
}

public class TomlPathNodeConfig
{
    public string Id { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public class TomlPathFeatureConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Road";
    public List<string> NodeIds { get; set; } = new();
    public float Width { get; set; } = 8.0f;
    public float Depth { get; set; }
    public float SideSlope { get; set; } = 4.0f;
    public float CornerSpan { get; set; } = 0.35f;
    public string RoadStyle { get; set; } = "Dirt";
}
