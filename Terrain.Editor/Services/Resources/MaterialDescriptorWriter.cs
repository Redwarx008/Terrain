#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Tommy;

namespace Terrain.Editor.Services.Resources;

public readonly record struct EditorMaterialDescriptorSlot(
    string Id,
    int Index,
    string Name,
    string? Albedo,
    string? Normal,
    string? Properties);

public sealed class MaterialDescriptorWriter
{
    private const string TopCommentTemplate =
        "# Example material:\n" +
        "# [[materials]]\n" +
        "# id = \"plains\"\n" +
        "# index = 0\n" +
        "# name = \"Plains\"\n" +
        "# albedo = \"plains_01_diffuse.dds\"\n" +
        "# normal = \"plains_01_normal.dds\"\n" +
        "# properties = \"plains_01_properties.dds\"\n" +
        "\n";

    public void Write(EditorResourceSession session, IReadOnlyList<EditorMaterialDescriptorSlot> slots)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (slots == null)
            throw new ArgumentNullException(nameof(slots));
        if (!session.MaterialDescriptor.IsWritable)
            throw new InvalidOperationException($"Material descriptor target is read-only: {session.MaterialDescriptor.ResolvedPath}");
        Write(session.MaterialDescriptor.ResolvedPath, slots);
    }

    internal void Write(string outputPath, IReadOnlyList<EditorMaterialDescriptorSlot> slots)
    {
        if (slots == null)
            throw new ArgumentNullException(nameof(slots));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be null or empty.", nameof(outputPath));

        var root = new TomlTable
        {
            ["version"] = 1,
        };

        var materials = new TomlArray();
        foreach (EditorMaterialDescriptorSlot slot in slots)
        {
            var material = new TomlTable
            {
                ["id"] = slot.Id,
                ["index"] = slot.Index,
                ["name"] = slot.Name,
            };
            AddOptionalPath(material, "albedo", slot.Albedo);
            AddOptionalPath(material, "normal", slot.Normal);
            AddOptionalPath(material, "properties", slot.Properties);
            materials.Add(material);
        }

        root["materials"] = materials;

        WriteTomlWithTemplate(outputPath, root, TopCommentTemplate);
    }

    private static void AddOptionalPath(TomlTable material, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (Path.IsPathRooted(value) || value.Contains('/') || value.Contains('\\'))
            throw new InvalidDataException($"Material texture path must be a file name relative to descriptor.toml: {value}");

        material[key] = value;
    }

    private static void WriteTomlWithTemplate(string outputPath, TomlTable root, string template)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var writer = File.CreateText(outputPath);
        writer.Write(template);
        root.WriteTo(writer);
    }
}
