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
using Terrain.Editor.Services.Resources;
using Terrain.Resources;
using StrideColor = Stride.Core.Mathematics.Color;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;
using HeightmapImage = SixLabors.ImageSharp.Image;

namespace Terrain.Editor.Services;

/// <summary>
/// Manages the editor terrain scene object and the shared CPU height cache.
/// Large height rasters use sliced height textures internally, but still appear as one logical terrain.
/// </summary>
public sealed class TerrainManager : IDisposable, IRiverMapSource
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");
    private const int DefaultLeafNodeSize = 32;
    private const float HeightSampleNormalization = 1.0f / ushort.MaxValue;

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
    private string? pendingProjectNotification;

    private RiverCell[,]? riverMap;
    private string? currentRiverMapPath;

    private TerrainComponent? terrainComponent;
    private string? lastLoadError;

    /// <summary>
    /// 高度缩放系数，将 ushort (0-65535) 值转换为世界空间高度。
    /// 世界高度 = raw * (1/65535) * HeightScale。
    /// </summary>
    public float HeightScale { get; private set; } = 100.0f;
    public bool TerrainVisible { get; private set; } = true;

    public string? PendingProjectNotification => pendingProjectNotification;

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

        TerrainSurfaceChanged?.Invoke(this, EventArgs.Empty);
        EditorDirtyState.Instance.MarkDirty();
    }

    public event EventHandler<TerrainLoadedEventArgs>? TerrainLoaded;

    /// <summary>
    /// 项目级提示消息产生后触发，例如旧数据迁移完成。
    /// </summary>
    public event EventHandler? ProjectNotificationRaised;

    /// <summary>
    /// 地形表面状态发生变化后触发，例如高度编辑、缩放调整或卸载地形。
    /// </summary>
    public event EventHandler? TerrainSurfaceChanged;

    /// <summary>
    /// 项目加载完成后触发，通知需要加载材质纹理。
    /// </summary>
    public event EventHandler? MaterialTexturesLoadRequired;

    public event EventHandler? RiverMapChanged;

    public void SetTerrainVisible(bool visible)
    {
        if (TerrainVisible == visible)
            return;

        TerrainVisible = visible;
        foreach (var sceneEntity in sceneEntities)
        {
            if (sceneEntity.Get<EditorTerrainComponent>() is { } component)
            {
                component.Enabled = visible;
            }
        }
    }

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
                    Enabled = TerrainVisible,
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
        heightDataCache = null;
        heightDataWidth = 0;
        heightDataHeight = 0;
        BiomeMask = null;
        pendingProjectNotification = null;
        TerrainSurfaceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<List<EditorTerrainEntity>> LoadFromResourceSession(EditorResourceSession session)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));

        if (session.HeightScale > 0.0f)
            HeightScale = session.HeightScale;

        var materialDescriptor = RuntimeMaterialDescriptorReader.ReadFrom(session.MaterialDescriptor.ResolvedPath);
        MaterialSlotManager.Instance.ApplyDescriptor(materialDescriptor, session.MaterialDescriptor.ResolvedPath);
        RuntimeBiomeSettings biomeSettings = RuntimeBiomeSettingsReader.ReadFrom(session.BiomeSettings.ResolvedPath);
        GameResourceResolver resourceResolver = GameResourceResolverBootstrap.CreateForAppDirectory(AppContext.BaseDirectory);
        EditorMaterialRecoveryResult materialRecovery = new EditorMaterialRecoveryService().Recover(
            materialDescriptor,
            session.MaterialDescriptor.ResolvedPath,
            biomeSettings,
            virtualPath => ResolveOptionalVirtualPath(resourceResolver, virtualPath));
        session.ApplyMaterialLoadState(materialRecovery.LoadState);
        foreach (EditorMaterialLoadIssue issue in materialRecovery.LoadState.Issues)
        {
            Log.Error(issue.Message);
        }

        MaterialSlotManager.Instance.ApplyRecoveredMaterials(materialRecovery);
        BiomeRuleService.Instance.ApplyRuntimeSettings(biomeSettings, materialRecovery.MaterialIndicesById);

        if (session.HasPendingHeightmap)
        {
            RemoveCurrentTerrain();
            lastLoadError = $"Terrain workspace heightmap is missing: {session.Heightmap.ResolvedPath}";
            Log.Error(lastLoadError);

            if (session.Rivers is { } pendingRivers)
                LoadRiverMap(pendingRivers.ResolvedPath, markDirty: false);
            else
                ClearRiverMap();

            MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);
            return new List<EditorTerrainEntity>();
        }

        List<EditorTerrainEntity> entities = await LoadTerrainAsync(session.Heightmap.ResolvedPath);
        if (entities.Count == 0)
        {
            RemoveCurrentTerrain();
            ClearRiverMap();
            return entities;
        }

        LoadBiomeMask(session.BiomeMask.ResolvedPath, markDirty: false);
        if (session.Rivers is { } rivers)
            LoadRiverMap(rivers.ResolvedPath, markDirty: false);
        else
            ClearRiverMap();

        MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);
        return entities;
    }

    private static string? ResolveOptionalVirtualPath(GameResourceResolver resolver, string virtualPath)
    {
        try
        {
            return resolver.ResolveRequiredFile(virtualPath).ResolvedPath;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public void SaveAuthoringResources(EditorResourceSession session)
    {
        SaveAuthoringResources(session, progress: null);
    }

    public void SaveAuthoringResources(
        EditorResourceSession session,
        IProgress<AuthoringSaveProgress>? progress)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (heightDataCache == null || heightDataWidth <= 0 || heightDataHeight <= 0)
            throw new InvalidOperationException("Heightmap data is not loaded.");
        if (BiomeMask == null)
            throw new InvalidOperationException("Biome mask data is not loaded.");

        progress?.Report(AuthoringSaveProgress.Running(1, 9, "Preparing authoring data..."));
        IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots =
            EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots(MaterialSlotManager.Instance.GetActiveSlots().ToArray());
        var materialIdsByIndex = descriptorSlots.ToDictionary(
            static slot => slot.Index,
            static slot => slot.Id);
        EditorBiomeSettingsSnapshot biomeSnapshot =
            EditorAuthoringResourceMapper.CreateBiomeSettingsSnapshot(BiomeRuleService.Instance, materialIdsByIndex);
        EditorResourceSaveService.Save(
            session,
            heightDataCache,
            heightDataWidth,
            heightDataHeight,
            BiomeMask,
            HeightScale,
            descriptorSlots,
            biomeSnapshot,
            progress);
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
        {
            terrainEntities[0].MarkBiomeSplatDirty(centerX, centerZ, radius);
            TerrainSurfaceChanged?.Invoke(this, EventArgs.Empty);
        }
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

    public string? ConsumePendingProjectNotification()
    {
        string? message = pendingProjectNotification;
        pendingProjectNotification = null;
        return message;
    }

    #endregion

    /// <summary>
    /// 加载材质纹理到 GPU。需要在渲染线程中调用。
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

        MarkBiomeMaskDirty();
        RegenerateMaterialIndices();
        if (markDirty)
            EditorDirtyState.Instance.MarkDirty();
        return true;
    }

    public bool LoadRiverMap(string path, bool markDirty = true)
    {
        var service = new RiverMapService();
        service.Load(path); // Always load data; errors are reported but don't block

        if (service.Cells == null)
        {
            Log.Error($"River map load failed: {string.Join("; ", service.Errors)}");
            return false;
        }

        riverMap = service.Cells;
        currentRiverMapPath = path;
        RiverMapChanged?.Invoke(this, EventArgs.Empty);
        if (markDirty)
            EditorDirtyState.Instance.MarkDirty();

        if (service.Errors.Count > 0)
            Log.Warning($"River map loaded with {service.Errors.Count} validation issue(s): {string.Join("; ", service.Errors)}");

        return true;
    }

    public void ClearRiverMap()
    {
        riverMap = null;
        currentRiverMapPath = null;
        RiverMapChanged?.Invoke(this, EventArgs.Empty);
    }

}

public sealed class TerrainLoadedEventArgs : EventArgs
{
    public required List<EditorTerrainEntity> Entities { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string SourcePath { get; init; }
}
