#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Terrain.Editor.Services;
using Terrain.Resources;

namespace Terrain.Editor.Services.Resources;

public static class EditorResourceSaveService
{
    public static void Save(
        EditorResourceSession session,
        ushort[]? heightData,
        int width,
        int height,
        BiomeMask? biomeMask,
        float heightScale,
        IReadOnlyList<EditorMaterialDescriptorSlot>? descriptorSlots,
        EditorBiomeSettingsSnapshot? biomeSnapshot)
    {
        Save(
            session,
            heightData,
            width,
            height,
            biomeMask,
            heightScale,
            descriptorSlots,
            biomeSnapshot,
            progress: null,
            riverMaxVisibleCameraHeight: session.MapDefinitionModel.RiverMaxVisibleCameraHeight,
            seaLevel: session.MapDefinitionModel.SeaLevel);
    }

    public static void Save(
        EditorResourceSession session,
        ushort[]? heightData,
        int width,
        int height,
        BiomeMask? biomeMask,
        float heightScale,
        IReadOnlyList<EditorMaterialDescriptorSlot>? descriptorSlots,
        EditorBiomeSettingsSnapshot? biomeSnapshot,
        IProgress<AuthoringSaveProgress>? progress = null,
        float? riverMaxVisibleCameraHeight = null,
        float? seaLevel = null,
        EditorDirtyResource dirtyResources = EditorDirtyResource.All)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (dirtyResources == EditorDirtyResource.None)
        {
            progress?.Report(AuthoringSaveProgress.Completed(AuthoringSaveProgress.TotalSteps, AuthoringSaveProgress.TotalSteps, "No dirty authoring resources to save."));
            return;
        }

        progress?.Report(AuthoringSaveProgress.Running(2, AuthoringSaveProgress.TotalSteps, "Validating save targets..."));

        if (HasDirtyResource(dirtyResources, EditorDirtyResource.MapDefinition))
            EnsureWritable(session.MapDefinition, "Map definition");
        if (HasDirtyResource(dirtyResources, EditorDirtyResource.Heightmap))
            EnsureWritable(session.Heightmap, "Heightmap");
        if (HasDirtyResource(dirtyResources, EditorDirtyResource.BiomeMask))
            EnsureWritable(session.BiomeMask, "Biome mask");
        if (HasDirtyResource(dirtyResources, EditorDirtyResource.MaterialDescriptor))
            EnsureWritable(session.MaterialDescriptor, "Material descriptor");
        if (HasDirtyResource(dirtyResources, EditorDirtyResource.BiomeSettings))
            EnsureWritable(session.BiomeSettings, "Biome settings");

        var mapDefinition = new RuntimeMapDefinition
        {
            HeightmapPath = session.MapDefinitionModel.HeightmapPath,
            TerrainDataPath = session.MapDefinitionModel.TerrainDataPath,
            RiversPath = session.MapDefinitionModel.RiversPath,
            ProvincesPath = session.MapDefinitionModel.ProvincesPath,
            HeightScale = heightScale,
            RiverMinWidth = session.MapDefinitionModel.RiverMinWidth,
            RiverMaxWidth = session.MapDefinitionModel.RiverMaxWidth,
            RiverMaxVisibleCameraHeight = riverMaxVisibleCameraHeight ?? session.MapDefinitionModel.RiverMaxVisibleCameraHeight,
            SeaLevel = seaLevel ?? session.MapDefinitionModel.SeaLevel,
        };

        var mapDefinitionWriter = new MapDefinitionWriter();
        var heightmapWriter = new HeightmapWriter();
        var biomeMaskWriter = new BiomeMaskWriter();
        var materialDescriptorWriter = new MaterialDescriptorWriter();
        var biomeSettingsWriter = new BiomeSettingsWriter();

        using var transaction = new AtomicResourceWriteTransaction();
        if (HasDirtyResource(dirtyResources, EditorDirtyResource.MapDefinition))
        {
            string stagedMapDefinition = transaction.CreateStagingPath(session.MapDefinition.ResolvedPath);
            progress?.Report(AuthoringSaveProgress.Running(3, AuthoringSaveProgress.TotalSteps, "Writing map definition..."));
            mapDefinitionWriter.Write(stagedMapDefinition, mapDefinition);
        }

        if (HasDirtyResource(dirtyResources, EditorDirtyResource.Heightmap))
        {
            ArgumentNullException.ThrowIfNull(heightData);
            string stagedHeightmap = transaction.CreateStagingPath(session.Heightmap.ResolvedPath);
            progress?.Report(AuthoringSaveProgress.Running(4, AuthoringSaveProgress.TotalSteps, "Writing heightmap PNG..."));
            heightmapWriter.Write(stagedHeightmap, heightData, width, height);
        }

        if (HasDirtyResource(dirtyResources, EditorDirtyResource.BiomeMask))
        {
            ArgumentNullException.ThrowIfNull(biomeMask);
            string stagedBiomeMask = transaction.CreateStagingPath(session.BiomeMask.ResolvedPath);
            progress?.Report(AuthoringSaveProgress.Running(5, AuthoringSaveProgress.TotalSteps, "Writing biome mask PNG..."));
            biomeMaskWriter.Write(stagedBiomeMask, biomeMask);
        }

        if (HasDirtyResource(dirtyResources, EditorDirtyResource.MaterialDescriptor))
        {
            ArgumentNullException.ThrowIfNull(descriptorSlots);
            string stagedMaterialDescriptor = transaction.CreateStagingPath(session.MaterialDescriptor.ResolvedPath);
            progress?.Report(AuthoringSaveProgress.Running(6, AuthoringSaveProgress.TotalSteps, "Writing material descriptor..."));
            materialDescriptorWriter.Write(stagedMaterialDescriptor, descriptorSlots);
        }

        if (HasDirtyResource(dirtyResources, EditorDirtyResource.BiomeSettings))
        {
            ArgumentNullException.ThrowIfNull(biomeSnapshot);
            string stagedBiomeSettings = transaction.CreateStagingPath(session.BiomeSettings.ResolvedPath);
            progress?.Report(AuthoringSaveProgress.Running(7, AuthoringSaveProgress.TotalSteps, "Writing biome settings..."));
            biomeSettingsWriter.Write(stagedBiomeSettings, biomeSnapshot.Biomes, biomeSnapshot.Layers, biomeSnapshot.Modifiers);
        }

        progress?.Report(AuthoringSaveProgress.Running(8, AuthoringSaveProgress.TotalSteps, "Committing staged resources..."));
        transaction.Commit();
    }

    private static bool HasDirtyResource(EditorDirtyResource dirtyResources, EditorDirtyResource resource)
    {
        return (dirtyResources & resource) != 0;
    }

    private static void EnsureWritable(ResolvedGameResource resource, string displayName)
    {
        if (!resource.IsWritable)
            throw new InvalidOperationException($"{displayName} target is read-only: {resource.ResolvedPath}");
    }
}
