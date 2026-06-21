#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Terrain.Resources;

public sealed class GameRuntimeResourceBootstrap
{
    private const string MapDataDirectory = "map";
    private const string DefaultMapDefinitionPath = "map/default.toml";
    private const string BiomeMaskPath = "map/biome_mask.png";
    private const string BiomeSettingsPath = "map/biome_settings.toml";
    private const string MaterialDescriptorPath = "map/materials/descriptor.toml";

    private readonly GameResourceResolver resolver;

    public GameRuntimeResourceBootstrap(GameResourceResolver resolver)
    {
        this.resolver = resolver;
    }

    public TerrainRuntimeResourceBundle Load()
    {
        ResolvedGameResource mapDefinitionResource = resolver.ResolveRequiredFile(DefaultMapDefinitionPath);
        RuntimeMapDefinition mapDefinition = RuntimeMapDefinitionReader.ReadFrom(
            mapDefinitionResource.ResolvedPath,
            requireHeightmap: false);

        ResolvedGameResource terrainDataResource = resolver.ResolveRequiredFile(ToMapDataVirtualPath(mapDefinition.TerrainDataPath));
        ResolvedGameResource biomeMaskResource = resolver.ResolveRequiredFile(BiomeMaskPath);
        ResolvedGameResource materialDescriptorResource = resolver.ResolveRequiredFile(MaterialDescriptorPath);
        RuntimeMaterialDescriptor materialDescriptor = RuntimeMaterialDescriptorReader.ReadFrom(materialDescriptorResource.ResolvedPath);
        List<RuntimeMaterialTextureSlot> materialTextureSlots = ResolveMaterialTextureSlots(materialDescriptor);

        ResolvedGameResource biomeSettingsResource = resolver.ResolveRequiredFile(BiomeSettingsPath);
        var materialIds = new HashSet<string>(materialDescriptor.Materials.Select(material => material.Id), StringComparer.Ordinal);
        RuntimeBiomeSettings biomeSettings = RuntimeBiomeSettingsReader.ReadFrom(biomeSettingsResource.ResolvedPath, materialIds);

        string? riversPath = null;
        var diagnostics = new List<string>();
        if (mapDefinition.RiversPath != null)
        {
            string riversVirtualPath = ToMapDataVirtualPath(mapDefinition.RiversPath);
            try
            {
                riversPath = resolver.ResolveRequiredFile(riversVirtualPath).ResolvedPath;
            }
            catch (FileNotFoundException)
            {
                diagnostics.Add($"rivers resource was declared but not found: {riversVirtualPath}");
            }
        }

        bool hasDeclaredProvinces = mapDefinition.ProvincesPath != null;
        if (hasDeclaredProvinces)
            diagnostics.Add("provinces resource is declared but not implemented in runtime bootstrap v1.");

        return new TerrainRuntimeResourceBundle
        {
            TerrainDataPath = terrainDataResource.ResolvedPath,
            BiomeMaskPath = biomeMaskResource.ResolvedPath,
            BiomeSettingsPath = biomeSettingsResource.ResolvedPath,
            MaterialDescriptorPath = materialDescriptorResource.ResolvedPath,
            MaterialsDirectory = Path.GetFullPath(Path.GetDirectoryName(materialDescriptorResource.ResolvedPath)!),
            RiversPath = riversPath,
            HasDeclaredProvinces = hasDeclaredProvinces,
            HeightScale = mapDefinition.HeightScale,
            RiverMinWidth = mapDefinition.RiverMinWidth,
            RiverMaxWidth = mapDefinition.RiverMaxWidth,
            MaterialDescriptor = materialDescriptor,
            MaterialTextureSlots = materialTextureSlots,
            BiomeSettings = biomeSettings,
            Diagnostics = diagnostics,
        };
    }

    private List<RuntimeMaterialTextureSlot> ResolveMaterialTextureSlots(RuntimeMaterialDescriptor descriptor)
    {
        var slots = new List<RuntimeMaterialTextureSlot>(descriptor.Materials.Count);
        foreach (RuntimeMaterialEntry material in descriptor.Materials)
        {
            slots.Add(new RuntimeMaterialTextureSlot
            {
                Id = material.Id,
                Index = material.Index,
                Name = material.Name,
                AlbedoPath = ResolveOptionalMaterialTexture(material.AlbedoPath),
                NormalPath = ResolveOptionalMaterialTexture(material.NormalPath),
                PropertiesPath = ResolveOptionalMaterialTexture(material.PropertiesPath),
            });
        }

        return slots;
    }

    private string? ResolveOptionalMaterialTexture(string? relativePath)
    {
        if (relativePath == null)
            return null;

        return resolver.ResolveRequiredFile($"map/materials/{relativePath}").ResolvedPath;
    }

    private static string ToMapDataVirtualPath(string relativePath)
    {
        return $"{MapDataDirectory}/{relativePath}";
    }
}
