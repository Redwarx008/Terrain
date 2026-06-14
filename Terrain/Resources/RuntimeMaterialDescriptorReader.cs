#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Tommy;

namespace Terrain.Resources;

public static class RuntimeMaterialDescriptorReader
{
    private const int MaxMaterialIndex = 254;

    public static RuntimeMaterialDescriptor ReadFrom(string filePath)
    {
        using var reader = File.OpenText(filePath);
        TomlTable root = TOML.Parse(reader);

        ValidateRootKeys(root, filePath, "version", "materials");
        RequireVersion(root, filePath);

        var descriptor = new RuntimeMaterialDescriptor();
        if (!root.HasKey("materials") || !root["materials"].IsArray)
            throw new InvalidDataException($"Missing TOML array 'materials': {filePath}");

        var ids = new HashSet<string>();
        var indices = new HashSet<int>();
        foreach (TomlNode materialNode in root["materials"].AsArray)
        {
            if (!materialNode.IsTable)
                throw new InvalidDataException($"Each 'materials' entry must be a table: {filePath}");

            ValidateTableKeys(materialNode, filePath, "materials", "id", "index", "name", "albedo", "normal", "properties");
            string id = RequireString(materialNode, "id", filePath);
            int index = RequireNonNegativeInt(materialNode, "index", filePath);
            string name = RequireString(materialNode, "name", filePath);
            if (!ids.Add(id))
                throw new InvalidDataException($"Duplicate material id '{id}': {filePath}");
            if (!indices.Add(index))
                throw new InvalidDataException($"Duplicate material index '{index}': {filePath}");

            descriptor.Materials.Add(new RuntimeMaterialEntry
            {
                Id = id,
                Index = index,
                Name = name,
                AlbedoPath = ReadOptionalRelativePath(materialNode, "albedo", filePath),
                NormalPath = ReadOptionalRelativePath(materialNode, "normal", filePath),
                PropertiesPath = ReadOptionalRelativePath(materialNode, "properties", filePath),
            });
        }

        return descriptor;
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

    private static string RequireString(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key) || !node[key].IsString || string.IsNullOrWhiteSpace(node[key].AsString.Value))
            throw new InvalidDataException($"Missing required non-empty TOML string '{key}': {filePath}");

        return node[key].AsString.Value;
    }

    private static string? ReadOptionalRelativePath(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key))
            return null;

        if (!node[key].IsString || string.IsNullOrWhiteSpace(node[key].AsString.Value))
            throw new InvalidDataException($"Optional TOML string '{key}' must be non-empty when present: {filePath}");

        return NormalizeRelativePath(node[key].AsString.Value, key, filePath);
    }

    private static int RequireNonNegativeInt(TomlNode node, string key, string filePath)
    {
        if (!node.HasKey(key) || !node[key].IsInteger)
            throw new InvalidDataException($"Missing required TOML integer '{key}': {filePath}");

        long rawValue = node[key].AsInteger.Value;
        if (rawValue < 0 || rawValue > MaxMaterialIndex)
            throw new InvalidDataException($"TOML integer '{key}' must be between 0 and {MaxMaterialIndex} in {filePath}.");

        return (int)rawValue;
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
        if (segments.Count != 1)
            throw new InvalidDataException($"TOML path '{key}' must be a file name relative to the materials descriptor directory in {filePath}: {path}");

        return string.Join("/", segments);
    }

    private static bool IsRootedPath(string path)
    {
        if (path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal))
            return true;

        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
    }
}
