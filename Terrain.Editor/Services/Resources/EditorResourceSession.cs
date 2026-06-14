#nullable enable

using Terrain.Resources;

namespace Terrain.Editor.Services.Resources;

public sealed class EditorResourceSession
{
    public EditorResourceSession(
        ResolvedGameResource mapDefinition,
        ResolvedGameResource heightmap,
        ResolvedGameResource terrainData,
        ResolvedGameResource biomeMask,
        ResolvedGameResource biomeSettings,
        ResolvedGameResource materialDescriptor,
        RuntimeMapDefinition mapDefinitionModel,
        ResolvedGameResource? rivers = null,
        bool hasDeclaredProvinces = false)
    {
        MapDefinition = mapDefinition;
        Heightmap = heightmap;
        TerrainData = terrainData;
        BiomeMask = biomeMask;
        BiomeSettings = biomeSettings;
        MaterialDescriptor = materialDescriptor;
        MapDefinitionModel = mapDefinitionModel;
        Rivers = rivers;
        HasDeclaredProvinces = hasDeclaredProvinces;
    }

    public ResolvedGameResource MapDefinition { get; }
    public ResolvedGameResource Heightmap { get; }
    public ResolvedGameResource TerrainData { get; }
    public ResolvedGameResource BiomeMask { get; }
    public ResolvedGameResource BiomeSettings { get; }
    public ResolvedGameResource MaterialDescriptor { get; }
    public ResolvedGameResource? Rivers { get; }
    public RuntimeMapDefinition MapDefinitionModel { get; }
    public bool HasDeclaredProvinces { get; }
    public float HeightScale => MapDefinitionModel.HeightScale;
}
