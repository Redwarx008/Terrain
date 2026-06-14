#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Terrain.Resources;

namespace Terrain.Editor.Services.Resources;

public static class EditorResourceSaveService
{
    public static void Save(
        EditorResourceSession session,
        ushort[] heightData,
        int width,
        int height,
        BiomeMask biomeMask,
        float heightScale,
        IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots,
        EditorBiomeSettingsSnapshot biomeSnapshot)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(heightData);
        ArgumentNullException.ThrowIfNull(biomeMask);
        ArgumentNullException.ThrowIfNull(descriptorSlots);
        ArgumentNullException.ThrowIfNull(biomeSnapshot);

        EnsureWritable(session.MapDefinition, "Map definition");
        EnsureWritable(session.Heightmap, "Heightmap");
        EnsureWritable(session.BiomeMask, "Biome mask");
        EnsureWritable(session.MaterialDescriptor, "Material descriptor");
        EnsureWritable(session.BiomeSettings, "Biome settings");

        var mapDefinition = new RuntimeMapDefinition
        {
            HeightmapPath = session.MapDefinitionModel.HeightmapPath,
            TerrainDataPath = session.MapDefinitionModel.TerrainDataPath,
            RiversPath = session.MapDefinitionModel.RiversPath,
            ProvincesPath = session.MapDefinitionModel.ProvincesPath,
            HeightScale = heightScale,
        };

        var mapDefinitionWriter = new MapDefinitionWriter();
        var heightmapWriter = new HeightmapWriter();
        var biomeMaskWriter = new BiomeMaskWriter();
        var materialDescriptorWriter = new MaterialDescriptorWriter();
        var biomeSettingsWriter = new BiomeSettingsWriter();

        using var transaction = new AtomicResourceWriteTransaction();
        string stagedMapDefinition = transaction.CreateStagingPath(session.MapDefinition.ResolvedPath);
        string stagedHeightmap = transaction.CreateStagingPath(session.Heightmap.ResolvedPath);
        string stagedBiomeMask = transaction.CreateStagingPath(session.BiomeMask.ResolvedPath);
        string stagedMaterialDescriptor = transaction.CreateStagingPath(session.MaterialDescriptor.ResolvedPath);
        string stagedBiomeSettings = transaction.CreateStagingPath(session.BiomeSettings.ResolvedPath);

        mapDefinitionWriter.Write(stagedMapDefinition, mapDefinition);
        heightmapWriter.Write(stagedHeightmap, heightData, width, height);
        biomeMaskWriter.Write(stagedBiomeMask, biomeMask);
        materialDescriptorWriter.Write(stagedMaterialDescriptor, descriptorSlots);
        biomeSettingsWriter.Write(stagedBiomeSettings, biomeSnapshot.Biomes, biomeSnapshot.Layers, biomeSnapshot.Modifiers);

        transaction.Commit();
    }

    private static void EnsureWritable(ResolvedGameResource resource, string displayName)
    {
        if (!resource.IsWritable)
            throw new InvalidOperationException($"{displayName} target is read-only: {resource.ResolvedPath}");
    }
}
