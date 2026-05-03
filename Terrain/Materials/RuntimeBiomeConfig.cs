#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Terrain.Shared;
using Tommy;

namespace Terrain;

public sealed class RuntimeBiomeConfig
{
    public float HeightScale { get; set; } = 100.0f;

    public List<(int index, string albedoPath, string? normalPath, string? propertiesPath)> MaterialSlots { get; } = new();

    public List<TerrainBiomeRuleLayer> BiomeLayers { get; } = new();

    public static RuntimeBiomeConfig ReadFromToml(string tomlFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tomlFilePath);

        var result = new RuntimeBiomeConfig();
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(tomlFilePath)) ?? "";

        using var reader = File.OpenText(tomlFilePath);
        TomlTable root = TOML.Parse(reader);

        if (root.HasKey("terrain") && root["terrain"].IsTable)
        {
            TomlNode terrain = root["terrain"];
            if (terrain.HasKey("height_scale"))
            {
                TomlNode heightScaleNode = terrain["height_scale"];
                result.HeightScale = heightScaleNode.IsFloat
                    ? (float)heightScaleNode.AsFloat.Value
                    : heightScaleNode.IsInteger
                    ? (float)heightScaleNode.AsInteger.Value
                    : result.HeightScale;
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

                string? albedoPath = slotNode.HasKey("albedo") && slotNode["albedo"].IsString
                    ? ResolvePath(slotNode["albedo"].AsString.Value, baseDir)
                    : null;
                string? normalPath = slotNode.HasKey("normal") && slotNode["normal"].IsString
                    ? ResolvePath(slotNode["normal"].AsString.Value, baseDir)
                    : null;
                string? propertiesPath = slotNode.HasKey("properties") && slotNode["properties"].IsString
                    ? ResolvePath(slotNode["properties"].AsString.Value, baseDir)
                    : null;

                result.MaterialSlots.Add((index, albedoPath ?? "", normalPath, propertiesPath));
            }
        }

        Dictionary<int, TerrainBiomeRuleLayer> layersById = new();
        if (root.HasKey("biome_layers") && root["biome_layers"].IsArray)
        {
            foreach (TomlNode layerNode in root["biome_layers"].AsArray)
            {
                if (!layerNode.IsTable)
                    continue;

                int id = layerNode.HasKey("id") && layerNode["id"].IsInteger ? (int)layerNode["id"].AsInteger : 0;
                var layer = new TerrainBiomeRuleLayer
                {
                    Name = layerNode.HasKey("name") && layerNode["name"].IsString ? layerNode["name"].AsString.Value : "Layer",
                    BiomeId = layerNode.HasKey("biome_id") && layerNode["biome_id"].IsInteger ? (int)layerNode["biome_id"].AsInteger : 0,
                    Enabled = layerNode.HasKey("enabled") && layerNode["enabled"].IsBoolean ? layerNode["enabled"].AsBoolean.Value : true,
                    Visible = layerNode.HasKey("visible") && layerNode["visible"].IsBoolean ? layerNode["visible"].AsBoolean.Value : true,
                    MaterialSlotIndex = layerNode.HasKey("material_slot") && layerNode["material_slot"].IsInteger ? (int)layerNode["material_slot"].AsInteger : 0,
                    PriorityOrder = layerNode.HasKey("priority") && layerNode["priority"].IsInteger ? (int)layerNode["priority"].AsInteger : 0,
                };

                layersById[id] = layer;
                result.BiomeLayers.Add(layer);
            }

            result.BiomeLayers.Sort(static (left, right) => left.PriorityOrder.CompareTo(right.PriorityOrder));
        }

        if (root.HasKey("biome_modifiers") && root["biome_modifiers"].IsArray)
        {
            foreach (TomlNode modifierNode in root["biome_modifiers"].AsArray)
            {
                if (!modifierNode.IsTable)
                    continue;

                int layerId = modifierNode.HasKey("layer_id") && modifierNode["layer_id"].IsInteger
                    ? (int)modifierNode["layer_id"].AsInteger
                    : 0;
                if (!layersById.TryGetValue(layerId, out TerrainBiomeRuleLayer? layer))
                    continue;

                if (!Enum.TryParse(
                        modifierNode.HasKey("type") && modifierNode["type"].IsString ? modifierNode["type"].AsString.Value : nameof(BiomeModifierType.HeightRange),
                        ignoreCase: true,
                        out BiomeModifierType type))
                {
                    type = BiomeModifierType.HeightRange;
                }

                if (!Enum.TryParse(
                        modifierNode.HasKey("blend_mode") && modifierNode["blend_mode"].IsString ? modifierNode["blend_mode"].AsString.Value : nameof(BiomeModifierBlendMode.Multiply),
                        ignoreCase: true,
                        out BiomeModifierBlendMode blendMode))
                {
                    blendMode = BiomeModifierBlendMode.Multiply;
                }

                layer.Modifiers.Add(new TerrainBiomeModifier
                {
                    Name = modifierNode.HasKey("name") && modifierNode["name"].IsString ? modifierNode["name"].AsString.Value : "Modifier",
                    Type = type,
                    BlendMode = blendMode,
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

        return result;
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

    private static string? ResolvePath(string? relativeOrAbsolute, string baseDir)
    {
        if (string.IsNullOrEmpty(relativeOrAbsolute))
            return null;
        if (Path.IsPathRooted(relativeOrAbsolute))
            return relativeOrAbsolute;
        return Path.GetFullPath(Path.Combine(baseDir, relativeOrAbsolute.Replace('/', Path.DirectorySeparatorChar)));
    }
}
