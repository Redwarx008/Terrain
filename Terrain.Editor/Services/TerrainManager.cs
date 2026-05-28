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
using System.Linq;
using System.Threading.Tasks;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering;
using Terrain.Editor.Services.PathFeatures;
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
    private const string DefaultHeightmapFileName = "terrain_heightmap.png";
    private const string DefaultBiomeMaskFileName = "terrain_biome_mask.png";

    private readonly GraphicsDevice graphicsDevice;
    private readonly Scene scene;
    private readonly Texture? defaultTerrainTexture;
    private readonly BiomeRuleService biomeRuleService = BiomeRuleService.Instance;
    private bool lastLayerHeatmapPreviewEnabled;
    private Texture? defaultDiffuseTexture;
    private HeightmapInfo? currentHeightmapInfo;

    private readonly List<EditorTerrainEntity> terrainEntities = new();
    private readonly List<Entity> sceneEntities = new();
    private SplitTerrainConfig? currentSplitConfig;

    private ushort[]? heightDataCache;
    private int heightDataWidth;
    private int heightDataHeight;

    /// <summary>
    /// 气候蒙版，R8 格式，尺寸为高度图的 1/2（对齐 SplatMap）。
    /// 每个像素存储一个气候 ID，驱动 GPU compute 重建材质控制图。
    /// </summary>
    public BiomeMask? BiomeMask { get; private set; }

    private string? currentBiomeMaskPath;

    private RiverCell[,]? riverMap;
    private string? currentRiverMapPath;

    private TerrainComponent? terrainComponent;
    private string? currentTerrainPath;
    private string? lastLoadError;
    private PathFeatureService? pathFeatureService;

    /// <summary>
    /// 高度缩放系数，将 ushort (0-65535) 值转换为世界空间高度。
    /// 世界高度 = raw * (1/65535) * HeightScale。
    /// </summary>
    public float HeightScale { get; private set; } = 100.0f;

    public string? CurrentTerrainPath => currentTerrainPath;
    public string? CurrentBiomeMaskPath => currentBiomeMaskPath;

    public IReadOnlyList<EditorTerrainEntity> TerrainEntities => terrainEntities;
    public bool HasTerrainLoaded => terrainEntities.Count > 0;
    public bool HasHeightCache => heightDataCache != null;
    public int HeightCacheWidth => heightDataWidth;
    public int HeightCacheHeight => heightDataHeight;
    public ushort[]? HeightDataCache => heightDataCache;
    public SplitTerrainConfig? SplitConfig => currentSplitConfig;
    public string? LastLoadError => lastLoadError;

    public RiverCell[,]? RiverMap => riverMap;
    public string? CurrentRiverMapPath => currentRiverMapPath;

    public PathFeatureService? PathFeatureService => pathFeatureService;

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

    public event EventHandler? RiverMapChanged;

    public TerrainManager(GraphicsDevice graphicsDevice, Scene scene, Texture? defaultTerrainTexture = null)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        this.defaultTerrainTexture = defaultTerrainTexture;
        lastLayerHeatmapPreviewEnabled = EditorState.Instance.CurrentDebugViewMode == SceneDebugViewMode.LayerHeatmap;
        biomeRuleService.StateChanged += OnBiomeRuleStateChanged;
        EditorState.Instance.DebugViewModeChanged += OnDebugViewModeChanged;
        EditorState.Instance.RuleSelectionChanged += OnRuleSelectionChanged;
    }

    public async Task<List<EditorTerrainEntity>> LoadTerrainAsync(
        string heightmapPath,
        bool preservePendingBiomeMaskPath = false,
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

        // Project reopen prepares pendingBiomeMaskPath before calling LoadTerrainAsync().
        // Preserve it across RemoveCurrentTerrain() so the loaded terrain can still consume the saved mask.
        string? preservedPendingBiomeMaskPath = preservePendingBiomeMaskPath ? pendingBiomeMaskPath : null;
        RemoveCurrentTerrain();
        if (preservePendingBiomeMaskPath)
            pendingBiomeMaskPath = preservedPendingBiomeMaskPath;
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

            // BiomeMask 使用 heightmap 的 1/2 分辨率（对齐 SplatMap，避免大地形 GPU 纹理尺寸溢出）
            int biomeMaskWidth = (heightDataWidth + 1) / 2;
            int biomeMaskHeight = (heightDataHeight + 1) / 2;
            BiomeMask = new BiomeMask(biomeMaskWidth, biomeMaskHeight);

            terrainEntity.SetBiomeMask(graphicsDevice, BiomeMask);

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
        pathFeatureService?.Clear();

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
        BiomeMask = null;
        currentBiomeMaskPath = null;
        pendingBiomeMaskPath = null;
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

    public void SetPathFeatureService(PathFeatureService service)
    {
        pathFeatureService = service ?? throw new ArgumentNullException(nameof(service));
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
            terrainEntities[0].MarkBiomeSplatDirty(centerX, centerZ, radius);
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
        biomeRuleService.StateChanged -= OnBiomeRuleStateChanged;
        EditorState.Instance.DebugViewModeChanged -= OnDebugViewModeChanged;
        EditorState.Instance.RuleSelectionChanged -= OnRuleSelectionChanged;
        RemoveCurrentTerrain();
        defaultDiffuseTexture?.Dispose();
        heightDataCache = null;
    }

    private void OnBiomeRuleStateChanged(object? sender, EventArgs e)
    {
        RegenerateMaterialIndices();
    }

    private void OnDebugViewModeChanged(object? sender, EventArgs e)
    {
        bool isLayerHeatmapPreviewEnabled = EditorState.Instance.CurrentDebugViewMode == SceneDebugViewMode.LayerHeatmap;
        if (isLayerHeatmapPreviewEnabled || lastLayerHeatmapPreviewEnabled)
            RegenerateLayerHeatmapPreview();

        lastLayerHeatmapPreviewEnabled = isLayerHeatmapPreviewEnabled;
    }

    private void OnRuleSelectionChanged(object? sender, EventArgs e)
    {
        if (EditorState.Instance.CurrentDebugViewMode == SceneDebugViewMode.LayerHeatmap)
            RegenerateLayerHeatmapPreview();
    }

    #region 生物群系规则求值

    public void RegenerateMaterialIndices()
    {
        foreach (var terrainEntity in terrainEntities)
        {
            terrainEntity.MarkBiomeRulesDirty();
            terrainEntity.MarkAllBiomeSplatDirty();
        }
    }

    public void RegenerateMaterialIndices(float centerX, float centerY, float radius)
    {
        if (BiomeMask == null)
            return;

        int centerSampleX = (int)centerX;
        int centerSampleZ = (int)centerY;
        float sampleRadius = Math.Max(1.0f, radius);

        foreach (var terrainEntity in terrainEntities)
        {
            terrainEntity.MarkBiomeSplatDirty(centerSampleX, centerSampleZ, sampleRadius);
        }
    }

    private void RegenerateLayerHeatmapPreview()
    {
        foreach (var terrainEntity in terrainEntities)
            terrainEntity.MarkAllBiomeSplatDirty();
    }

    public void MarkBiomeMaskDirty()
    {
        foreach (var terrainEntity in terrainEntities)
        {
            terrainEntity.MarkBiomeMaskDirty();
            terrainEntity.MarkAllBiomeSplatDirty();
        }
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

        SaveProject(projectManager.ProjectFilePath, projectManager.ProjectName, snapshotEditableAssetsIntoProject: false);
    }

    /// <summary>
    /// 另存为新项目，并将当前可编辑资源快照写入新项目目录。
    /// </summary>
    public void SaveProjectAs(string projectFilePath, string projectName)
    {
        SaveProject(projectFilePath, projectName, snapshotEditableAssetsIntoProject: true);
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
                PropertiesPath = slot.PropertiesTexturePath,
            });
        }
        return configs;
    }

    private static List<TomlBiomeDefinitionConfig> SaveBiomeConfigs()
    {
        var configs = new List<TomlBiomeDefinitionConfig>();
        foreach (var biome in BiomeRuleService.Instance.Biomes)
        {
            configs.Add(new TomlBiomeDefinitionConfig
            {
                Id = biome.Id,
                Name = biome.Name,
                DebugColorR = biome.DebugColor.X,
                DebugColorG = biome.DebugColor.Y,
                DebugColorB = biome.DebugColor.Z,
                DebugColorA = biome.DebugColor.W,
            });
        }
        return configs;
    }

    private static List<TomlBiomeLayerConfig> SaveBiomeLayerConfigs()
    {
        var configs = new List<TomlBiomeLayerConfig>();
        foreach (BiomeRuleLayer layer in BiomeRuleService.Instance.Layers)
        {
            configs.Add(new TomlBiomeLayerConfig
            {
                Id = layer.Id,
                BiomeId = layer.BiomeId,
                Name = layer.Name,
                Enabled = layer.Enabled,
                Visible = layer.Visible,
                MaterialSlotIndex = layer.MaterialSlotIndex,
                PriorityOrder = layer.PriorityOrder,
            });
        }

        return configs;
    }

    private static List<TomlBiomeModifierConfig> SaveBiomeModifierConfigs()
    {
        var configs = new List<TomlBiomeModifierConfig>();
        foreach (BiomeRuleLayer layer in BiomeRuleService.Instance.Layers)
        {
            foreach (BiomeModifier modifier in layer.Modifiers)
            {
                configs.Add(new TomlBiomeModifierConfig
                {
                    Id = modifier.Id,
                    LayerId = layer.Id,
                    Name = modifier.Name,
                    Type = modifier.Type.ToString(),
                    BlendMode = modifier.BlendMode.ToString(),
                    Enabled = modifier.Enabled,
                    Visible = modifier.Visible,
                    Opacity = modifier.Opacity,
                    Min = modifier.Min,
                    Max = modifier.Max,
                    MinFalloff = modifier.MinFalloff,
                    MaxFalloff = modifier.MaxFalloff,
                    Radius = modifier.Radius,
                    AngleDegrees = modifier.AngleDegrees,
                    AngleRangeDegrees = modifier.AngleRangeDegrees,
                    Scale = modifier.Scale,
                    OffsetX = modifier.OffsetX,
                    OffsetY = modifier.OffsetY,
                    Seed = modifier.Seed,
                    Octaves = modifier.Octaves,
                    Invert = modifier.Invert,
                    TextureMaskPath = modifier.TextureMaskPath,
                    TextureMaskChannel = modifier.TextureMaskChannel,
                });
            }
        }

        return configs;
    }

    private List<TomlPathNodeConfig> SavePathNodeConfigs()
    {
        var configs = new List<TomlPathNodeConfig>();
        if (pathFeatureService == null)
            return configs;

        HashSet<Guid> persistedNodeIds = pathFeatureService.Features
            .SelectMany(static feature => feature.NodeIds)
            .ToHashSet();

        foreach (PathNode node in pathFeatureService.Nodes.Values.Where(node => persistedNodeIds.Contains(node.Id)))
        {
            configs.Add(new TomlPathNodeConfig
            {
                Id = node.Id.ToString("D"),
                X = node.Position.X,
                Y = node.Position.Y,
                Z = node.Position.Z,
            });
        }

        return configs;
    }

    private List<TomlPathFeatureConfig> SavePathFeatureConfigs()
    {
        var configs = new List<TomlPathFeatureConfig>();
        if (pathFeatureService == null)
            return configs;

        foreach (PathFeature feature in pathFeatureService.Features)
        {
            configs.Add(new TomlPathFeatureConfig
            {
                Id = feature.Id.ToString("D"),
                Name = feature.Name,
                Kind = feature.Kind.ToString(),
                NodeIds = feature.NodeIds.Select(static id => id.ToString("D")).ToList(),
                Width = feature.Style.Width,
                Depth = feature.Style.Depth,
                SideSlope = feature.Style.SideSlope,
                CornerSpan = feature.Style.CornerSpan,
                RoadStyle = feature.Style.RoadStyle.ToString(),
            });
        }

        return configs;
    }

    private static bool RestoreBiomeData(TomlProjectConfig config)
    {
        var biomeState = BiomeRuleService.Instance;
        biomeState.ClearAll();

        foreach (var biomeConfig in config.Biomes)
        {
            biomeState.AddBiomeFromConfig(
                biomeConfig.Id,
                biomeConfig.Name,
                new System.Numerics.Vector4(
                    biomeConfig.DebugColorR,
                    biomeConfig.DebugColorG,
                    biomeConfig.DebugColorB,
                    biomeConfig.DebugColorA));
        }

        if (config.BiomeLayers.Count > 0)
        {
            var layerLookup = new Dictionary<int, BiomeRuleLayer>();
            foreach (TomlBiomeLayerConfig layerConfig in config.BiomeLayers.OrderBy(static entry => entry.PriorityOrder))
            {
                BiomeRuleLayer layer = biomeState.AddLayer(layerConfig.BiomeId);
                layer.Id = layerConfig.Id;
                layer.Name = layerConfig.Name;
                layer.Enabled = layerConfig.Enabled;
                layer.Visible = layerConfig.Visible;
                layer.MaterialSlotIndex = layerConfig.MaterialSlotIndex;
                layer.PriorityOrder = layerConfig.PriorityOrder;
                layer.Modifiers.Clear();
                layerLookup[layer.Id] = layer;
            }

            foreach (TomlBiomeModifierConfig modifierConfig in config.BiomeModifiers)
            {
                if (!layerLookup.TryGetValue(modifierConfig.LayerId, out BiomeRuleLayer? layer))
                    continue;

                if (!Enum.TryParse(modifierConfig.Type, ignoreCase: true, out BiomeModifierType modifierType))
                    modifierType = BiomeModifierType.HeightRange;

                if (!Enum.TryParse(modifierConfig.BlendMode, ignoreCase: true, out BiomeModifierBlendMode blendMode))
                    blendMode = BiomeModifierBlendMode.Multiply;

                layer.Modifiers.Add(new BiomeModifier
                {
                    Id = modifierConfig.Id,
                    Name = modifierConfig.Name,
                    Type = modifierType,
                    BlendMode = blendMode,
                    Enabled = modifierConfig.Enabled,
                    Visible = modifierConfig.Visible,
                    Opacity = modifierConfig.Opacity,
                    Min = modifierConfig.Min,
                    Max = modifierConfig.Max,
                    MinFalloff = modifierConfig.MinFalloff,
                    MaxFalloff = modifierConfig.MaxFalloff,
                    Radius = modifierConfig.Radius,
                    AngleDegrees = modifierConfig.AngleDegrees,
                    AngleRangeDegrees = modifierConfig.AngleRangeDegrees,
                    Scale = modifierConfig.Scale,
                    OffsetX = modifierConfig.OffsetX,
                    OffsetY = modifierConfig.OffsetY,
                    Seed = modifierConfig.Seed,
                    Octaves = modifierConfig.Octaves,
                    Invert = modifierConfig.Invert,
                    TextureMaskPath = modifierConfig.TextureMaskPath,
                    TextureMaskChannel = modifierConfig.TextureMaskChannel,
                });
            }

            foreach (BiomeRuleLayer layer in layerLookup.Values)
                layer.EnsureLegacyModifiers();
        }

        biomeState.NormalizeAllRanges();
        bool repairedDefaultBaseMaterialSlot = RepairDefaultBaseMaterialSlot(biomeState);
        biomeState.RebaseNextIds();

        var editorState = EditorState.Instance;
        int selectedLayerIndex = editorState.SelectedRuleIndex;
        if ((uint)selectedLayerIndex >= (uint)biomeState.Layers.Count)
            selectedLayerIndex = biomeState.Layers.Count > 0 ? 0 : -1;

        if (selectedLayerIndex >= 0)
        {
            editorState.CurrentBiomeId = biomeState.Layers[selectedLayerIndex].BiomeId;
        }

        editorState.SelectedRuleIndex = selectedLayerIndex;
        biomeState.NotifyMutated();
        return repairedDefaultBaseMaterialSlot;
    }

    private void RestorePathData(TomlProjectConfig config)
    {
        if (pathFeatureService == null)
            return;

        var snapshot = new PathNetworkSnapshot();
        foreach (TomlPathNodeConfig nodeConfig in config.PathNodes)
        {
            if (!Guid.TryParse(nodeConfig.Id, out Guid nodeId))
                continue;

            snapshot.Nodes.Add(new PathNode
            {
                Id = nodeId,
                Position = new Vector3(nodeConfig.X, nodeConfig.Y, nodeConfig.Z),
            });
        }

        var validNodeIds = snapshot.Nodes.Select(static node => node.Id).ToHashSet();
        foreach (TomlPathFeatureConfig featureConfig in config.PathFeatures)
        {
            if (!Guid.TryParse(featureConfig.Id, out Guid featureId))
                continue;

            if (!Enum.TryParse(featureConfig.Kind, ignoreCase: true, out PathFeatureKind kind))
                kind = PathFeatureKind.Road;

            PathRoadStyle roadStyle = PathRoadStyle.Dirt;
            if (kind == PathFeatureKind.Road
                && !Enum.TryParse(featureConfig.RoadStyle, ignoreCase: true, out roadStyle))
            {
                roadStyle = PathRoadStyle.Dirt;
            }

            var feature = new PathFeature
            {
                Id = featureId,
                Name = string.IsNullOrWhiteSpace(featureConfig.Name) ? kind.ToString() : featureConfig.Name,
                Kind = kind,
                Style = new PathFeatureStyle
                {
                    Width = featureConfig.Width,
                    Depth = featureConfig.Depth,
                    SideSlope = featureConfig.SideSlope,
                    CornerSpan = featureConfig.CornerSpan,
                    RoadStyle = roadStyle,
                },
            };

            foreach (string nodeIdText in featureConfig.NodeIds)
            {
                if (Guid.TryParse(nodeIdText, out Guid nodeId) && validNodeIds.Contains(nodeId))
                    feature.NodeIds.Add(nodeId);
            }

            if (feature.NodeIds.Count >= 2)
                snapshot.Features.Add(feature);
        }

        pathFeatureService.RestoreSnapshotFromProject(snapshot);
    }

    private static bool RepairDefaultBaseMaterialSlot(BiomeRuleService biomeState)
    {
        int firstActiveSlotIndex = MaterialSlotManager.Instance
            .GetActiveSlots()
            .Select(static slot => slot.Index)
            .DefaultIfEmpty(-1)
            .First();
        if (firstActiveSlotIndex < 0)
            return false;

        bool repaired = false;
        foreach (BiomeRuleLayer layer in biomeState.Layers)
        {
            BiomeDefinition? biome = biomeState.FindBiome(layer.BiomeId);
            if (!string.Equals(biome?.Name, "Default Biome", StringComparison.Ordinal)
                || !string.Equals(layer.Name, "Default Base", StringComparison.Ordinal))
                continue;

            if ((uint)layer.MaterialSlotIndex < 256
                && !MaterialSlotManager.Instance[layer.MaterialSlotIndex].IsEmpty)
            {
                continue;
            }

            if (layer.MaterialSlotIndex == firstActiveSlotIndex)
                continue;

            layer.MaterialSlotIndex = firstActiveSlotIndex;
            repaired = true;
        }

        return repaired;
    }

    private static void SaveBiomeMask(BiomeMask map, string path)
    {
        EnsureParentDirectory(path);

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

    private void SaveProject(string projectFilePath, string projectName, bool snapshotEditableAssetsIntoProject)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || heightDataCache == null)
            return;

        string fullProjectFilePath = Path.GetFullPath(projectFilePath);
        string resolvedProjectName = !string.IsNullOrWhiteSpace(projectName)
            ? projectName
            : Path.GetFileNameWithoutExtension(fullProjectFilePath);
        int version = ProjectManager.Instance.LoadConfig()?.Version ?? 2;

        string heightmapPath = ResolveHeightmapSavePath(fullProjectFilePath, snapshotEditableAssetsIntoProject);
        SaveHeightmap(pathFeatureService?.GetHeightDataForSave() ?? heightDataCache, heightDataWidth, heightDataHeight, heightmapPath);
        currentTerrainPath = heightmapPath;

        string? biomeMaskPath = null;
        if (BiomeMask != null)
        {
            biomeMaskPath = ResolveBiomeMaskSavePath(fullProjectFilePath, snapshotEditableAssetsIntoProject);
            SaveBiomeMask(BiomeMask, biomeMaskPath);
            currentBiomeMaskPath = biomeMaskPath;
        }

        var config = new TomlProjectConfig
        {
            Version = version,
            Name = resolvedProjectName,
            HeightmapPath = currentTerrainPath,
            BiomeMaskPath = biomeMaskPath,
            RiverMapImagePath = currentRiverMapPath,
            HeightScale = HeightScale,
            MaterialSlots = SaveMaterialSlotConfigs(),
            Biomes = SaveBiomeConfigs(),
            BiomeLayers = SaveBiomeLayerConfigs(),
            BiomeModifiers = SaveBiomeModifierConfigs(),
            PathNodes = SavePathNodeConfigs(),
            PathFeatures = SavePathFeatureConfigs(),
        };

        ProjectManager.Instance.SaveConfigAs(fullProjectFilePath, config);
    }

    private string ResolveHeightmapSavePath(string projectFilePath, bool snapshotEditableAssetsIntoProject)
    {
        if (!snapshotEditableAssetsIntoProject && !string.IsNullOrWhiteSpace(currentTerrainPath))
            return currentTerrainPath;

        string fileName = !string.IsNullOrWhiteSpace(currentTerrainPath)
            ? Path.GetFileName(currentTerrainPath)
            : DefaultHeightmapFileName;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            fileName = $"{fileName}.png";

        return Path.Combine(ProjectManager.GetHeightmapsPath(projectFilePath), fileName);
    }

    private string ResolveBiomeMaskSavePath(string projectFilePath, bool snapshotEditableAssetsIntoProject)
    {
        if (!snapshotEditableAssetsIntoProject && !string.IsNullOrWhiteSpace(currentBiomeMaskPath))
            return currentBiomeMaskPath;

        string fileName = !string.IsNullOrWhiteSpace(currentBiomeMaskPath)
            ? Path.GetFileName(currentBiomeMaskPath)
            : DefaultBiomeMaskFileName;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            fileName = $"{fileName}.png";

        return Path.Combine(ProjectManager.GetSplatMapsPath(projectFilePath), fileName);
    }

    private static void SaveHeightmap(ushort[] heightData, int width, int height, string path)
    {
        EnsureParentDirectory(path);

        using var image = new Image<L16>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<L16> row = accessor.GetRowSpan(y);
                int rowOffset = y * width;
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new L16(heightData[rowOffset + x]);
                }
            }
        });

        image.SaveAsPng(path);
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
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
            if (!string.IsNullOrEmpty(slotConfig.PropertiesPath))
                slot.PropertiesTexturePath = slotConfig.PropertiesPath;
        }

        int firstActiveSlot = MaterialSlotManager.Instance.GetActiveSlots().Select(static slot => slot.Index).DefaultIfEmpty(0).First();
        MaterialSlotManager.Instance.SelectedSlotIndex = firstActiveSlot;
        MaterialSlotManager.Instance.NotifySlotsChanged();

        // 恢复生物群系定义和层
        if (RestoreBiomeData(config))
        {
            ProjectManager.Instance.MarkDirty();
        }

        // 设置 HeightScale（在加载高度图前，以便 LoadTerrainAsync 使用）
        HeightScale = config.HeightScale;

        // 加载生物群系蒙版（异步高度图加载完成后 BiomeMask 才存在，
        // 此处记录路径，由 HeightmapLoaded 事件触发实际加载）
        pendingBiomeMaskPath = !string.IsNullOrEmpty(config.BiomeMaskPath) && File.Exists(config.BiomeMaskPath)
            ? config.BiomeMaskPath
            : null;

        // Restore river map
        if (!string.IsNullOrEmpty(config.RiverMapImagePath) && File.Exists(config.RiverMapImagePath))
        {
            LoadRiverMap(config.RiverMapImagePath);
        }

        // 加载高度图。pendingBiomeMaskPath 必须先准备好，
        // 因为当前 LoadTerrainAsync 会在 TerrainLoaded 事件中立刻尝试消费它。
        if (!string.IsNullOrEmpty(config.HeightmapPath) && File.Exists(config.HeightmapPath))
        {
            _ = LoadTerrainAsync(config.HeightmapPath, preservePendingBiomeMaskPath: true);

            // 兜底：如果当前调用路径下没有订阅 TerrainLoaded，仍然尝试消费暂存蒙版。
            if (HasTerrainLoaded)
                TryLoadPendingBiomeMask();
        }

        RestorePathData(config);

        // 通知需要加载材质纹理（由外部调用 LoadMaterialTextures）
        MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);
    }

    // 生物群系蒙版加载路径暂存，等待高度图加载完成后使用
    private string? pendingBiomeMaskPath;

    /// <summary>
    /// 尝试加载暂存的生物群系蒙版。由 HeightmapLoaded 事件调用。
    /// </summary>
    public bool TryLoadPendingBiomeMask()
    {
        if (string.IsNullOrEmpty(pendingBiomeMaskPath))
            return false;

        string path = pendingBiomeMaskPath;
        pendingBiomeMaskPath = null;
        return LoadBiomeMask(path, markDirty: false);
    }

    /// <summary>
    /// 加载项目后调用此方法来加载材质纹理到 GPU。
    /// 需要在渲染线程中调用。
    /// </summary>
    public void LoadMaterialTextures(CommandList commandList)
    {
        MaterialSlotManager.Instance.LoadTexturesFromConfiguredPaths(graphicsDevice, commandList);
    }

    public bool LoadBiomeMask(string path, bool markDirty = true)
    {
        if (BiomeMask == null || !File.Exists(path))
            return false;

        using var image = HeightmapImage.Load<L8>(path);

        // BiomeMask 为半分辨率 (splatmap 尺寸)
        // 支持：半分辨率图像直接加载，全分辨率图像自动降采样
        bool halfRes = image.Width == BiomeMask.Width && image.Height == BiomeMask.Height;
        bool fullRes = image.Width == heightDataWidth && image.Height == heightDataHeight;
        if (!halfRes && !fullRes)
            return false;

        for (int y = 0; y < BiomeMask.Height; y++)
        {
            for (int x = 0; x < BiomeMask.Width; x++)
            {
                int srcX = fullRes ? x * 2 : x;
                int srcY = fullRes ? y * 2 : y;
                byte biomeId = image[srcX, srcY].PackedValue;
                BiomeMask.SetValue(x, y, biomeId);
            }
        }

        currentBiomeMaskPath = path;
        MarkBiomeMaskDirty();
        RegenerateMaterialIndices();
        if (markDirty)
            ProjectManager.Instance.MarkDirty();
        return true;
    }

    public bool LoadRiverMap(string path)
    {
        var service = new RiverMapService();
        if (!service.Load(path))
        {
            Log.Error($"River map load failed: {string.Join("; ", service.Errors)}");
            return false;
        }

        riverMap = service.Cells;
        currentRiverMapPath = path;
        RiverMapChanged?.Invoke(this, EventArgs.Empty);
        ProjectManager.Instance.MarkDirty();
        return true;
    }

    public void ClearRiverMap()
    {
        riverMap = null;
        currentRiverMapPath = null;
        RiverMapChanged?.Invoke(this, EventArgs.Empty);
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
