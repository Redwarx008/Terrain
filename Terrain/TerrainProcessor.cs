#nullable enable

using System;
using System.IO;
using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace Terrain;

public sealed class TerrainProcessor : EntityProcessor<TerrainComponent, TerrainRenderObject>, IEntityComponentRenderProcessor
{
    private const float DiffuseWorldRepeatSize = 8.0f;
    private static readonly Logger Log = GlobalLogger.GetLogger("Quantum");

    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override TerrainRenderObject GenerateComponentData([NotNull] Entity entity, [NotNull] TerrainComponent component)
    {
        return new TerrainRenderObject
        {
            Source = component,
        };
    }

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] TerrainComponent component, [NotNull] TerrainRenderObject renderObject)
    {
        if (component.IsRegisteredWithVisibilityGroup)
        {
            VisibilityGroup.RenderObjects.Remove(renderObject);
            component.IsRegisteredWithVisibilityGroup = false;
        }

        component.StreamingManager?.Dispose();
        component.StreamingManager = null;
        renderObject.Dispose();
        base.OnEntityComponentRemoved(entity, component, renderObject);
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var graphicsDevice = Services.GetService<IGraphicsDeviceService>()?.GraphicsDevice;
        var graphicsContext = Services.GetService<IGame>()?.GraphicsContext;
        if (graphicsDevice == null || graphicsContext == null)
        {
            return;
        }

        foreach (var pair in ComponentDatas)
        {
            if (!EnsureInitialized(graphicsDevice, graphicsContext.CommandList, pair.Key, pair.Value))
            {
                continue;
            }

            pair.Key.StreamingManager?.ProcessPendingUploads(
                graphicsContext.CommandList,
                pair.Key.MaxStreamingUploadsPerFrame);
            UpdateRenderObject(pair.Key.Entity, pair.Key, pair.Value, graphicsDevice);
        }
    }

    private bool EnsureInitialized(GraphicsDevice graphicsDevice, CommandList commandList, TerrainComponent component, TerrainRenderObject renderObject)
    {
        if (string.IsNullOrWhiteSpace(component.TerrainDataPath))
        {
            Log.Warning("Terrain component is missing TerrainDataPath.");
            component.IsInitialized = false;
            renderObject.Enabled = false;
            return false;
        }

        if (IsCurrentInitializationValid(component, renderObject))
        {
            return true;
        }

        if (!TryLoadTerrainData(component, out var loadedData))
        {
            component.IsInitialized = false;
            renderObject.Enabled = false;
            return false;
        }

        ApplyLoadedTerrainData(graphicsDevice, commandList, component, renderObject, loadedData);
        return true;
    }

    private static bool IsCurrentInitializationValid(TerrainComponent component, TerrainRenderObject renderObject)
    {
        return component.IsInitialized
            && component.LoadedConfig == TerrainConfig.Capture(component)
            && IsGpuDataValid(renderObject);
    }

    private static bool IsGpuDataValid(TerrainRenderObject renderObject)
    {
        return renderObject.HeightmapArray != null
            && renderObject.InstanceBuffer != null
            && renderObject.LodMapTexture != null
            && renderObject.PatchVertexBuffer != null
            && renderObject.PatchIndexBuffer != null
            && renderObject.Mesh != null;
    }

    private bool TryLoadTerrainData(TerrainComponent component, out LoadedTerrainData loadedData)
    {
        loadedData = default;
        try
        {
            string terrainDataPath = ResolveTerrainDataPath(component.TerrainDataPath!);
            ValidateTerrainDataPath(terrainDataPath);
            var fileReader = new TerrainFileReader(terrainDataPath);
            var minMaxErrorMaps = fileReader.ReadAllMinMaxErrorMaps();
            if (minMaxErrorMaps.Length == 0)
            {
                fileReader.Dispose();
                Log.Warning($"Terrain data '{terrainDataPath}' does not contain any MinMaxErrorMaps.");
                return false;
            }

            minMaxErrorMaps[^1].GetGlobalMinMax(out var minHeight, out var maxHeight);
            int maxLod = minMaxErrorMaps.Length - 1;
            int baseChunkSize = fileReader.Header.LeafNodeSize;
            int maxResidentChunks = Math.Max(component.MaxResidentChunks, minMaxErrorMaps[maxLod].Width * minMaxErrorMaps[maxLod].Height);
            loadedData = new LoadedTerrainData(
                terrainDataPath,
                fileReader,
                fileReader.Header.Width,
                fileReader.Header.Height,
                minHeight,
                maxHeight,
                maxLod,
                baseChunkSize,
                fileReader.HeightmapHeader.TileSize,
                fileReader.HeightmapHeader.Padding,
                maxResidentChunks,
                ComputeMaxLeafChunkCount(fileReader.Header.Width, fileReader.Header.Height, baseChunkSize),
                minMaxErrorMaps);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning($"Terrain data could not be read: {exception.Message}");
            return false;
        }
    }

    private void ApplyLoadedTerrainData(GraphicsDevice graphicsDevice, CommandList commandList, TerrainComponent component, TerrainRenderObject renderObject, LoadedTerrainData loadedData)
    {
        component.StreamingManager?.Dispose();
        component.StreamingManager = null;

        component.MaxLeafChunkCount = loadedData.MaxLeafChunkCount;
        component.InstanceCapacity = Math.Min(loadedData.MaxLeafChunkCount, Math.Max(1, component.MaxVisibleChunkInstances));
        component.InstanceData = new TerrainChunkInstance[component.InstanceCapacity];
        component.BaseChunkSize = loadedData.BaseChunkSize;
        component.HeightmapTileSize = loadedData.HeightmapTileSize;
        component.HeightmapTilePadding = loadedData.HeightmapTilePadding;

        renderObject.ReinitializeGpuResources(
            graphicsDevice,
            component.BaseChunkSize,
            loadedData.Width,
            loadedData.Height,
            loadedData.HeightmapTileSize,
            loadedData.HeightmapTilePadding,
            loadedData.MaxResidentChunks,
            component.InstanceCapacity);

        if (renderObject.HeightmapArray == null)
        {
            throw new InvalidOperationException("Terrain heightmap array was not created.");
        }

        var gpuHeightArray = new GpuHeightArray(renderObject.HeightmapArray, loadedData.HeightmapTileSize, loadedData.HeightmapTilePadding, loadedData.MaxResidentChunks);
        var streamingManager = new TerrainStreamingManager(loadedData.FileReader, gpuHeightArray, component.BaseChunkSize);
        streamingManager.PreloadTopLevelChunks(commandList, loadedData.MinMaxErrorMaps[loadedData.MaxLod]);
        component.StreamingManager = streamingManager;

        component.HeightmapWidth = loadedData.Width;
        component.HeightmapHeight = loadedData.Height;
        component.MaxLod = loadedData.MaxLod;
        component.MinHeight = loadedData.MinHeight;
        component.MaxHeight = loadedData.MaxHeight;
        component.MinMaxErrorMaps = loadedData.MinMaxErrorMaps;

        // Reinitialization replaces the underlying GPU resources, so the old "material is ready" markers
        // must be cleared or EnsureMaterial() will incorrectly reuse a pass bound to stale buffers/textures.
        component.LoadedDiffuseTexture = null;
        renderObject.ResetRenderState();

        // Reinitialization only swaps GPU resources; the same render object must stay registered exactly once.
        if (!component.IsRegisteredWithVisibilityGroup)
        {
            VisibilityGroup.RenderObjects.Add(renderObject);
            component.IsRegisteredWithVisibilityGroup = true;
        }

        component.LoadedConfig = TerrainConfig.Capture(component);
        component.IsInitialized = true;
    }

    private void UpdateRenderObject(Entity entity, TerrainComponent component, TerrainRenderObject renderObject, GraphicsDevice graphicsDevice)
    {
        if (renderObject.HeightmapArray == null || component.MinMaxErrorMaps == null || component.StreamingManager == null)
        {
            return;
        }

        if (!EnsureMaterial(graphicsDevice, component, renderObject))
        {
            renderObject.Enabled = false;
            return;
        }

        entity.Transform.UpdateWorldMatrix();
        var terrainWorldMatrix = CreateTerrainWorldMatrix(entity.Transform.WorldMatrix);
        renderObject.Enabled = component.Enabled;
        renderObject.RenderGroup = component.RenderGroup;
        renderObject.World = terrainWorldMatrix;
        renderObject.IsScalingNegative = false;
        renderObject.IsShadowCaster = component.CastShadows;

        UpdateBounds(terrainWorldMatrix, component, renderObject);
        UpdateMaterialParameters(component, renderObject, graphicsDevice);
    }

    private bool EnsureMaterial(GraphicsDevice graphicsDevice, TerrainComponent component, TerrainRenderObject renderObject)
    {
        if (component.DefaultDiffuseTexture == null)
        {
            Log.Warning("Terrain component is missing DefaultDiffuseTexture.");
            return false;
        }

        if (renderObject.MaterialPass != null
            && renderObject.HeightmapArray != null
            && renderObject.InstanceBuffer != null
            && ReferenceEquals(component.LoadedDiffuseTexture, component.DefaultDiffuseTexture))
        {
            return true;
        }

        var descriptor = new MaterialDescriptor();
        descriptor.Attributes.Diffuse = new MaterialTerrainDiffuseFeature();
        descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
        descriptor.Attributes.Displacement = new MaterialTerrainDisplacementFeature();
        descriptor.Attributes.MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(0.12f));
        descriptor.Attributes.Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0.0f));
        descriptor.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();

        var material = Material.New(graphicsDevice, descriptor);
        renderObject.MaterialPass = material.Passes[0];
        component.LoadedDiffuseTexture = component.DefaultDiffuseTexture;
        return true;
    }

    private void UpdateMaterialParameters(TerrainComponent component, TerrainRenderObject renderObject, GraphicsDevice graphicsDevice)
    {
        var materialPass = renderObject.MaterialPass;
        if (materialPass == null || renderObject.HeightmapArray == null || renderObject.InstanceBuffer == null || component.DefaultDiffuseTexture == null)
        {
            return;
        }

        var parameters = materialPass.Parameters;
        var dimensionsInSamples = new Vector2(component.HeightmapWidth - 1, component.HeightmapHeight - 1);
        parameters.Set(TerrainHeightParametersKeys.HeightmapArray, renderObject.HeightmapArray);
        parameters.Set(TerrainHeightParametersKeys.HeightScale, component.HeightScale);
        parameters.Set(TerrainHeightParametersKeys.BaseChunkSize, component.BaseChunkSize);
        parameters.Set(TerrainHeightParametersKeys.HeightmapTileSize, component.HeightmapTileSize);
        parameters.Set(TerrainHeightParametersKeys.HeightmapTilePadding, component.HeightmapTilePadding);

        parameters.Set(MaterialTerrainDisplacementKeys.InstanceBuffer, renderObject.InstanceBuffer);
        parameters.Set(MaterialTerrainDisplacementKeys.HeightmapDimensionsInSamples, dimensionsInSamples);

        parameters.Set(MaterialTerrainDiffuseKeys.DefaultDiffuseTexture, component.DefaultDiffuseTexture);
        parameters.Set(MaterialTerrainDiffuseKeys.TerrainDiffuseRepeatSampler, graphicsDevice.SamplerStates.LinearWrap);
        parameters.Set(MaterialTerrainDiffuseKeys.DiffuseWorldRepeatSize, DiffuseWorldRepeatSize);
        parameters.Set(MaterialTerrainDiffuseKeys.BaseColor, component.BaseColor);
    }

    private void UpdateBounds(Matrix terrainWorldMatrix, TerrainComponent component, TerrainRenderObject renderObject)
    {
        // The terrain can be drawn by shadow views that see different chunks than the main camera,
        // so shrinking bounds to the current selection would cull shadow-casting terrain too early.
        int terrainSampleExtent = Math.Max(component.HeightmapWidth - 1, component.HeightmapHeight - 1);
        var fullTerrainBounds = ComputeWorldBounds(
            terrainWorldMatrix,
            component,
            0,
            0,
            terrainSampleExtent,
            component.MinHeight,
            component.MaxHeight);
        renderObject.BoundingBox = (BoundingBoxExt)fullTerrainBounds;
    }

    private static BoundingBox ComputeWorldBounds(Matrix terrainWorldMatrix, TerrainComponent component, int originSampleX, int originSampleY, int sizeInSamples, float minHeight, float maxHeight)
    {
        int endSampleX = Math.Min(originSampleX + sizeInSamples, component.HeightmapWidth - 1);
        int endSampleY = Math.Min(originSampleY + sizeInSamples, component.HeightmapHeight - 1);
        float worldHeightScale = component.HeightScale * TerrainComponent.HeightSampleNormalization;
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(originSampleX, minHeight * worldHeightScale, originSampleY);
        corners[1] = new Vector3(endSampleX, minHeight * worldHeightScale, originSampleY);
        corners[2] = new Vector3(originSampleX, minHeight * worldHeightScale, endSampleY);
        corners[3] = new Vector3(endSampleX, minHeight * worldHeightScale, endSampleY);
        corners[4] = new Vector3(originSampleX, maxHeight * worldHeightScale, originSampleY);
        corners[5] = new Vector3(endSampleX, maxHeight * worldHeightScale, originSampleY);
        corners[6] = new Vector3(originSampleX, maxHeight * worldHeightScale, endSampleY);
        corners[7] = new Vector3(endSampleX, maxHeight * worldHeightScale, endSampleY);

        var worldMin = new Vector3(float.MaxValue);
        var worldMax = new Vector3(float.MinValue);
        foreach (ref readonly var corner in corners)
        {
            var world = Vector3.TransformCoordinate(corner, terrainWorldMatrix);
            worldMin = Vector3.Min(worldMin, world);
            worldMax = Vector3.Max(worldMax, world);
        }

        return new BoundingBox(worldMin, worldMax);
    }

    private static int ComputeMaxLeafChunkCount(int width, int height, int baseChunkSize)
    {
        int baseDimX = (width - 1 + baseChunkSize - 1) / baseChunkSize;
        int baseDimY = (height - 1 + baseChunkSize - 1) / baseChunkSize;
        return baseDimX * baseDimY;
    }

    private static Matrix CreateTerrainWorldMatrix(Matrix entityWorldMatrix)
    {
        entityWorldMatrix.Decompose(out _, out Matrix rotation, out var translation);
        rotation.TranslationVector = translation;
        return rotation;
    }

    private static string ResolveTerrainDataPath(string terrainDataPath)
    {
        terrainDataPath = terrainDataPath.Trim().Trim('"');
        string fullPath = Path.IsPathRooted(terrainDataPath)
            ? terrainDataPath
            : Path.Combine(AppContext.BaseDirectory, terrainDataPath);
        return Path.GetFullPath(fullPath);
    }

    private static void ValidateTerrainDataPath(string terrainDataPath)
    {
        if (!string.Equals(Path.GetExtension(terrainDataPath), ".terrain", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Terrain data path '{terrainDataPath}' must point to a .terrain file.");
        }

        if (!File.Exists(terrainDataPath))
        {
            throw new FileNotFoundException("Terrain data file was not found.", terrainDataPath);
        }
    }

    private readonly record struct LoadedTerrainData(
        string TerrainDataPath,
        TerrainFileReader FileReader,
        int Width,
        int Height,
        float MinHeight,
        float MaxHeight,
        int MaxLod,
        int BaseChunkSize,
        int HeightmapTileSize,
        int HeightmapTilePadding,
        int MaxResidentChunks,
        int MaxLeafChunkCount,
        TerrainMinMaxErrorMap[] MinMaxErrorMaps);
}
