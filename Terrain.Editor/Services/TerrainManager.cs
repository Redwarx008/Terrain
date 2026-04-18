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
    /// 鏉愯川绱㈠紩鍥撅紝瀛樺偍姣忎釜鍍忕礌鐨勬潗璐ㄦЫ浣嶇储寮曘€?
    /// </summary>
    public MaterialIndexMap? MaterialIndices { get; private set; }
    public ClimateMask? ClimateMask { get; private set; }

    private TerrainComponent? terrainComponent;
    private string? currentTerrainPath;
    private string? currentClimateMaskPath;
    private string? lastLoadError;

    /// <summary>
    /// 楂樺害缂╂斁绯绘暟锛屽皢 ushort (0-65535) 鍊艰浆鎹负涓栫晫绌洪棿楂樺害銆?
    /// 涓栫晫楂樺害 = raw * (1/65535) * HeightScale銆?
    /// </summary>
    public float HeightScale { get; private set; } = 100.0f;

    public IReadOnlyList<EditorTerrainEntity> TerrainEntities => terrainEntities;
    public bool HasTerrainLoaded => terrainEntities.Count > 0;
    public bool HasHeightCache => heightDataCache != null;
    public int HeightCacheWidth => heightDataWidth;
    public int HeightCacheHeight => heightDataHeight;
    public ushort[]? HeightDataCache => heightDataCache;
    public SplitTerrainConfig? SplitConfig => currentSplitConfig;
    public string? LastLoadError => lastLoadError;
    public string? CurrentTerrainPath => currentTerrainPath;
    public string? CurrentClimateMaskPath => currentClimateMaskPath;

    /// <summary>
    /// 璁剧疆楂樺害缂╂斁绯绘暟锛屽疄鏃朵紶鎾埌鎵€鏈夊湴褰㈠疄浣撱€?
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
    /// 椤圭洰鍔犺浇瀹屾垚鍚庤Е鍙戯紝閫氱煡闇€瑕佸姞杞芥潗璐ㄧ汗鐞嗐€?
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

            // 鏉愯川绱㈠紩鍥句娇鐢?heightmap 鐨?1/2 鍒嗚鲸鐜?
            int splatMapWidth = (heightDataWidth + 1) / 2;
            int splatMapHeight = (heightDataHeight + 1) / 2;
            ClimateMask = new ClimateMask(splatMapWidth, splatMapHeight);
            MaterialIndices = new MaterialIndexMap(splatMapWidth, splatMapHeight);
            RegenerateMaterialIndices();

            // 璁剧疆鏉愯川绱㈠紩鏁版嵁寮曠敤鍒板疄浣?
            terrainEntity.MaterialIndexMap = MaterialIndices;

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
        currentClimateMaskPath = null;
        heightDataCache = null;
        heightDataWidth = 0;
        heightDataHeight = 0;
        ClimateMask = null;
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
        return height * HeightSampleNormalization * HeightScale;
    }

    /// <summary>
    /// 鑾峰彇鎸囧畾浣嶇疆鐨勫師濮嬮珮搴﹀€?(ushort 0-65535)銆?
    /// 鐢ㄤ簬 Flatten 绛夐渶瑕佷笌 HeightData 鏁扮粍鐩存帴姣旇緝鐨勫伐鍏枫€?
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
    /// 鏍囪鎸囧畾閫氶亾鐨勬暟鎹渶瑕佸悓姝ュ埌 GPU銆?
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

        terrainEntities[0].MarkRegionDirty(TerrainDataChannel.Height, modifiedX, modifiedZ, radius);
    }

    // 娉ㄦ剰锛氭暟鎹悓姝ョ幇鍦ㄧ敱 EditorTerrainProcessor.Draw() 閫氳繃缁熶竴鐨?TerrainDataChannel 鏈哄埗澶勭悊銆?
    // 姝ゆ柟娉曚繚鐣欎綔涓哄鐢ㄥ叆鍙ｏ紝浣嗛€氬父涓嶉渶瑕佺洿鎺ヨ皟鐢ㄣ€?
    public void SyncToGpu(CommandList commandList)
    {
        foreach (var entity in terrainEntities)
        {
            // 浣跨敤缁熶竴鎺ュ彛鍚屾鎵€鏈夎剰鏁版嵁
            if (entity.IsDataDirty(TerrainDataChannel.Height))
                entity.SyncDataToGpu(TerrainDataChannel.Height, commandList);
            if (entity.IsDataDirty(TerrainDataChannel.MaterialIndex))
                entity.SyncDataToGpu(TerrainDataChannel.MaterialIndex, commandList);
        }
    }

    public bool LoadClimateMask(string path, bool markDirty = true)
    {
        if (ClimateMask == null || !File.Exists(path))
            return false;

        using var image = HeightmapImage.Load<Rgba32>(path);
        if (image.Width != ClimateMask.Width || image.Height != ClimateMask.Height)
            return false;

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                // The climate authoring map is a single-channel index map, so we
                // read the red channel and ignore the other debug/preview channels.
                ClimateMask.SetValue(x, y, image[x, y].R);
            }
        }

        currentClimateMaskPath = path;
        RegenerateMaterialIndices();
        if (markDirty)
            ProjectManager.Instance.MarkDirty();
        return true;
    }

    public void RegenerateMaterialIndices()
    {
        if (ClimateMask == null)
            return;

        float fullRadius = MathF.Max(ClimateMask.Width, ClimateMask.Height);
        RegenerateMaterialIndices(ClimateMask.Width * 0.5f, ClimateMask.Height * 0.5f, fullRadius);
    }

    public void RegenerateMaterialIndices(float centerX, float centerY, float radius)
    {
        if (ClimateMask == null || MaterialIndices == null)
            return;
        if (heightDataCache == null || heightDataWidth <= 0 || heightDataHeight <= 0)
            return;

        int minX = radius >= MathF.Max(ClimateMask.Width, ClimateMask.Height)
            ? 0
            : Math.Max(0, (int)MathF.Floor(centerX - radius));
        int minY = radius >= MathF.Max(ClimateMask.Width, ClimateMask.Height)
            ? 0
            : Math.Max(0, (int)MathF.Floor(centerY - radius));
        int maxX = radius >= MathF.Max(ClimateMask.Width, ClimateMask.Height)
            ? ClimateMask.Width - 1
            : Math.Min(ClimateMask.Width - 1, (int)MathF.Ceiling(centerX + radius));
        int maxY = radius >= MathF.Max(ClimateMask.Width, ClimateMask.Height)
            ? ClimateMask.Height - 1
            : Math.Min(ClimateMask.Height - 1, (int)MathF.Ceiling(centerY + radius));

        var climateState = ClimateRuleService.Instance;

        // Rule evaluation runs on the authored climate-mask grid so climate paint,
        // material generation, and export all resolve against the same texel space.
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                byte climateId = ClimateMask.GetValue(x, y);
                float altitude = SampleAverageAltitude(x, y);
                float slope = SampleSlopeDegrees(x, y);
                int materialIndex = ResolveMaterialIndex(climateState, climateId, altitude, slope);

                MaterialIndices.SetPixel(x, y, new MaterialPixel
                {
                    Index = (byte)Math.Clamp(materialIndex, 0, byte.MaxValue),
                    Weight = 255,
                    Projection = 0x77,
                    Rotation = 0
                });
            }
        }

        // The renderer still uploads dirty regions in heightmap texel space, so
        // convert the regenerated climate-mask area back to the 2x denser grid.
        MarkDataDirty(TerrainDataChannel.MaterialIndex, (int)(centerX * 2.0f), (int)(centerY * 2.0f), Math.Max(1.0f, radius * 2.0f));
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

    private float SampleAverageAltitude(int maskX, int maskY)
    {
        int hx = maskX * 2;
        int hy = maskY * 2;

        float total = 0.0f;
        int count = 0;
        for (int offsetY = 0; offsetY < 2; offsetY++)
        {
            for (int offsetX = 0; offsetX < 2; offsetX++)
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

    private float SampleSlopeDegrees(int maskX, int maskY)
    {
        int hx = Math.Clamp(maskX * 2, 0, heightDataWidth - 1);
        int hy = Math.Clamp(maskY * 2, 0, heightDataHeight - 1);

        float left = SampleHeightNormalized(hx - 1, hy);
        float right = SampleHeightNormalized(hx + 1, hy);
        float up = SampleHeightNormalized(hx, hy - 1);
        float down = SampleHeightNormalized(hx, hy + 1);

        float worldNx = (left - right) * HeightScale;
        float worldNz = (up - down) * HeightScale;
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
        ClimateSeason activeSeason = ClimateSeason.All;

        foreach (var rule in climateState.Rules)
        {
            if (!rule.Enabled)
                continue;
            if (rule.ClimateId != climateId)
                continue;
            if (rule.Season != ClimateSeason.All && rule.Season != activeSeason)
                continue;
            if (altitude < rule.MinAltitude || altitude > rule.MaxAltitude)
                continue;
            if (slope > rule.MaxSlopeDegrees)
                continue;

            resolvedMaterial = rule.MaterialSlotIndex;
        }

        return resolvedMaterial;
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

    #region 椤圭洰鎸佷箙鍖?

    /// <summary>
    /// 淇濆瓨椤圭洰鍒?TOML 鏂囦欢銆?
    /// </summary>
    public void SaveProject()
    {
        var projectManager = ProjectManager.Instance;
        if (!projectManager.IsProjectOpen)
            return;

        // 淇濆瓨鏉愯川绱㈠紩鍥惧埌 splatmaps/
        string? climateMaskPath = null;
        if (ClimateMask != null)
        {
            climateMaskPath = !string.IsNullOrEmpty(currentClimateMaskPath)
                ? currentClimateMaskPath
                : Path.Combine(projectManager.ProjectPath, "terrain_climate_mask.png");
            SaveClimateMask(ClimateMask, climateMaskPath);
            currentClimateMaskPath = climateMaskPath;
        }

        if (MaterialIndices != null)
        {
            string indexPath = projectManager.GetMaterialIndexPath("terrain");
            SaveMaterialIndexMap(MaterialIndices, indexPath);
        }

        var config = new TomlProjectConfig
        {
            Name = projectManager.ProjectName,
            HeightmapPath = currentTerrainPath,
            ClimateMaskPath = climateMaskPath,
            IndexMapPath = MaterialIndices != null
                ? projectManager.GetMaterialIndexPath("terrain")
                : null,
            HeightScale = HeightScale,
            MaterialSlots = SaveMaterialSlotConfigs()
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

    private void SaveMaterialIndexMap(MaterialIndexMap map, string path)
    {
        using var image = new Image<Rgba32>(map.Width, map.Height);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var pixel = map.GetPixel(x, y);
                image[x, y] = new Rgba32(
                    pixel.Index,
                    pixel.Weight,
                    pixel.Projection,
                    pixel.Rotation
                );
            }
        }
        image.SaveAsPng(path);
    }

    private static void SaveClimateMask(ClimateMask map, string path)
    {
        using var image = new Image<Rgba32>(map.Width, map.Height);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                byte climateId = map.GetValue(x, y);
                image[x, y] = new Rgba32(climateId, climateId, climateId, 255);
            }
        }

        image.SaveAsPng(path);
    }

    /// <summary>
    /// 浠?TOML 鏂囦欢鍔犺浇椤圭洰銆?
    /// </summary>
    public void LoadProject(string tomlFilePath)
    {
        // 娓呯悊鏃ч」鐩姸鎬?
        RemoveCurrentTerrain();
        MaterialSlotManager.Instance.ClearAll();

        var projectManager = ProjectManager.Instance;
        if (!projectManager.OpenProject(tomlFilePath))
            return;

        var config = projectManager.LoadConfig();
        if (config == null)
            return;

        // 鎭㈠鏉愯川妲戒綅锛堣矾寰勫凡鐢?TomlProjectConfig.ReadFrom 瑙ｆ瀽涓虹粷瀵硅矾寰勶級
        foreach (var slotConfig in config.MaterialSlots)
        {
            var slot = MaterialSlotManager.Instance[slotConfig.Index];
            slot.Name = slotConfig.Name;

            if (!string.IsNullOrEmpty(slotConfig.AlbedoPath))
                slot.AlbedoTexturePath = slotConfig.AlbedoPath;
            if (!string.IsNullOrEmpty(slotConfig.NormalPath))
                slot.NormalTexturePath = slotConfig.NormalPath;
        }

        // 璁剧疆 HeightScale锛堝湪鍔犺浇楂樺害鍥惧墠锛屼互渚?LoadTerrainAsync 浣跨敤锛?
        HeightScale = config.HeightScale;

        // 鍔犺浇楂樺害鍥?
        if (!string.IsNullOrEmpty(config.HeightmapPath) && File.Exists(config.HeightmapPath))
        {
            _ = LoadTerrainAsync(config.HeightmapPath);
        }

        if (!string.IsNullOrEmpty(config.ClimateMaskPath) && File.Exists(config.ClimateMaskPath))
        {
            LoadClimateMask(config.ClimateMaskPath, markDirty: false);
        }

        // 鍔犺浇鏉愯川绱㈠紩鍥撅紙濡傛灉瀛樺湪锛?
        if (!string.IsNullOrEmpty(config.IndexMapPath) && File.Exists(config.IndexMapPath))
        {
            var loadedIndexMap = LoadMaterialIndexMap(config.IndexMapPath);
            if (loadedIndexMap != null)
            {
                MaterialIndices = loadedIndexMap;

                // Rebind render-side data reference after replacing MaterialIndices,
                // otherwise paint edits update a different byte[] than the GPU upload path.
                if (terrainEntities.Count > 0)
                {
                    terrainEntities[0].MaterialIndexMap = MaterialIndices;
                }
            }
        }

        // 閫氱煡闇€瑕佸姞杞芥潗璐ㄧ汗鐞嗭紙鐢卞閮ㄨ皟鐢?LoadMaterialTextures锛?
        MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 鍔犺浇椤圭洰鍚庤皟鐢ㄦ鏂规硶鏉ュ姞杞芥潗璐ㄧ汗鐞嗗埌 GPU銆?
    /// 闇€瑕佸湪娓叉煋绾跨▼涓皟鐢ㄣ€?
    /// </summary>
    public void LoadMaterialTextures(CommandList commandList)
    {
        MaterialSlotManager.Instance.LoadTexturesFromConfiguredPaths(graphicsDevice, commandList);
    }

    /// <summary>
    /// 浠?PNG 鏂囦欢鍔犺浇鏉愯川绱㈠紩鍥撅紝鏇挎崲褰撳墠鐨勭储寮曞浘銆?
    /// </summary>
    public bool LoadIndexMap(string path)
    {
        if (terrainEntities.Count == 0)
            return false;

        var loaded = LoadMaterialIndexMap(path);
        if (loaded == null)
            return false;

        MaterialIndices = loaded;
        terrainEntities[0].MaterialIndexMap = MaterialIndices;
        return true;
    }

    private MaterialIndexMap? LoadMaterialIndexMap(string path)
    {
        if (!File.Exists(path))
            return null;

        using var image = HeightmapImage.Load<Rgba32>(path);
        var map = new MaterialIndexMap(image.Width, image.Height);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                map.SetPixel(x, y, new MaterialPixel
                {
                    Index = pixel.R,
                    Weight = pixel.G,
                    Projection = pixel.B,
                    Rotation = pixel.A
                });
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

