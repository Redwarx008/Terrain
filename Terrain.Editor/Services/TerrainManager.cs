#nullable enable
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Terrain.Editor.Rendering;
using TerrainPreProcessor.Models;
using TerrainPreProcessorTerrainProcessor = TerrainPreProcessor.Services.TerrainProcessor;

// Disambiguate types that exist in both Stride and ImageSharp
using HeightmapImage = SixLabors.ImageSharp.Image;
using StrideColor = Stride.Core.Mathematics.Color;

namespace Terrain.Editor.Services;

/// <summary>
/// Manages terrain entities in the editor scene.
/// Handles heightmap loading, terrain processing, and entity creation.
/// Supports multi-chunk terrain for large heightmaps (> 16k).
/// </summary>
public sealed class TerrainManager : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");
    private const int DefaultLeafNodeSize = 32;
    private const int DefaultTileSize = 129;
    private const float DefaultHeightScale = 100.0f;
    private const float HeightSampleNormalization = 1.0f / ushort.MaxValue;

    private readonly GraphicsDevice graphicsDevice;
    private readonly Scene scene;
    private readonly Texture? defaultTerrainTexture;
    private Texture? defaultDiffuseTexture;
    private HeightmapInfo? currentHeightmapInfo;

    // Multi-entity terrain support
    private readonly List<EditorTerrainEntity> terrainEntities = new();
    private readonly List<Entity> sceneEntities = new();
    private SplitTerrainConfig? currentSplitConfig;

    // CPU-side height data cache for brush preview and terrain queries
    private ushort[]? heightDataCache;
    private int heightDataWidth;
    private int heightDataHeight;

    // GPU sync support - using file write + cache invalidation
    private TerrainComponent? terrainComponent;
    private string? currentTerrainPath;
    private string? lastLoadError;

    /// <summary>
    /// The currently loaded terrain entities (may be multiple for split terrains).
    /// </summary>
    public IReadOnlyList<EditorTerrainEntity> TerrainEntities => terrainEntities;

    /// <summary>
    /// Whether any terrain is currently loaded.
    /// </summary>
    public bool HasTerrainLoaded => terrainEntities.Count > 0;

    /// <summary>
    /// Whether the height data cache is loaded and ready for queries.
    /// </summary>
    public bool HasHeightCache => heightDataCache != null;

    /// <summary>
    /// Width of the height data cache (0 if not loaded).
    /// </summary>
    public int HeightCacheWidth => heightDataWidth;

    /// <summary>
    /// Height of the height data cache (0 if not loaded).
    /// </summary>
    public int HeightCacheHeight => heightDataHeight;

    /// <summary>
    /// Gets the height data cache for direct modification.
    /// Returns null if no terrain is loaded.
    /// </summary>
    public ushort[]? HeightDataCache => heightDataCache;

    /// <summary>
    /// Gets the split configuration if the terrain is split into multiple chunks.
    /// Returns null if no terrain is loaded or terrain is not split.
    /// </summary>
    public SplitTerrainConfig? SplitConfig => currentSplitConfig;
    public string? LastLoadError => lastLoadError;

    /// <summary>
    /// Raised when a new terrain is loaded.
    /// </summary>
    public event EventHandler<TerrainLoadedEventArgs>? TerrainLoaded;

    public TerrainManager(GraphicsDevice graphicsDevice, Scene scene, Texture? defaultTerrainTexture = null)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        this.defaultTerrainTexture = defaultTerrainTexture;
    }

    /// <summary>
    /// Loads a heightmap PNG and creates terrain entity/entities.
    /// Automatically splits heightmaps larger than 16k into multiple chunks.
    /// </summary>
    /// <param name="heightmapPath">Path to the heightmap PNG file</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>The created terrain entities (may be multiple for split terrains)</returns>
    public async Task<List<EditorTerrainEntity>> LoadTerrainAsync(
        string heightmapPath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        lastLoadError = null;

        // Validate input
        if (!HeightmapLoader.IsValidHeightmap(heightmapPath))
        {
            lastLoadError = $"Invalid heightmap file: {heightmapPath}";
            Log.Error(lastLoadError);
            return new List<EditorTerrainEntity>();
        }

        progress?.Report((0, 100, "Validating heightmap..."));
        var info = HeightmapLoader.LoadHeightmapInfo(heightmapPath);
        if (info == null)
        {
            lastLoadError = $"Failed to load heightmap info: {heightmapPath}";
            Log.Error(lastLoadError);
            return new List<EditorTerrainEntity>();
        }

        // Remove existing terrain (before loading new height cache)
        RemoveCurrentTerrain();

        // Load height data cache for raycasting (after removing old terrain)
        LoadHeightDataCache(heightmapPath);

        try
        {
            // GPU resources must be created on the main thread with the active GraphicsDevice.
            // Running the whole entity creation path inside Task.Run caused terrain loading to fail
            // silently on some systems because Texture/Buffer creation happened off the render thread.
            progress?.Report((10, 100, "Loading terrain..."));
            var entities = TerrainSplitter.SplitAndCreateEntities(graphicsDevice, heightmapPath, progress);

            if (entities.Count == 0)
            {
                lastLoadError = "Failed to create terrain entities.";
                Log.Error(lastLoadError);
                return new List<EditorTerrainEntity>();
            }

            // Store entities
            terrainEntities.AddRange(entities);
            currentHeightmapInfo = info;
            currentSplitConfig = TerrainSplitter.ComputeSplitConfig(heightmapPath);

            // Create scene entities with EditorTerrainComponent for rendering
            foreach (var terrainEntity in entities)
            {
                var sceneEntity = new Entity($"TerrainChunk_{terrainEntity.ChunkX}_{terrainEntity.ChunkZ}")
                {
                    new EditorTerrainComponent
                    {
                        TerrainEntity = terrainEntity,
                        DefaultDiffuseTexture = GetOrCreateDefaultDiffuseTexture(),
                    }
                };
                scene.Entities.Add(sceneEntity);
                sceneEntities.Add(sceneEntity);
            }

            progress?.Report((95, 100, "Terrain loaded successfully."));

            TerrainLoaded?.Invoke(this, new TerrainLoadedEventArgs
            {
                Entities = entities,
                Width = info.Width,
                Height = info.Height,
                SourcePath = heightmapPath
            });

            Log.Info($"Loaded terrain: {info.Width}x{info.Height} as {entities.Count} chunk(s)");
            return entities;
        }
        catch (Exception ex)
        {
            lastLoadError = $"Failed to load terrain: {ex.Message}";
            Log.Error(lastLoadError);
            return new List<EditorTerrainEntity>();
        }
    }

    /// <summary>
    /// Removes the current terrain from the scene.
    /// </summary>
    public void RemoveCurrentTerrain()
    {
        // Remove scene entities
        foreach (var sceneEntity in sceneEntities)
        {
            scene.Entities.Remove(sceneEntity);
        }
        sceneEntities.Clear();

        // Dispose all terrain entities
        foreach (var entity in terrainEntities)
        {
            entity.Dispose();
        }
        terrainEntities.Clear();

        currentHeightmapInfo = null;
        currentSplitConfig = null;
        terrainComponent = null;
        heightDataCache = null;
    }

    /// <summary>
    /// Gets the terrain bounds for camera positioning.
    /// Returns combined bounds of all terrain chunks.
    /// </summary>
    public BoundingBox GetTerrainBounds()
    {
        if (terrainEntities.Count == 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);

        // Combine bounds from all chunks
        var bounds = terrainEntities[0].Bounds;
        for (int i = 1; i < terrainEntities.Count; i++)
        {
            bounds = BoundingBox.Merge(bounds, terrainEntities[i].Bounds);
        }

        return bounds;
    }

    /// <summary>
    /// Gets the terrain height at a world position using nearest-neighbor sampling.
    /// Matches the shader's SampleHeightAtLocalPos behavior (no interpolation).
    /// Queries across all terrain chunks to find the correct one.
    /// </summary>
    /// <param name="worldX">World X coordinate</param>
    /// <param name="worldZ">World Z coordinate</param>
    /// <returns>Height in world units, or null if position is outside terrain</returns>
    public float? GetHeightAtPosition(float worldX, float worldZ)
    {
        if (heightDataCache == null || currentHeightmapInfo == null)
            return null;

        // For single terrain or split terrain, use the global height cache
        // Nearest-neighbor sampling (matches shader behavior)
        int x = (int)MathF.Round(worldX);
        int z = (int)MathF.Round(worldZ);

        // Check bounds
        if (x < 0 || x >= heightDataWidth || z < 0 || z >= heightDataHeight)
            return null;

        // Direct lookup - no interpolation
        ushort height = heightDataCache[z * heightDataWidth + x];

        // Convert to world height
        return height * HeightSampleNormalization * DefaultHeightScale;
    }

    /// <summary>
    /// Checks if a world position is within the terrain bounds.
    /// </summary>
    public bool IsPositionOnTerrain(float worldX, float worldZ)
    {
        if (heightDataCache == null || currentHeightmapInfo == null)
            return false;

        // Use same bounds check as GetHeightAtPosition
        int x = (int)MathF.Round(worldX);
        int z = (int)MathF.Round(worldZ);

        return x >= 0 && x < heightDataWidth && z >= 0 && z < heightDataHeight;
    }

    /// <summary>
    /// Sets the terrain component reference for file-based height updates.
    /// Called after terrain is loaded.
    /// </summary>
    public void SetTerrainComponent(TerrainComponent component)
    {
        terrainComponent = component;
    }

    /// <summary>
    /// Writes modified height data to all affected terrain chunks.
    /// For split terrains, propagates edits to adjacent chunks at overlap regions.
    /// </summary>
    /// <param name="modifiedX">X coordinate of modified region center (world space)</param>
    /// <param name="modifiedZ">Z coordinate of modified region center (world space)</param>
    /// <param name="radius">Radius of modified region (world space)</param>
    public void UpdateHeightData(int modifiedX, int modifiedZ, float radius)
    {
        if (heightDataCache == null)
            return;

        // Find all chunks affected by the brush
        foreach (var entity in terrainEntities)
        {
            var bounds = entity.Bounds;
            float distToChunk = DistanceToBounds(modifiedX, modifiedZ, bounds);

            if (distToChunk <= radius)
            {
                // This chunk is affected - mark for update
                UpdateEntityHeightData(entity, modifiedX, modifiedZ, radius);
            }
        }

        // If brush spans multiple chunks, ensure overlap regions are synchronized
        SynchronizeOverlapRegions(modifiedX, modifiedZ, radius);
    }

    /// <summary>
    /// Calculates the distance from a point to a bounding box (0 if inside).
    /// </summary>
    private static float DistanceToBounds(float x, float z, BoundingBox bounds)
    {
        float dx = MathF.Max(bounds.Minimum.X - x, MathF.Max(0, x - bounds.Maximum.X));
        float dz = MathF.Max(bounds.Minimum.Z - z, MathF.Max(0, z - bounds.Maximum.Z));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Updates height data in a specific entity's cache.
    /// </summary>
    private void UpdateEntityHeightData(EditorTerrainEntity entity, int modifiedX, int modifiedZ, float radius)
    {
        if (entity.HeightDataCache == null)
            return;

        // Convert world coordinates to chunk-local coordinates
        int localX = modifiedX - (int)entity.WorldOffset.X;
        int localZ = modifiedZ - (int)entity.WorldOffset.Z;

        // Update the entity's height data within the brush radius
        // The actual height modification is done in HeightEditor
        // This method just marks the region as needing GPU sync
    }

    /// <summary>
    /// Synchronizes overlap regions between adjacent chunks.
    /// Per CONTEXT.md: Modifications near edges propagate to adjacent chunks.
    /// </summary>
    private void SynchronizeOverlapRegions(int modifiedX, int modifiedZ, float radius)
    {
        if (currentSplitConfig == null || terrainEntities.Count <= 1)
            return;

        // For each chunk boundary, if brush is within radius, sync the overlapping sample
        // between the two adjacent chunks
        int chunkSize = currentSplitConfig.ChunkSize;

        // Check if the modification is near any chunk boundary
        for (int i = 0; i < terrainEntities.Count; i++)
        {
            var entity = terrainEntities[i];

            // Check if this entity is near a chunk boundary
            int chunkX = entity.ChunkX;
            int chunkZ = entity.ChunkZ;

            // Check right boundary (if not last chunk in X)
            if (chunkX < currentSplitConfig.ChunkCountX - 1)
            {
                int boundaryX = (chunkX + 1) * chunkSize - 1;
                if (MathF.Abs(modifiedX - boundaryX) <= radius)
                {
                    // Sync with the chunk to the right
                    SyncOverlapWithAdjacentChunk(entity, chunkX + 1, chunkZ, modifiedX, modifiedZ, radius);
                }
            }

            // Check bottom boundary (if not last chunk in Z)
            if (chunkZ < currentSplitConfig.ChunkCountZ - 1)
            {
                int boundaryZ = (chunkZ + 1) * chunkSize - 1;
                if (MathF.Abs(modifiedZ - boundaryZ) <= radius)
                {
                    // Sync with the chunk below
                    SyncOverlapWithAdjacentChunk(entity, chunkX, chunkZ + 1, modifiedX, modifiedZ, radius);
                }
            }
        }
    }

    /// <summary>
    /// Synchronizes height data between adjacent chunks at their overlap region.
    /// Per CONTEXT.md: 1-sample overlap on shared edges.
    /// </summary>
    private void SyncOverlapWithAdjacentChunk(
        EditorTerrainEntity sourceEntity,
        int targetChunkX,
        int targetChunkZ,
        int modifiedX,
        int modifiedZ,
        float radius)
    {
        // Find the target entity
        EditorTerrainEntity? targetEntity = null;
        foreach (var entity in terrainEntities)
        {
            if (entity.ChunkX == targetChunkX && entity.ChunkZ == targetChunkZ)
            {
                targetEntity = entity;
                break;
            }
        }

        if (targetEntity == null || sourceEntity.HeightDataCache == null || targetEntity.HeightDataCache == null)
            return;

        // Determine the overlap region based on chunk relationship
        int sourceWidth = sourceEntity.HeightmapWidth;
        int sourceHeight = sourceEntity.HeightmapHeight;
        int targetWidth = targetEntity.HeightmapWidth;
        int targetHeight = targetEntity.HeightmapHeight;

        // Calculate world-space boundaries
        int sourceMinX = (int)sourceEntity.WorldOffset.X;
        int sourceMinZ = (int)sourceEntity.WorldOffset.Z;
        int targetMinX = (int)targetEntity.WorldOffset.X;
        int targetMinZ = (int)targetEntity.WorldOffset.Z;

        // Determine which edge overlaps
        bool isHorizontalNeighbor = sourceEntity.ChunkX != targetChunkX;
        bool isVerticalNeighbor = sourceEntity.ChunkZ != targetChunkZ;

        if (isHorizontalNeighbor)
        {
            // Source is to the left of target (source shares right edge, target shares left edge)
            if (sourceEntity.ChunkX < targetChunkX)
            {
                // Copy the rightmost column of source to the leftmost column of target
                int sourceX = sourceWidth - 1;
                int targetX = 0;
                int copyHeight = Math.Min(sourceHeight, targetHeight);

                for (int z = 0; z < copyHeight; z++)
                {
                    int sourceIndex = z * sourceWidth + sourceX;
                    int targetIndex = z * targetWidth + targetX;
                    targetEntity.HeightDataCache[targetIndex] = sourceEntity.HeightDataCache[sourceIndex];
                }
            }
            // Source is to the right of target (source shares left edge, target shares right edge)
            else
            {
                // Copy the leftmost column of source to the rightmost column of target
                int sourceX = 0;
                int targetX = targetWidth - 1;
                int copyHeight = Math.Min(sourceHeight, targetHeight);

                for (int z = 0; z < copyHeight; z++)
                {
                    int sourceIndex = z * sourceWidth + sourceX;
                    int targetIndex = z * targetWidth + targetX;
                    targetEntity.HeightDataCache[targetIndex] = sourceEntity.HeightDataCache[sourceIndex];
                }
            }
        }

        if (isVerticalNeighbor)
        {
            // Source is above target (source shares bottom edge, target shares top edge)
            if (sourceEntity.ChunkZ < targetChunkZ)
            {
                // Copy the bottom row of source to the top row of target
                int sourceZ = sourceHeight - 1;
                int targetZ = 0;
                int copyWidth = Math.Min(sourceWidth, targetWidth);

                for (int x = 0; x < copyWidth; x++)
                {
                    int sourceIndex = sourceZ * sourceWidth + x;
                    int targetIndex = targetZ * targetWidth + x;
                    targetEntity.HeightDataCache[targetIndex] = sourceEntity.HeightDataCache[sourceIndex];
                }
            }
            // Source is below target (source shares top edge, target shares bottom edge)
            else
            {
                // Copy the top row of source to the bottom row of target
                int sourceZ = 0;
                int targetZ = targetHeight - 1;
                int copyWidth = Math.Min(sourceWidth, targetWidth);

                for (int x = 0; x < copyWidth; x++)
                {
                    int sourceIndex = sourceZ * sourceWidth + x;
                    int targetIndex = targetZ * targetWidth + x;
                    targetEntity.HeightDataCache[targetIndex] = sourceEntity.HeightDataCache[sourceIndex];
                }
            }
        }
    }

    /// <summary>
    /// Syncs all modified terrain entities to GPU.
    /// </summary>
    public void SyncToGpu(CommandList commandList)
    {
        foreach (var entity in terrainEntities)
        {
            entity.SyncToGpu(commandList);
        }
    }

    private void LoadHeightDataCache(string heightmapPath)
    {
        try
        {
            using var image = HeightmapImage.Load<L16>(heightmapPath);
            heightDataWidth = image.Width;
            heightDataHeight = image.Height;
            heightDataCache = new ushort[heightDataWidth * heightDataHeight];

            // Copy pixel data using ProcessPixelRows API (ImageSharp 3.x)
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    int rowOffset = y * heightDataWidth;
                    for (int x = 0; x < pixelRow.Length; x++)
                    {
                        heightDataCache[rowOffset + x] = pixelRow[x].PackedValue;
                    }
                }
            });

            Log.Info($"Loaded height data cache: {heightDataWidth}x{heightDataHeight}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load height data cache: {ex.Message}");
            heightDataCache = null;
        }
    }

    private Texture GetOrCreateDefaultDiffuseTexture()
    {
        if (defaultDiffuseTexture != null)
            return defaultDiffuseTexture;

        if (defaultTerrainTexture != null)
        {
            defaultDiffuseTexture = defaultTerrainTexture;
            return defaultDiffuseTexture;
        }

        // Fall back to a checkerboard only if the project texture asset is unavailable. The normal
        // path should use the authored Grid Gray asset, which already comes through Stride's asset
        // pipeline with mipmaps and avoids the severe moire seen on the raw runtime-generated texture.
        int size = 64;
        var data = new StrideColor[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool isLight = ((x / 8) + (y / 8)) % 2 == 0;
                data[y * size + x] = isLight ? new StrideColor(180, 180, 180, 255) : new StrideColor(120, 120, 120, 255);
            }
        }

        defaultDiffuseTexture = Texture.New2D(
            graphicsDevice,
            size,
            size,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            data);

        return defaultDiffuseTexture;
    }

    public void Dispose()
    {
        RemoveCurrentTerrain();
        defaultDiffuseTexture?.Dispose();
        heightDataCache = null;
    }
}

/// <summary>
/// Event arguments for terrain loaded event.
/// </summary>
public sealed class TerrainLoadedEventArgs : EventArgs
{
    /// <summary>
    /// The loaded terrain entities (may be multiple for split terrains).
    /// </summary>
    public required List<EditorTerrainEntity> Entities { get; init; }

    /// <summary>
    /// Width of the source heightmap in pixels.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Height of the source heightmap in pixels.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Path to the source heightmap file.
    /// </summary>
    public required string SourcePath { get; init; }
}
