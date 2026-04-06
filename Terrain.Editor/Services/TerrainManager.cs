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
using StrideColor = Stride.Core.Mathematics.Color;
using HeightmapImage = SixLabors.ImageSharp.Image;

namespace Terrain.Editor.Services;

/// <summary>
/// Manages the editor terrain scene object and the shared CPU height cache.
/// Large heightmaps use sliced height textures internally, but still appear as one logical terrain.
/// </summary>
public sealed class TerrainManager : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");
    private const int DefaultLeafNodeSize = 32;
    private const float DefaultHeightScale = 100.0f;
    private const float HeightSampleNormalization = 1.0f / ushort.MaxValue;

    private readonly GraphicsDevice graphicsDevice;
    private readonly Scene scene;
    private readonly Texture? defaultTerrainTexture;
    private Texture? defaultDiffuseTexture;
    private HeightmapInfo? currentHeightmapInfo;

    private readonly List<EditorTerrainEntity> terrainEntities = new();
    private readonly List<Entity> sceneEntities = new();
    private SplitTerrainConfig? currentSplitConfig;

    private ushort[]? heightDataCache;
    private int heightDataWidth;
    private int heightDataHeight;

    /// <summary>
    /// 材质索引图，存储每个像素的材质槽位索引。
    /// </summary>
    public MaterialIndexMap? MaterialIndices { get; private set; }

    private TerrainComponent? terrainComponent;
    private string? currentTerrainPath;
    private string? lastLoadError;

    public IReadOnlyList<EditorTerrainEntity> TerrainEntities => terrainEntities;
    public bool HasTerrainLoaded => terrainEntities.Count > 0;
    public bool HasHeightCache => heightDataCache != null;
    public int HeightCacheWidth => heightDataWidth;
    public int HeightCacheHeight => heightDataHeight;
    public ushort[]? HeightDataCache => heightDataCache;
    public SplitTerrainConfig? SplitConfig => currentSplitConfig;
    public string? LastLoadError => lastLoadError;

    public event EventHandler<TerrainLoadedEventArgs>? TerrainLoaded;

    /// <summary>
    /// 项目加载完成后触发，通知需要加载材质纹理。
    /// </summary>
    public event EventHandler? MaterialTexturesLoadRequired;

    public TerrainManager(GraphicsDevice graphicsDevice, Scene scene, Texture? defaultTerrainTexture = null)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        this.defaultTerrainTexture = defaultTerrainTexture;
    }

    public async Task<List<EditorTerrainEntity>> LoadTerrainAsync(
        string heightmapPath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        lastLoadError = null;

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

        RemoveCurrentTerrain();
        LoadHeightDataCache(heightmapPath);
        if (heightDataCache == null)
        {
            lastLoadError = $"Failed to load height data cache: {heightmapPath}";
            Log.Error(lastLoadError);
            return new List<EditorTerrainEntity>();
        }

        try
        {
            progress?.Report((10, 100, "Building logical terrain..."));
            currentSplitConfig = SplitTerrainConfig.Compute(heightDataWidth, heightDataHeight, DefaultLeafNodeSize);
            var terrainEntity = TerrainSplitter.CreateTerrainEntity(
                graphicsDevice,
                heightDataCache,
                heightDataWidth,
                heightDataHeight,
                progress,
                DefaultLeafNodeSize);

            if (terrainEntity == null)
            {
                lastLoadError = "Failed to create editor terrain.";
                Log.Error(lastLoadError);
                return new List<EditorTerrainEntity>();
            }

            terrainEntities.Add(terrainEntity);
            currentHeightmapInfo = info;
            currentTerrainPath = heightmapPath;

            // 初始化材质索引图
            MaterialIndices = new MaterialIndexMap(heightDataWidth, heightDataHeight);

            // 设置材质索引数据引用到实体
            terrainEntity.MaterialIndexData = MaterialIndices.GetRawData();

            var sceneEntity = new Entity("EditorTerrain")
            {
                new EditorTerrainComponent
                {
                    TerrainEntity = terrainEntity,
                    DefaultDiffuseTexture = GetOrCreateDefaultDiffuseTexture(),
                }
            };
            scene.Entities.Add(sceneEntity);
            sceneEntities.Add(sceneEntity);

            progress?.Report((95, 100, "Terrain loaded successfully."));

            var entities = new List<EditorTerrainEntity> { terrainEntity };
            TerrainLoaded?.Invoke(this, new TerrainLoadedEventArgs
            {
                Entities = entities,
                Width = info.Width,
                Height = info.Height,
                SourcePath = heightmapPath
            });

            Log.Info($"Loaded terrain: {info.Width}x{info.Height} as 1 logical terrain with {terrainEntity.Slices.Count} slice(s)");
            return entities;
        }
        catch (Exception ex)
        {
            lastLoadError = $"Failed to load terrain: {ex.Message}";
            Log.Error(lastLoadError);
            return new List<EditorTerrainEntity>();
        }
    }

    public void RemoveCurrentTerrain()
    {
        foreach (var sceneEntity in sceneEntities)
        {
            scene.Entities.Remove(sceneEntity);
        }
        sceneEntities.Clear();

        foreach (var entity in terrainEntities)
        {
            entity.Dispose();
        }
        terrainEntities.Clear();

        currentHeightmapInfo = null;
        currentSplitConfig = null;
        terrainComponent = null;
        currentTerrainPath = null;
        heightDataCache = null;
        heightDataWidth = 0;
        heightDataHeight = 0;
        MaterialIndices = null;
    }

    public BoundingBox GetTerrainBounds()
    {
        if (terrainEntities.Count == 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);

        return terrainEntities[0].Bounds;
    }

    public float? GetHeightAtPosition(float worldX, float worldZ)
    {
        if (heightDataCache == null || currentHeightmapInfo == null)
            return null;

        int x = (int)MathF.Round(worldX);
        int z = (int)MathF.Round(worldZ);
        if (x < 0 || x >= heightDataWidth || z < 0 || z >= heightDataHeight)
            return null;

        ushort height = heightDataCache[z * heightDataWidth + x];
        return height * HeightSampleNormalization * DefaultHeightScale;
    }

    /// <summary>
    /// 获取指定位置的原始高度值 (ushort 0-65535)。
    /// 用于 Flatten 等需要与 HeightData 数组直接比较的工具。
    /// </summary>
    public float? GetRawHeightAtPosition(float worldX, float worldZ)
    {
        if (heightDataCache == null || currentHeightmapInfo == null)
            return null;

        int x = (int)MathF.Round(worldX);
        int z = (int)MathF.Round(worldZ);
        if (x < 0 || x >= heightDataWidth || z < 0 || z >= heightDataHeight)
            return null;

        return heightDataCache[z * heightDataWidth + x];
    }

    public bool IsPositionOnTerrain(float worldX, float worldZ)
    {
        if (heightDataCache == null || currentHeightmapInfo == null)
            return false;

        int x = (int)MathF.Round(worldX);
        int z = (int)MathF.Round(worldZ);
        return x >= 0 && x < heightDataWidth && z >= 0 && z < heightDataHeight;
    }

    public void SetTerrainComponent(TerrainComponent component)
    {
        terrainComponent = component;
    }

    /// <summary>
    /// 标记指定通道的数据需要同步到 GPU。
    /// </summary>
    public void MarkDataDirty(TerrainDataChannel channel, int centerX = 0, int centerZ = 0, float radius = 0)
    {
        if (terrainEntities.Count == 0)
            return;

        terrainEntities[0].MarkDataDirty(channel, centerX, centerZ, radius);
    }

    public void UpdateHeightData(int modifiedX, int modifiedZ, float radius)
    {
        if (heightDataCache == null || terrainEntities.Count == 0)
            return;

        terrainEntities[0].MarkHeightRegionDirty(modifiedX, modifiedZ, radius);
    }

    // 注意：数据同步现在由 EditorTerrainProcessor.Draw() 通过统一的 TerrainDataChannel 机制处理。
    // 此方法保留作为备用入口，但通常不需要直接调用。
    public void SyncToGpu(CommandList commandList)
    {
        foreach (var entity in terrainEntities)
        {
            // 使用统一接口同步所有脏数据
            if (entity.IsDataDirty(TerrainDataChannel.Height))
                entity.SyncDataToGpu(TerrainDataChannel.Height, commandList);
            if (entity.IsDataDirty(TerrainDataChannel.MaterialIndex))
                entity.SyncDataToGpu(TerrainDataChannel.MaterialIndex, commandList);
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
            heightDataWidth = 0;
            heightDataHeight = 0;
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

    #region 项目持久化

    /// <summary>
    /// 保存项目。
    /// </summary>
    public void SaveProject()
    {
        var projectManager = ProjectManager.Instance;
        if (!projectManager.IsProjectOpen)
            return;

        var config = new ProjectConfig
        {
            HeightmapPath = currentTerrainPath,
            MaterialSlots = SaveMaterialSlotConfigs(),
            DefaultTextureSize = TextureSize.Size512
        };

        // 保存材质索引图
        if (MaterialIndices != null)
        {
            string indexPath = projectManager.GetMaterialIndexPath("terrain");
            SaveMaterialIndexMap(MaterialIndices, indexPath);
        }

        projectManager.SaveConfig(config);
    }

    private List<MaterialSlotConfig> SaveMaterialSlotConfigs()
    {
        var configs = new List<MaterialSlotConfig>();
        foreach (var slot in MaterialSlotManager.Instance.GetActiveSlots())
        {
            configs.Add(new MaterialSlotConfig
            {
                Index = slot.Index,
                Name = slot.Name,
                AlbedoTexturePath = slot.AlbedoTexturePath,
                NormalTexturePath = slot.NormalTexturePath,
                TilingScale = slot.TilingScale,
                TextureSize = TextureSize.Size512
            });
        }
        return configs;
    }

    private void SaveMaterialIndexMap(MaterialIndexMap map, string path)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8>(map.Width, map.Height);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                byte index = map.GetIndex(x, y);
                image[x, y] = new SixLabors.ImageSharp.PixelFormats.L8(index);
            }
        }
        image.SaveAsPng(path);
    }

    /// <summary>
    /// 加载项目。
    /// </summary>
    public void LoadProject(string projectPath)
    {
        var projectManager = ProjectManager.Instance;
        if (!projectManager.OpenProject(projectPath))
            return;

        var config = projectManager.LoadConfig();
        if (config == null)
            return;

        // 恢复材质槽位
        foreach (var slotConfig in config.MaterialSlots)
        {
            var slot = MaterialSlotManager.Instance[slotConfig.Index];
            slot.Name = slotConfig.Name;
            slot.TilingScale = slotConfig.TilingScale;

            if (!string.IsNullOrEmpty(slotConfig.AlbedoTexturePath))
            {
                string fullPath = Path.IsPathRooted(slotConfig.AlbedoTexturePath)
                    ? slotConfig.AlbedoTexturePath
                    : Path.Combine(projectManager.MaterialsPath, slotConfig.AlbedoTexturePath);
                slot.AlbedoTexturePath = fullPath;
            }
        }

        // 加载高度图
        if (!string.IsNullOrEmpty(config.HeightmapPath))
        {
            _ = LoadTerrainAsync(config.HeightmapPath);
        }

        // 通知需要加载材质纹理（由外部调用 LoadMaterialTextures）
        MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 加载项目后调用此方法来加载材质纹理到 GPU。
    /// 需要在渲染线程中调用。
    /// </summary>
    public void LoadMaterialTextures(CommandList commandList)
    {
        MaterialSlotManager.Instance.LoadTexturesFromConfiguredPaths(graphicsDevice, commandList);
    }

    private MaterialIndexMap? LoadMaterialIndexMap(string path)
    {
        if (!File.Exists(path))
            return null;

        using var image = HeightmapImage.Load<L8>(path);
        var map = new MaterialIndexMap(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                map.SetIndex(x, y, image[x, y].PackedValue);
            }
        }
        return map;
    }

    #endregion
}

public sealed class TerrainLoadedEventArgs : EventArgs
{
    public required List<EditorTerrainEntity> Entities { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string SourcePath { get; init; }
}
