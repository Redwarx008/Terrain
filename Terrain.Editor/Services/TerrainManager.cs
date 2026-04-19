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

    /// <summary>
    /// 气候蒙版，R8 格式，尺寸为高度图的 1/4。
    /// 每个像素存储一个气候 ID，驱动规则求值生成 MaterialIndices。
    /// </summary>
    public ClimateMask? ClimateMask { get; private set; }

    private string? currentClimateMaskPath;

    private TerrainComponent? terrainComponent;
    private string? currentTerrainPath;
    private string? lastLoadError;

    /// <summary>
    /// 高度缩放系数，将 ushort (0-65535) 值转换为世界空间高度。
    /// 世界高度 = raw * (1/65535) * HeightScale。
    /// </summary>
    public float HeightScale { get; private set; } = 100.0f;

    public string? CurrentTerrainPath => currentTerrainPath;
    public string? CurrentClimateMaskPath => currentClimateMaskPath;

    public IReadOnlyList<EditorTerrainEntity> TerrainEntities => terrainEntities;
    public bool HasTerrainLoaded => terrainEntities.Count > 0;
    public bool HasHeightCache => heightDataCache != null;
    public int HeightCacheWidth => heightDataWidth;
    public int HeightCacheHeight => heightDataHeight;
    public ushort[]? HeightDataCache => heightDataCache;
    public SplitTerrainConfig? SplitConfig => currentSplitConfig;
    public string? LastLoadError => lastLoadError;

    /// <summary>
    /// 设置高度缩放系数，实时传播到所有地形实体。
    /// </summary>
    public void SetHeightScale(float newScale)
    {
        if (newScale <= 0.0f)
            return;
        if (MathF.Abs(HeightScale - newScale) < 0.001f)
            return;

        HeightScale = newScale;
        foreach (var entity in terrainEntities)
        {
            entity.SetHeightScale(HeightScale);
        }
        ProjectManager.Instance.MarkDirty();
    }

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
                DefaultLeafNodeSize,
                HeightScale);

            if (terrainEntity == null)
            {
                lastLoadError = "Failed to create editor terrain.";
                Log.Error(lastLoadError);
                return new List<EditorTerrainEntity>();
            }

            terrainEntities.Add(terrainEntity);
            currentHeightmapInfo = info;
            currentTerrainPath = heightmapPath;

            // ClimateMask 使用 heightmap 的 1/4 分辨率，MaterialIndexMap 使用 1/2 分辨率
            int climateMaskWidth = (heightDataWidth + 3) / 4;
            int climateMaskHeight = (heightDataHeight + 3) / 4;
            ClimateMask = new ClimateMask(climateMaskWidth, climateMaskHeight);

            int splatMapWidth = (heightDataWidth + 1) / 2;
            int splatMapHeight = (heightDataHeight + 1) / 2;
            MaterialIndices = new MaterialIndexMap(splatMapWidth, splatMapHeight);

            // 设置材质索引数据引用到实体
            terrainEntity.MaterialIndexMap = MaterialIndices;
            terrainEntity.SetClimateMask(graphicsDevice, ClimateMask);

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
            Log.Error($"{lastLoadError}\n{ex.StackTrace}");
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
        ClimateMask = null;
        currentClimateMaskPath = null;
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
        return height * HeightSampleNormalization * HeightScale;
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

        if (channel == TerrainDataChannel.Height)
            terrainEntities[0].MarkClimateSplatDirty(centerX, centerZ, radius);
    }

    public void UpdateHeightData(int modifiedX, int modifiedZ, float radius)
    {
        if (heightDataCache == null || terrainEntities.Count == 0)
            return;

        terrainEntities[0].MarkRegionDirty(TerrainDataChannel.Height, modifiedX, modifiedZ, radius);
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

    #region 气候规则求值

    public void RegenerateMaterialIndices()
    {
        foreach (var terrainEntity in terrainEntities)
        {
            terrainEntity.MarkClimateRulesDirty();
            terrainEntity.MarkAllClimateSplatDirty();
        }
    }

    public void RegenerateMaterialIndices(float centerX, float centerY, float radius)
    {
        if (ClimateMask == null)
            return;

        int centerSampleX = (int)(centerX * 4.0f);
        int centerSampleZ = (int)(centerY * 4.0f);
        float sampleRadius = Math.Max(1.0f, radius * 4.0f);

        foreach (var terrainEntity in terrainEntities)
            terrainEntity.MarkClimateSplatDirty(centerSampleX, centerSampleZ, sampleRadius);
    }

    public void MarkClimateMaskDirty()
    {
        foreach (var terrainEntity in terrainEntities)
        {
            terrainEntity.MarkClimateMaskDirty();
            terrainEntity.MarkAllClimateSplatDirty();
        }
    }

    /// <summary>
    /// 在 4x4 高度图邻域上采样平均海拔。
    /// maskX/maskY 为 ClimateMask 坐标（1/4 高度图），乘以 4 映射到高度图。
    /// </summary>
    private float SampleAverageAltitude(int maskX, int maskY)
    {
        int hx = maskX * 4;
        int hy = maskY * 4;

        float total = 0.0f;
        int count = 0;
        for (int offsetY = 0; offsetY < 4; offsetY++)
        {
            for (int offsetX = 0; offsetX < 4; offsetX++)
            {
                int sampleX = Math.Clamp(hx + offsetX, 0, heightDataWidth - 1);
                int sampleY = Math.Clamp(hy + offsetY, 0, heightDataHeight - 1);
                total += heightDataCache![sampleY * heightDataWidth + sampleX];
                count++;
            }
        }

        float rawAverage = total / Math.Max(1, count);
        return rawAverage * HeightSampleNormalization * HeightScale;
    }

    /// <summary>
    /// 采样地形坡度。使用中心差分法在高度图上计算法线，
    /// worldNy = 4.0f 因为采样点间距为 4 个高度图像素（1/4 分辨率映射），
    /// 对应 4 个世界单位水平距离。
    /// </summary>
    private float SampleSlopeDegrees(int maskX, int maskY)
    {
        int hx = Math.Clamp(maskX * 4, 0, heightDataWidth - 1);
        int hy = Math.Clamp(maskY * 4, 0, heightDataHeight - 1);

        float left = SampleHeightNormalized(hx - 1, hy);
        float right = SampleHeightNormalized(hx + 1, hy);
        float up = SampleHeightNormalized(hx, hy - 1);
        float down = SampleHeightNormalized(hx, hy + 1);

        float worldNx = (left - right) * HeightScale;
        float worldNz = (up - down) * HeightScale;
        // 2 像素间距 × 4 像素/ClimateMask 单位 = 8 世界单位水平距离
        // 但中心差分本身就是相邻像素间距 (1 pixel)，在 1:1 heightmap→world 映射下 = 1 世界单位
        // 左右各 1 像素，总跨度 2 世界单位
        const float worldNy = 2.0f;

        float normalLength = MathF.Sqrt(worldNx * worldNx + worldNz * worldNz + worldNy * worldNy);
        if (normalLength <= 0.0001f)
            return 0.0f;

        float cosSlope = Math.Clamp(worldNy / normalLength, -1.0f, 1.0f);
        return MathF.Acos(cosSlope) * (180.0f / MathF.PI);
    }

    private float SampleHeightNormalized(int x, int y)
    {
        int clampedX = Math.Clamp(x, 0, heightDataWidth - 1);
        int clampedY = Math.Clamp(y, 0, heightDataHeight - 1);
        return heightDataCache![clampedY * heightDataWidth + clampedX] * HeightSampleNormalization;
    }

    private static int ResolveMaterialIndex(ClimateRuleService climateState, byte climateId, float altitude, float slope)
    {
        int resolvedMaterial = 0;

        // Rule stack semantics:
        // if multiple rules overlap on altitude / slope within the same climate,
        // later rules override earlier ones as long as the full condition set matches.
        foreach (var rule in climateState.GetRulesForClimate(climateId))
        {
            if (!rule.Enabled)
                continue;

            if (altitude < rule.MinAltitude || altitude > rule.MaxAltitude)
                continue;

            if (slope < rule.MinSlopeDegrees || slope > rule.MaxSlopeDegrees)
                continue;

            resolvedMaterial = rule.MaterialSlotIndex;
        }

        return resolvedMaterial;
    }

    #endregion

    #region 项目持久化

    /// <summary>
    /// 保存项目到 TOML 文件。
    /// </summary>
    public void SaveProject()
    {
        var projectManager = ProjectManager.Instance;
        if (!projectManager.IsProjectOpen)
            return;

        // 保存气候蒙版（L8 PNG，1/4 高度图分辨率）
        string? climateMaskPath = null;
        if (ClimateMask != null)
        {
            climateMaskPath = !string.IsNullOrEmpty(currentClimateMaskPath)
                ? currentClimateMaskPath
                : Path.Combine(projectManager.ProjectPath, "terrain_climate_mask.png");
            SaveClimateMask(ClimateMask, climateMaskPath);
            currentClimateMaskPath = climateMaskPath;
        }

        var config = new TomlProjectConfig
        {
            Name = projectManager.ProjectName,
            HeightmapPath = currentTerrainPath,
            ClimateMaskPath = climateMaskPath,
            HeightScale = HeightScale,
            MaterialSlots = SaveMaterialSlotConfigs(),
            Climates = SaveClimateConfigs(),
            ClimateRules = SaveClimateRuleConfigs()
        };

        projectManager.SaveConfig(config);
    }

    private List<TomlMaterialSlotConfig> SaveMaterialSlotConfigs()
    {
        var configs = new List<TomlMaterialSlotConfig>();
        foreach (var slot in MaterialSlotManager.Instance.GetActiveSlots())
        {
            configs.Add(new TomlMaterialSlotConfig
            {
                Index = slot.Index,
                Name = slot.Name,
                AlbedoPath = slot.AlbedoTexturePath,
                NormalPath = slot.NormalTexturePath,
            });
        }
        return configs;
    }

    private static List<TomlClimateDefinitionConfig> SaveClimateConfigs()
    {
        var configs = new List<TomlClimateDefinitionConfig>();
        foreach (var climate in ClimateRuleService.Instance.Climates)
        {
            configs.Add(new TomlClimateDefinitionConfig
            {
                Id = climate.Id,
                Name = climate.Name,
                DebugColorR = climate.DebugColor.X,
                DebugColorG = climate.DebugColor.Y,
                DebugColorB = climate.DebugColor.Z,
                DebugColorA = climate.DebugColor.W,
            });
        }
        return configs;
    }

    private static List<TomlClimateRuleConfig> SaveClimateRuleConfigs()
    {
        var configs = new List<TomlClimateRuleConfig>();
        foreach (var rule in ClimateRuleService.Instance.Rules)
        {
            configs.Add(new TomlClimateRuleConfig
            {
                ClimateId = rule.ClimateId,
                Name = rule.Name,
                Enabled = rule.Enabled,
                MinAltitude = rule.MinAltitude,
                MaxAltitude = rule.MaxAltitude,
                MinSlopeDegrees = rule.MinSlopeDegrees,
                MaxSlopeDegrees = rule.MaxSlopeDegrees,
                BlendRange = rule.BlendRange,
                MaterialSlotIndex = rule.MaterialSlotIndex,
            });
        }
        return configs;
    }

    private static void RestoreClimateData(TomlProjectConfig config)
    {
        var climateState = ClimateRuleService.Instance;
        climateState.ClearAll();

        foreach (var climateConfig in config.Climates)
        {
            climateState.AddClimateFromConfig(
                climateConfig.Id,
                climateConfig.Name,
                new System.Numerics.Vector4(
                    climateConfig.DebugColorR,
                    climateConfig.DebugColorG,
                    climateConfig.DebugColorB,
                    climateConfig.DebugColorA));
        }

        foreach (var ruleConfig in config.ClimateRules)
        {
            climateState.AddRuleFromConfig(
                ruleConfig.ClimateId, ruleConfig.Name, ruleConfig.Enabled,
                ruleConfig.MinAltitude, ruleConfig.MaxAltitude,
                ruleConfig.MinSlopeDegrees, ruleConfig.MaxSlopeDegrees,
                ruleConfig.BlendRange, ruleConfig.MaterialSlotIndex);
        }

        climateState.NotifyMutated();
    }

    private static void SaveClimateMask(ClimateMask map, string path)
    {
        using var image = new Image<L8>(map.Width, map.Height);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                image[x, y] = new L8(map.GetValue(x, y));
            }
        }

        image.SaveAsPng(path);
    }

    /// <summary>
    /// 从 TOML 文件加载项目。
    /// </summary>
    public void LoadProject(string tomlFilePath)
    {
        // 清理旧项目状态
        RemoveCurrentTerrain();
        MaterialSlotManager.Instance.ClearAll();

        var projectManager = ProjectManager.Instance;
        if (!projectManager.OpenProject(tomlFilePath))
            return;

        var config = projectManager.LoadConfig();
        if (config == null)
            return;

        // 恢复材质槽位（路径已由 TomlProjectConfig.ReadFrom 解析为绝对路径）
        foreach (var slotConfig in config.MaterialSlots)
        {
            var slot = MaterialSlotManager.Instance[slotConfig.Index];
            slot.Name = slotConfig.Name;

            if (!string.IsNullOrEmpty(slotConfig.AlbedoPath))
                slot.AlbedoTexturePath = slotConfig.AlbedoPath;
            if (!string.IsNullOrEmpty(slotConfig.NormalPath))
                slot.NormalTexturePath = slotConfig.NormalPath;
        }

        // 恢复气候定义和规则
        RestoreClimateData(config);

        // 设置 HeightScale（在加载高度图前，以便 LoadTerrainAsync 使用）
        HeightScale = config.HeightScale;

        // 加载高度图
        if (!string.IsNullOrEmpty(config.HeightmapPath) && File.Exists(config.HeightmapPath))
        {
            _ = LoadTerrainAsync(config.HeightmapPath);
        }

        // 加载气候蒙版（异步高度图加载完成后 ClimateMask 才存在，
        // 此处记录路径，由 HeightmapLoaded 事件触发实际加载）
        if (!string.IsNullOrEmpty(config.ClimateMaskPath) && File.Exists(config.ClimateMaskPath))
        {
            pendingClimateMaskPath = config.ClimateMaskPath;
        }

        // 通知需要加载材质纹理（由外部调用 LoadMaterialTextures）
        MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);
    }

    // 气候蒙版加载路径暂存，等待高度图加载完成后使用
    private string? pendingClimateMaskPath;

    /// <summary>
    /// 尝试加载暂存的气候蒙版。由 HeightmapLoaded 事件调用。
    /// </summary>
    public bool TryLoadPendingClimateMask()
    {
        if (string.IsNullOrEmpty(pendingClimateMaskPath))
            return false;

        string path = pendingClimateMaskPath;
        pendingClimateMaskPath = null;
        return LoadClimateMask(path, markDirty: false);
    }

    /// <summary>
    /// 加载项目后调用此方法来加载材质纹理到 GPU。
    /// 需要在渲染线程中调用。
    /// </summary>
    public void LoadMaterialTextures(CommandList commandList)
    {
        MaterialSlotManager.Instance.LoadTexturesFromConfiguredPaths(graphicsDevice, commandList);
    }

    public bool LoadClimateMask(string path, bool markDirty = true)
    {
        if (ClimateMask == null || !File.Exists(path))
            return false;

        using var image = HeightmapImage.Load<L8>(path);
        if (image.Width != ClimateMask.Width || image.Height != ClimateMask.Height)
            return false;

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                ClimateMask.SetValue(x, y, image[x, y].PackedValue);
            }
        }

        currentClimateMaskPath = path;
        MarkClimateMaskDirty();
        RegenerateMaterialIndices();
        if (markDirty)
            ProjectManager.Instance.MarkDirty();
        return true;
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
