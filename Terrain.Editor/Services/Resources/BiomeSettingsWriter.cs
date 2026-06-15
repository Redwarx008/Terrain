#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Tommy;

namespace Terrain.Editor.Services.Resources;

public readonly record struct EditorBiomeDefinition(int Id, string Name);

public readonly record struct EditorBiomeLayerDefinition(
    int Id,
    int BiomeId,
    string Name,
    string MaterialId,
    int Priority,
    bool Enabled,
    bool Visible);

public readonly record struct EditorBiomeModifierDefinition(
    int Id,
    int LayerId,
    string Name,
    string Type,
    string BlendMode,
    float Min,
    float Max,
    float MinFalloff,
    float MaxFalloff,
    float Radius,
    float AngleDegrees,
    float AngleRangeDegrees,
    float Scale,
    float OffsetX,
    float OffsetY,
    float Seed,
    float Octaves,
    float Invert,
    string? TextureMask,
    int TextureMaskChannel,
    float Opacity,
    bool Enabled,
    bool Visible);

public sealed class BiomeSettingsWriter
{
    private const string TopCommentTemplate =
        "# Example biome:\n" +
        "# [[biomes]]\n" +
        "# id = 1\n" +
        "# name = \"Default\"\n" +
        "#\n" +
        "# Example layer:\n" +
        "# [[layers]]\n" +
        "# id = 1\n" +
        "# biome_id = 1\n" +
        "# name = \"Base\"\n" +
        "# material_id = \"plains\"\n" +
        "# priority = 0\n" +
        "# enabled = true\n" +
        "# visible = true\n" +
        "#\n" +
        "# Example modifier:\n" +
        "# [[modifiers]]\n" +
        "# id = 1\n" +
        "# layer_id = 1\n" +
        "# name = \"Slope\"\n" +
        "# type = \"slope\"\n" +
        "# blend_mode = \"add\"\n" +
        "# min = 0.2\n" +
        "# max = 0.8\n" +
        "# min_falloff = 0.1\n" +
        "# max_falloff = 0.1\n" +
        "# opacity = 1.0\n" +
        "# enabled = true\n" +
        "# visible = true\n" +
        "\n";

    public void Write(
        EditorResourceSession session,
        IReadOnlyList<EditorBiomeDefinition> biomes,
        IReadOnlyList<EditorBiomeLayerDefinition> layers,
        IReadOnlyList<EditorBiomeModifierDefinition> modifiers)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (biomes == null)
            throw new ArgumentNullException(nameof(biomes));
        if (layers == null)
            throw new ArgumentNullException(nameof(layers));
        if (modifiers == null)
            throw new ArgumentNullException(nameof(modifiers));
        if (!session.BiomeSettings.IsWritable)
            throw new InvalidOperationException($"Biome settings target is read-only: {session.BiomeSettings.ResolvedPath}");
        Write(session.BiomeSettings.ResolvedPath, biomes, layers, modifiers);
    }

    internal void Write(
        string outputPath,
        IReadOnlyList<EditorBiomeDefinition> biomes,
        IReadOnlyList<EditorBiomeLayerDefinition> layers,
        IReadOnlyList<EditorBiomeModifierDefinition> modifiers)
    {
        if (biomes == null)
            throw new ArgumentNullException(nameof(biomes));
        if (layers == null)
            throw new ArgumentNullException(nameof(layers));
        if (modifiers == null)
            throw new ArgumentNullException(nameof(modifiers));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be null or empty.", nameof(outputPath));

        var root = new TomlTable
        {
            ["version"] = 1,
            ["biomes"] = CreateBiomesArray(biomes),
            ["layers"] = CreateLayersArray(layers),
            ["modifiers"] = CreateModifiersArray(modifiers),
        };

        WriteTomlWithTemplate(outputPath, root, TopCommentTemplate);
    }

    private static TomlArray CreateBiomesArray(IReadOnlyList<EditorBiomeDefinition> biomes)
    {
        var array = new TomlArray();
        foreach (EditorBiomeDefinition biome in biomes)
        {
            array.Add(new TomlTable
            {
                ["id"] = biome.Id,
                ["name"] = biome.Name,
            });
        }

        return array;
    }

    private static TomlArray CreateLayersArray(IReadOnlyList<EditorBiomeLayerDefinition> layers)
    {
        var array = new TomlArray();
        foreach (EditorBiomeLayerDefinition layer in layers)
        {
            array.Add(new TomlTable
            {
                ["id"] = layer.Id,
                ["biome_id"] = layer.BiomeId,
                ["name"] = layer.Name,
                ["material_id"] = layer.MaterialId,
                ["priority"] = layer.Priority,
                ["enabled"] = layer.Enabled,
                ["visible"] = layer.Visible,
            });
        }

        return array;
    }

    private static TomlArray CreateModifiersArray(IReadOnlyList<EditorBiomeModifierDefinition> modifiers)
    {
        var array = new TomlArray();
        foreach (EditorBiomeModifierDefinition modifier in modifiers)
        {
            var table = new TomlTable
            {
                ["id"] = modifier.Id,
                ["layer_id"] = modifier.LayerId,
                ["name"] = modifier.Name,
                ["type"] = modifier.Type,
                ["blend_mode"] = modifier.BlendMode,
                ["min"] = modifier.Min,
                ["max"] = modifier.Max,
                ["min_falloff"] = modifier.MinFalloff,
                ["max_falloff"] = modifier.MaxFalloff,
                ["radius"] = modifier.Radius,
                ["angle_degrees"] = modifier.AngleDegrees,
                ["angle_range_degrees"] = modifier.AngleRangeDegrees,
                ["scale"] = modifier.Scale,
                ["offset_x"] = modifier.OffsetX,
                ["offset_y"] = modifier.OffsetY,
                ["seed"] = modifier.Seed,
                ["octaves"] = modifier.Octaves,
                ["invert"] = modifier.Invert,
                ["texture_mask_channel"] = modifier.TextureMaskChannel,
                ["opacity"] = modifier.Opacity,
                ["enabled"] = modifier.Enabled,
                ["visible"] = modifier.Visible,
            };

            if (!string.IsNullOrWhiteSpace(modifier.TextureMask))
            {
                table["texture_mask"] = NormalizeRelativePath(modifier.TextureMask);
            }

            array.Add(table);
        }

        return array;
    }

    private static string NormalizeRelativePath(string path)
    {
        string trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
            throw new InvalidDataException($"Biome modifier texture mask path must be relative: {path}");

        string normalized = trimmed.Replace('\\', '/');
        foreach (string segment in normalized.Split('/'))
        {
            if (segment == "..")
                throw new InvalidDataException($"Biome modifier texture mask path must not contain parent traversal: {path}");
        }

        return normalized;
    }

    private static void WriteTomlWithTemplate(string outputPath, TomlTable root, string template)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var writer = File.CreateText(outputPath);
        writer.Write(template);
        root.WriteTo(writer);
    }
}
