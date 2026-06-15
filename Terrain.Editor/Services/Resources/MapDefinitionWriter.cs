#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Terrain.Resources;
using Tommy;

namespace Terrain.Editor.Services.Resources;

public sealed class MapDefinitionWriter
{
    private static readonly string[] TopCommentTemplateLines =
    [
        "# Optional terrain companion resources:",
        "# rivers = \"rivers.png\"",
        "# provinces = \"provinces.png\"",
    ];

    public void Write(EditorResourceSession session, RuntimeMapDefinition mapDefinition)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (mapDefinition == null)
            throw new ArgumentNullException(nameof(mapDefinition));
        if (!session.MapDefinition.IsWritable)
            throw new InvalidOperationException($"Map definition target is read-only: {session.MapDefinition.ResolvedPath}");
        Write(session.MapDefinition.ResolvedPath, mapDefinition);
    }

    internal void Write(string outputPath, RuntimeMapDefinition mapDefinition)
    {
        if (mapDefinition == null)
            throw new ArgumentNullException(nameof(mapDefinition));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be null or empty.", nameof(outputPath));
        if (mapDefinition.HeightScale <= 0.0f)
            throw new InvalidDataException("Map definition height_scale must be greater than 0.");

        var terrain = new TomlTable
        {
            ["heightmap"] = mapDefinition.HeightmapPath,
            ["terrain_data"] = mapDefinition.TerrainDataPath,
        };

        AddOptionalPath(terrain, "rivers", mapDefinition.RiversPath);
        AddOptionalPath(terrain, "provinces", mapDefinition.ProvincesPath);

        var root = new TomlTable
        {
            ["version"] = 1,
            ["terrain"] = terrain,
            ["settings"] = new TomlTable
            {
                ["height_scale"] = mapDefinition.HeightScale,
            },
        };

        WriteTomlWithTemplate(outputPath, root, TopCommentTemplateLines);
    }

    private static void AddOptionalPath(TomlTable table, string key, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (Path.IsPathRooted(path))
            throw new InvalidDataException($"Map definition path '{key}' must be relative: {path}");

        string normalized = path.Trim().Replace('\\', '/');
        foreach (string segment in normalized.Split('/'))
        {
            if (segment == "..")
                throw new InvalidDataException($"Map definition path '{key}' must not contain parent traversal: {path}");
        }

        table[key] = normalized;
    }

    private static void WriteTomlWithTemplate(string outputPath, TomlTable root, IReadOnlyList<string> templateLines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var writer = File.CreateText(outputPath);
        foreach (string line in templateLines)
        {
            writer.WriteLine(line);
        }

        writer.WriteLine();
        root.WriteTo(writer);
    }
}
