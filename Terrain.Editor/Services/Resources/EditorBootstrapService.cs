#nullable enable

using System;
using System.IO;
using Terrain.Resources;

namespace Terrain.Editor.Services.Resources;

public sealed class EditorBootstrapService
{
    private const string MapDataDirectory = "map_data";
    private const string MapDefinitionPath = "map_data/default.toml";
    private const string BiomeMaskPath = "map_data/biome_mask.png";
    private const string BiomeSettingsPath = "map_data/biome_settings.toml";
    private const string MaterialDescriptorPath = "map_data/materials/descriptor.toml";

    public EditorResourceSession LoadCurrentSession(string? appDirectory = null)
    {
        string effectiveAppDirectory = appDirectory ?? AppContext.BaseDirectory;
        var resolver = GameResourceResolverBootstrap.CreateForAppDirectory(effectiveAppDirectory);
        ResolvedGameResource mapDefinitionResource = resolver.ResolveRequiredFile(MapDefinitionPath);
        RuntimeMapDefinition mapDefinition = RuntimeMapDefinitionReader.ReadFrom(mapDefinitionResource.ResolvedPath);

        ResolvedGameResource heightmap = resolver.ResolveRequiredFile(ToMapDataVirtualPath(mapDefinition.HeightmapPath));
        ResolvedGameResource terrainData = resolver.ResolveWritableTarget(ToMapDataVirtualPath(mapDefinition.TerrainDataPath));
        ResolvedGameResource biomeMask = resolver.ResolveWritableTarget(BiomeMaskPath);
        ResolvedGameResource biomeSettings = resolver.ResolveRequiredFile(BiomeSettingsPath);
        ResolvedGameResource materialDescriptor = resolver.ResolveRequiredFile(MaterialDescriptorPath);
        ResolvedGameResource? rivers = ResolveOptional(resolver, mapDefinition.RiversPath);

        return new EditorResourceSession(
            mapDefinitionResource,
            heightmap,
            terrainData,
            biomeMask,
            biomeSettings,
            materialDescriptor,
            mapDefinition,
            rivers,
            hasDeclaredProvinces: mapDefinition.ProvincesPath != null);
    }

    private static ResolvedGameResource? ResolveOptional(GameResourceResolver resolver, string? mapDataRelativePath)
    {
        if (string.IsNullOrWhiteSpace(mapDataRelativePath))
            return null;

        try
        {
            return resolver.ResolveRequiredFile(ToMapDataVirtualPath(mapDataRelativePath));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static string ToMapDataVirtualPath(string relativePath)
    {
        return $"{MapDataDirectory}/{relativePath}";
    }
}
