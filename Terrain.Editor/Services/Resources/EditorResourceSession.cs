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
        bool hasPendingHeightmap = false,
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
        HasPendingHeightmap = hasPendingHeightmap;
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
    public bool HasPendingHeightmap { get; }
    public bool HasPendingResources => HasPendingHeightmap;
    public string? PendingHeightmapPath => HasPendingHeightmap ? Heightmap.ResolvedPath : null;
    public bool CanSaveAuthoringResources => !HasPendingHeightmap;
    public bool CanExportTerrainData => !HasPendingHeightmap;
    public float HeightScale => MapDefinitionModel.HeightScale;
}
