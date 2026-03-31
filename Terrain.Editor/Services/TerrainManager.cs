#nullable enable
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using System;
using System.IO;
using System.Threading.Tasks;
using Terrain;
using TerrainPreProcessor.Models;
using TerrainPreProcessorTerrainProcessor = TerrainPreProcessor.Services.TerrainProcessor;

// Disambiguate types that exist in both Stride and ImageSharp
using HeightmapImage = SixLabors.ImageSharp.Image;
using StrideColor = Stride.Core.Mathematics.Color;

namespace Terrain.Editor.Services;

/// <summary>
/// Manages terrain entities in the editor scene.
/// Handles heightmap loading, terrain processing, and entity creation.
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
    private Entity? currentTerrainEntity;
    private Texture? defaultDiffuseTexture;
    private HeightmapInfo? currentHeightmapInfo;

    // CPU-side height data cache for brush preview and terrain queries
    private ushort[]? heightDataCache;
    private int heightDataWidth;
    private int heightDataHeight;

    // GPU sync support
    private TerrainRenderObject? terrainRenderObject;
    private CommandList? commandList;
    private int heightmapTileSize;
    private int heightmapTilePadding;

    /// <summary>
    /// The currently loaded terrain entity, if any.
    /// </summary>
    public Entity? CurrentTerrain => currentTerrainEntity;

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
    /// Loads a heightmap PNG and creates a terrain entity.
    /// </summary>
    /// <param name="heightmapPath">Path to the heightmap PNG file</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>The created terrain entity, or null on failure</returns>
    public async Task<Entity?> LoadTerrainAsync(
        string heightmapPath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        // Validate input
        if (!HeightmapLoader.IsValidHeightmap(heightmapPath))
        {
            Log.Error($"Invalid heightmap file: {heightmapPath}");
            return null;
        }

        progress?.Report((0, 100, "Validating heightmap..."));
        var info = HeightmapLoader.LoadHeightmapInfo(heightmapPath);
        if (info == null)
        {
            Log.Error($"Failed to load heightmap info: {heightmapPath}");
            return null;
        }

        // Remove existing terrain (before loading new height cache)
        if (currentTerrainEntity != null)
        {
            RemoveCurrentTerrain();
        }

        // Load height data cache for raycasting (after removing old terrain)
        LoadHeightDataCache(heightmapPath);

        try
        {
            // Process heightmap to .terrain format
            progress?.Report((10, 100, "Processing heightmap..."));
            string terrainPath = await Task.Run(() => ProcessHeightmapToTerrain(heightmapPath, progress));

            if (string.IsNullOrEmpty(terrainPath) || !File.Exists(terrainPath))
            {
                Log.Error("Failed to process heightmap to terrain format.");
                return null;
            }

            // Create terrain entity
            progress?.Report((90, 100, "Creating terrain entity..."));
            var entity = CreateTerrainEntity(terrainPath, info);

            // Add to scene
            scene.Entities.Add(entity);
            currentTerrainEntity = entity;
            currentHeightmapInfo = info;

            progress?.Report((100, 100, "Terrain loaded successfully."));

            TerrainLoaded?.Invoke(this, new TerrainLoadedEventArgs
            {
                Entity = entity,
                Width = info.Width,
                Height = info.Height,
                TerrainPath = terrainPath
            });

            return entity;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load terrain: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Removes the current terrain from the scene.
    /// </summary>
    public void RemoveCurrentTerrain()
    {
        if (currentTerrainEntity != null)
        {
            scene.Entities.Remove(currentTerrainEntity);
            currentTerrainEntity = null;
            currentHeightmapInfo = null;
            heightDataCache = null;  // Clear height cache
        }
    }

    /// <summary>
    /// Gets the terrain bounds for camera positioning.
    /// Returns bounds based on heightmap dimensions with default height range.
    /// </summary>
    public BoundingBox GetTerrainBounds()
    {
        if (currentHeightmapInfo == null)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);

        var info = currentHeightmapInfo;
        float maxHeight = DefaultHeightScale;

        return new BoundingBox(
            new Vector3(0, 0, 0),
            new Vector3(
                info.Width - 1,
                maxHeight,
                info.Height - 1));
    }

    /// <summary>
    /// Gets the terrain height at a world position using nearest-neighbor sampling.
    /// Matches the shader's SampleHeightAtLocalPos behavior (no interpolation).
    /// </summary>
    /// <param name="worldX">World X coordinate</param>
    /// <param name="worldZ">World Z coordinate</param>
    /// <returns>Height in world units, or null if position is outside terrain</returns>
    public float? GetHeightAtPosition(float worldX, float worldZ)
    {
        if (heightDataCache == null || currentHeightmapInfo == null)
            return null;

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
    /// Sets the GPU sync context for uploading height modifications.
    /// Must be called after terrain is loaded with the render object from TerrainProcessor.
    /// </summary>
    public void SetGpuSyncContext(TerrainRenderObject renderObject, CommandList cmdList, int tileSize, int tilePadding)
    {
        terrainRenderObject = renderObject;
        commandList = cmdList;
        heightmapTileSize = tileSize;
        heightmapTilePadding = tilePadding;
    }

    /// <summary>
    /// Immediately syncs modified height data to GPU.
    /// Per D-04: Immediate sync after CPU cache modification.
    /// Per D-06: Uses Texture.SetData for upload.
    /// </summary>
    /// <param name="sliceIndex">The texture array slice to update (default 0 for first slice)</param>
    /// <remarks>
    /// This uploads the entire heightmap cache to GPU. For Phase 3, this is acceptable
    /// since typical heightmaps are under 4K resolution. Future optimization could
    /// implement region-based dirty tracking.
    /// </remarks>
    public void UpdateHeightData(int sliceIndex = 0)
    {
        if (heightDataCache == null || terrainRenderObject == null || commandList == null)
            return;

        // D-04, D-06: Immediate sync using Texture.SetData
        terrainRenderObject.UploadHeightmapSlice(
            commandList,
            heightDataCache,
            sliceIndex,
            heightDataWidth,
            heightDataHeight,
            heightmapTileSize,
            heightmapTilePadding);
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

    private string ProcessHeightmapToTerrain(
        string heightmapPath,
        IProgress<(int current, int total, string message)>? progress)
    {
        // Generate output path in temp directory
        string tempDir = Path.Combine(Path.GetTempPath(), "TerrainEditor");
        Directory.CreateDirectory(tempDir);
        string outputPath = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(heightmapPath)}.terrain");

        // Create processing config
        var config = new ProcessingConfig
        {
            HeightMapPath = heightmapPath,
            OutputPath = outputPath,
            LeafNodeSize = DefaultLeafNodeSize,
            TileSize = DefaultTileSize
        };

        // Process using TerrainPreProcessor logic
        var result = TerrainPreProcessorTerrainProcessor.Process(config, progress);

        if (result.IsFailure)
        {
            Log.Error($"Terrain processing failed: {result.ErrorMessage}");
            return string.Empty;
        }

        return outputPath;
    }

    private Entity CreateTerrainEntity(string terrainPath, HeightmapInfo info)
    {
        var entity = new Entity("Terrain");

        var terrain = new TerrainComponent
        {
            TerrainDataPath = terrainPath,
            HeightScale = DefaultHeightScale,
            DefaultDiffuseTexture = GetOrCreateDefaultDiffuseTexture()
        };

        entity.Add(terrain);
        return entity;
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
        heightDataCache = null;  // Clear height cache
    }
}

public sealed class TerrainLoadedEventArgs : EventArgs
{
    public required Entity Entity { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string TerrainPath { get; init; }
}
