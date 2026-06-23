#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Tommy;

namespace Terrain.Resources;

public static class RuntimeMapDefinitionReader
{
    public static RuntimeMapDefinition ReadFrom(string filePath, bool requireHeightmap = true)
    {
        using var reader = File.OpenText(filePath);
        TomlTable root = TOML.Parse(reader);

        ValidateRootKeys(root, filePath, "version", "terrain", "settings");
        RequireVersion(root, filePath);
        TomlNode terrain = RequireTable(root, "terrain", filePath);
        TomlNode settings = RequireTable(root, "settings", filePath);
        ValidateTableKeys(terrain, filePath, "terrain", "heightmap", "terrain_data", "rivers", "provinces");
        ValidateTableKeys(settings, filePath, "settings", "height_scale", "river_min_width", "river_max_width", "river_max_visible_camera_height");

        string heightmap = requireHeightmap
            ? RequireRelativePath(terrain, "heightmap", filePath)
            : string.Empty;
        string terrainData = RequireRelativePath(terrain, "terrain_data", filePath);
        float heightScale = RequireFloat(settings, "height_scale", filePath);
        if (!float.IsFinite(heightScale) || heightScale <= 0.0f)
            throw new InvalidDataException($"height_scale must be greater than 0: {filePath}");
        float riverMinWidth = ReadOptionalFloat(settings, "river_min_width", filePath, 1.0f);
        float riverMaxWidth = ReadOptionalFloat(settings, "river_max_width", filePath, 4.0f);
        ValidateRiverWidthRange(riverMinWidth, riverMaxWidth, filePath);
        float riverMaxVisibleCameraHeight = ReadOptionalFloat(settings, "river_max_visible_camera_height", filePath, 3000.0f);
        ValidateRiverMaxVisibleCameraHeight(riverMaxVisibleCameraHeight, filePath);

        return new RuntimeMapDefinition
        {
            HeightmapPath = heightmap,
            TerrainDataPath = terrainData,
            RiversPath = ReadOptionalRelativePath(terrain, "rivers", filePath),
            ProvincesPath = ReadOptionalRelativePath(terrain, "provinces", filePath),
            HeightScale = heightScale,
            RiverMinWidth = riverMinWidth,
            RiverMaxWidth = riverMaxWidth,
            RiverMaxVisibleCameraHeight = riverMaxVisibleCameraHeight,
        };
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
                throw new InvalidDataException($"Unknown TOML field '{key}' in [{tableName}] in {filePath}.");
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

    private static TomlNode RequireTable(TomlTable root, string key, string filePath)
    {
        if (!root.HasKey(key) || !root[key].IsTable)
            throw new InvalidDataException($"Missing TOML table [{key}]: {filePath}");

        return root[key];
    }

    private static string RequireString(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key) || !node[key].IsString || string.IsNullOrWhiteSpace(node[key].AsString.Value))
            throw new InvalidDataException($"Missing required TOML string '{key}': {filePath}");

        return node[key].AsString.Value;
    }

    private static string RequireRelativePath(TomlNode node, string key, string filePath)
    {
        return NormalizeRelativePath(RequireString(node, key, filePath), key, filePath);
    }

    private static string? ReadOptionalRelativePath(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key))
            return null;

        if (!node[key].IsString)
            throw new InvalidDataException($"Optional TOML value '{key}' must be a string in {filePath}.");

        return NormalizeRelativePath(node[key].AsString.Value, key, filePath);
    }

    private static string NormalizeRelativePath(string path, string key, string filePath)
    {
        string trimmed = path.Trim();
        if (IsRootedPath(trimmed))
            throw new InvalidDataException($"TOML path '{key}' must be relative in {filePath}: {path}");

        var segments = new List<string>();
        foreach (string segment in trimmed.Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
                throw new InvalidDataException($"TOML path '{key}' must not contain parent traversal in {filePath}: {path}");
            segments.Add(segment);
        }

        if (segments.Count == 0)
            throw new InvalidDataException($"TOML path '{key}' normalizes to empty in {filePath}.");

        return string.Join("/", segments);
    }

    private static bool IsRootedPath(string path)
    {
        if (path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal))
            return true;

        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
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

        TomlNode value = node[key];
        if (value.IsFloat)
            return (float)value.AsFloat.Value;
        if (value.IsInteger)
            return (float)value.AsInteger.Value;

        throw new InvalidDataException($"TOML value '{key}' must be numeric: {filePath}");
    }

    private static void ValidateRiverWidthRange(float riverMinWidth, float riverMaxWidth, string filePath)
    {
        if (!float.IsFinite(riverMinWidth))
            throw new InvalidDataException($"river_min_width must be finite: {filePath}");
        if (!float.IsFinite(riverMaxWidth))
            throw new InvalidDataException($"river_max_width must be finite: {filePath}");
        if (riverMinWidth <= 0.0f)
            throw new InvalidDataException($"river_min_width must be greater than 0: {filePath}");
        if (riverMaxWidth < riverMinWidth)
            throw new InvalidDataException($"river_max_width must be greater than or equal to river_min_width: {filePath}");
    }

    private static void ValidateRiverMaxVisibleCameraHeight(float value, string filePath)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"river_max_visible_camera_height must be finite: {filePath}");
        if (value <= 0.0f)
            throw new InvalidDataException($"river_max_visible_camera_height must be greater than 0: {filePath}");
    }
}
