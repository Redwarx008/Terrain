#nullable enable

using System;
using System.IO;
using Terrain.Resources;

namespace Terrain.Editor.Services.Resources;

public sealed class EditorMapDataScaffoldService
{
    private const string MapDefinitionPath = "map/default.toml";
    private const string BiomeSettingsPath = "map/biome_settings.toml";
    private const string MaterialDescriptorPath = "map/materials/descriptor.toml";

    public void EnsureScaffold(GameResourceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        EnsureMapDefinition(resolver);
        EnsureBiomeSettings(resolver);
        EnsureMaterialDescriptor(resolver);
    }

    private static void EnsureMapDefinition(GameResourceResolver resolver)
    {
        ResolvedGameResource target = resolver.ResolveWritableTarget(MapDefinitionPath);
        if (File.Exists(target.ResolvedPath))
            return;

        new MapDefinitionWriter().Write(
            target.ResolvedPath,
            new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 100.0f,
                RiverMinWidth = 1.0f,
                RiverMaxWidth = 4.0f,
            });
    }

    private static void EnsureBiomeSettings(GameResourceResolver resolver)
    {
        ResolvedGameResource target = resolver.ResolveWritableTarget(BiomeSettingsPath);
        if (File.Exists(target.ResolvedPath))
            return;

        new BiomeSettingsWriter().Write(
            target.ResolvedPath,
            Array.Empty<EditorBiomeDefinition>(),
            Array.Empty<EditorBiomeLayerDefinition>(),
            Array.Empty<EditorBiomeModifierDefinition>());
    }

    private static void EnsureMaterialDescriptor(GameResourceResolver resolver)
    {
        ResolvedGameResource target = resolver.ResolveWritableTarget(MaterialDescriptorPath);
        if (File.Exists(target.ResolvedPath))
            return;

        new MaterialDescriptorWriter().Write(
            target.ResolvedPath,
            Array.Empty<EditorMaterialDescriptorSlot>());
    }
}
