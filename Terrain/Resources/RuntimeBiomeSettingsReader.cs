#nullable enable

using System.Collections.Generic;
using System.IO;
using Tommy;

namespace Terrain.Resources;

public static class RuntimeBiomeSettingsReader
{
    public static RuntimeBiomeSettings ReadFrom(string filePath)
    {
        return ReadCore(filePath, knownMaterialIds: null);
    }

    public static RuntimeBiomeSettings ReadFrom(string filePath, IReadOnlySet<string> knownMaterialIds)
    {
        return ReadCore(filePath, knownMaterialIds);
    }

    private static RuntimeBiomeSettings ReadCore(string filePath, IReadOnlySet<string>? knownMaterialIds)
    {
        using var reader = File.OpenText(filePath);
        TomlTable root = TOML.Parse(reader);

        ValidateRootKeys(root, filePath, "version", "biomes", "layers", "modifiers");
        RequireVersion(root, filePath);

        var settings = new RuntimeBiomeSettings();
        var biomeIds = ReadBiomes(root, settings, filePath);
        var layerIds = ReadLayers(root, settings, filePath, biomeIds, knownMaterialIds);
        ReadModifiers(root, settings, filePath, layerIds);
        return settings;
    }

    private static void ValidateRootKeys(TomlTable root, string filePath, params string[] allowedKeys)
    {
        var allowed = new HashSet<string>(allowedKeys);
        foreach (string key in root.Keys)
        {
            if (!allowed.Contains(key))
                throw new InvalidDataException($"Unknown root TOML field '{key}' in {filePath}.");
        }
    }

    private static void ValidateTableKeys(TomlNode tableNode, string filePath, string tableName, params string[] allowedKeys)
    {
        var allowed = new HashSet<string>(allowedKeys);
        foreach (string key in tableNode.AsTable.Keys)
        {
            if (!allowed.Contains(key))
                throw new InvalidDataException($"Unknown TOML field '{key}' in [[{tableName}]] in {filePath}.");
        }
    }

    private static void RequireVersion(TomlTable root, string filePath)
    {
        if (!root.HasKey("version") || !root["version"].IsInteger)
            throw new InvalidDataException($"Missing required integer TOML field 'version' in {filePath}.");

        long version = root["version"].AsInteger.Value;
        if (version != 1L)
            throw new InvalidDataException($"Unsupported TOML version '{version}' in {filePath}.");
    }

    private static HashSet<int> ReadBiomes(TomlTable root, RuntimeBiomeSettings settings, string filePath)
    {
        if (!root.HasKey("biomes") || !root["biomes"].IsArray)
            throw new InvalidDataException($"Missing TOML array 'biomes': {filePath}");

        var ids = new HashSet<int>();
        foreach (TomlNode biomeNode in root["biomes"].AsArray)
        {
            if (!biomeNode.IsTable)
                throw new InvalidDataException($"Each 'biomes' entry must be a table: {filePath}");

            ValidateTableKeys(biomeNode, filePath, "biomes", "id", "name");
            int id = RequireInt(biomeNode, "id", filePath);
            if (!ids.Add(id))
                throw new InvalidDataException($"Duplicate biome id '{id}': {filePath}");

            settings.Biomes.Add(new RuntimeBiomeEntry
            {
                Id = id,
                Name = RequireString(biomeNode, "name", filePath),
            });
        }

        return ids;
    }

    private static HashSet<int> ReadLayers(
        TomlTable root,
        RuntimeBiomeSettings settings,
        string filePath,
        HashSet<int> biomeIds,
        IReadOnlySet<string>? knownMaterialIds)
    {
        if (!root.HasKey("layers") || !root["layers"].IsArray)
            throw new InvalidDataException($"Missing TOML array 'layers': {filePath}");

        var ids = new HashSet<int>();
        foreach (TomlNode layerNode in root["layers"].AsArray)
        {
            if (!layerNode.IsTable)
                throw new InvalidDataException($"Each 'layers' entry must be a table: {filePath}");

            ValidateTableKeys(layerNode, filePath, "layers", "id", "biome_id", "name", "material_id", "priority", "enabled", "visible");
            int id = RequireInt(layerNode, "id", filePath);
            if (!ids.Add(id))
                throw new InvalidDataException($"Duplicate layer id '{id}': {filePath}");

            int biomeId = RequireInt(layerNode, "biome_id", filePath);
            if (!biomeIds.Contains(biomeId))
                throw new InvalidDataException($"Layer biome_id '{biomeId}' does not reference an existing biome: {filePath}");

            settings.Layers.Add(new RuntimeBiomeLayerEntry
            {
                Id = id,
                BiomeId = biomeId,
                Name = RequireString(layerNode, "name", filePath),
                MaterialId = RequireMaterialId(layerNode, filePath, knownMaterialIds),
                Priority = RequireInt(layerNode, "priority", filePath),
                Enabled = RequireBool(layerNode, "enabled", filePath),
                Visible = RequireBool(layerNode, "visible", filePath),
            });
        }

        return ids;
    }

    private static string RequireMaterialId(TomlNode node, string filePath, IReadOnlySet<string>? knownMaterialIds)
    {
        string materialId = RequireString(node, "material_id", filePath);
        if (knownMaterialIds != null && !knownMaterialIds.Contains(materialId))
            throw new InvalidDataException($"Unknown material_id '{materialId}' in {filePath}.");

        return materialId;
    }

    private static void ReadModifiers(TomlTable root, RuntimeBiomeSettings settings, string filePath, HashSet<int> layerIds)
    {
        if (!root.HasKey("modifiers"))
            return;

        if (!root["modifiers"].IsArray)
            throw new InvalidDataException($"TOML value 'modifiers' must be an array: {filePath}");

        var ids = new HashSet<int>();
        foreach (TomlNode modifierNode in root["modifiers"].AsArray)
        {
            if (!modifierNode.IsTable)
                throw new InvalidDataException($"Each 'modifiers' entry must be a table: {filePath}");

            ValidateTableKeys(
                modifierNode,
                filePath,
                "modifiers",
                "id",
                "layer_id",
                "name",
                "type",
                "blend_mode",
                "min",
                "max",
                "min_falloff",
                "max_falloff",
                "radius",
                "angle_degrees",
                "angle_range_degrees",
                "scale",
                "offset_x",
                "offset_y",
                "seed",
                "octaves",
                "invert",
                "texture_mask",
                "texture_mask_channel",
                "opacity",
                "enabled",
                "visible");
            int id = RequireInt(modifierNode, "id", filePath);
            if (!ids.Add(id))
                throw new InvalidDataException($"Duplicate modifier id '{id}': {filePath}");

            int layerId = RequireInt(modifierNode, "layer_id", filePath);
            if (!layerIds.Contains(layerId))
                throw new InvalidDataException($"Modifier layer_id '{layerId}' does not reference an existing layer: {filePath}");

            settings.Modifiers.Add(new RuntimeBiomeModifierEntry
            {
                Id = id,
                LayerId = layerId,
                Name = ReadOptionalString(modifierNode, "name", filePath) ?? $"Modifier {id}",
                Type = RequireString(modifierNode, "type", filePath),
                BlendMode = RequireString(modifierNode, "blend_mode", filePath),
                Min = RequireFloat(modifierNode, "min", filePath),
                Max = RequireFloat(modifierNode, "max", filePath),
                MinFalloff = RequireFloat(modifierNode, "min_falloff", filePath),
                MaxFalloff = RequireFloat(modifierNode, "max_falloff", filePath),
                Radius = ReadOptionalFloat(modifierNode, "radius", filePath, 1.0f),
                AngleDegrees = ReadOptionalFloat(modifierNode, "angle_degrees", filePath, 0.0f),
                AngleRangeDegrees = ReadOptionalFloat(modifierNode, "angle_range_degrees", filePath, 180.0f),
                Scale = ReadOptionalFloat(modifierNode, "scale", filePath, 1.0f),
                OffsetX = ReadOptionalFloat(modifierNode, "offset_x", filePath, 0.0f),
                OffsetY = ReadOptionalFloat(modifierNode, "offset_y", filePath, 0.0f),
                Seed = ReadOptionalFloat(modifierNode, "seed", filePath, 0.0f),
                Octaves = ReadOptionalFloat(modifierNode, "octaves", filePath, 4.0f),
                Invert = ReadOptionalFloat(modifierNode, "invert", filePath, 0.0f),
                TextureMaskPath = ReadOptionalRelativePath(modifierNode, "texture_mask", filePath),
                TextureMaskChannel = ReadOptionalInt(modifierNode, "texture_mask_channel", filePath, 0),
                Opacity = RequireFloat(modifierNode, "opacity", filePath),
                Enabled = RequireBool(modifierNode, "enabled", filePath),
                Visible = RequireBool(modifierNode, "visible", filePath),
            });
        }
    }

    private static string RequireString(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key) || !node[key].IsString || string.IsNullOrWhiteSpace(node[key].AsString.Value))
            throw new InvalidDataException($"Missing required non-empty TOML string '{key}': {filePath}");

        return node[key].AsString.Value;
    }

    private static string? ReadOptionalString(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key))
            return null;

        if (!node[key].IsString || string.IsNullOrWhiteSpace(node[key].AsString.Value))
            throw new InvalidDataException($"Optional TOML string '{key}' must be non-empty when present: {filePath}");

        return node[key].AsString.Value;
    }

    private static int RequireInt(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key) || !node[key].IsInteger)
            throw new InvalidDataException($"Missing required TOML integer '{key}': {filePath}");

        long value = node[key].AsInteger.Value;
        if (value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException($"TOML integer '{key}' is outside the supported range in {filePath}.");

        return (int)value;
    }

    private static int ReadOptionalInt(TomlNode node, string key, string filePath, int fallback)
    {
        if (!node.HasKey(key))
            return fallback;

        return RequireInt(node, key, filePath);
    }

    private static float RequireFloat(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key))
            throw new InvalidDataException($"Missing required TOML number '{key}': {filePath}");

        TomlNode value = node[key];
        if (value.IsFloat)
            return (float)value.AsFloat.Value;
        if (value.IsInteger)
            return (float)value.AsInteger.Value;

        throw new InvalidDataException($"TOML value '{key}' must be numeric: {filePath}");
    }

    private static float ReadOptionalFloat(TomlNode node, string key, string filePath, float fallback)
    {
        if (!node.HasKey(key))
            return fallback;

        return RequireFloat(node, key, filePath);
    }

    private static string? ReadOptionalRelativePath(TomlNode node, string key, string filePath)
    {
        string? path = ReadOptionalString(node, key, filePath);
        if (path == null)
            return null;

        string trimmed = path.Trim();
        if (trimmed.StartsWith("/", System.StringComparison.Ordinal)
            || trimmed.StartsWith("\\", System.StringComparison.Ordinal)
            || (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':'))
        {
            throw new InvalidDataException($"TOML path '{key}' must be relative in {filePath}: {path}");
        }

        foreach (string segment in trimmed.Replace('\\', '/').Split('/'))
        {
            if (segment == "..")
                throw new InvalidDataException($"TOML path '{key}' must not contain parent traversal in {filePath}: {path}");
        }

        return trimmed.Replace('\\', '/');
    }

    private static bool RequireBool(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key) || !node[key].IsBoolean)
            throw new InvalidDataException($"Missing required TOML boolean '{key}': {filePath}");

        return node[key].AsBoolean.Value;
    }
}
