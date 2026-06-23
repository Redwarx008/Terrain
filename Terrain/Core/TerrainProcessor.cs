#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Terrain.Resources;
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
    // Grid Gray 128x128 contains an 8x8 checker pattern, so repeating the texture every 8 world
    // units makes each visible checker cell represent 1 meter in the editor viewport.
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

        component.QuadTree?.Dispose();
        component.QuadTree = null;
        component.MaterialManager?.Dispose();
        component.MaterialManager = null;
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
            if (!Initialize(graphicsDevice, graphicsContext.CommandList, pair.Key, pair.Value))
            {
                continue;
            }

            pair.Key.QuadTree?.ProcessPendingUploads(
                graphicsContext.CommandList,
                pair.Key.MaxStreamingUploadsPerFrame);
            UpdateRenderObject(pair.Key.Entity, pair.Key, pair.Value, graphicsDevice);
        }
    }

    private bool Initialize(GraphicsDevice graphicsDevice, CommandList commandList, TerrainComponent component, TerrainRenderObject renderObject)
    {
        if (IsCurrentInitializationValid(component, renderObject))
        {
            return true;
        }

        if (!ShouldAttemptRuntimeLoad(component))
        {
            component.IsInitialized = false;
            renderObject.Enabled = false;
            return false;
        }

        if (!TryLoadTerrainData(component, out var loadedData))
        {
            renderObject.Enabled = false;
            return false;
        }

        try
        {
            ApplyLoadedTerrainData(graphicsDevice, commandList, component, renderObject, loadedData);
            MarkRuntimeLoadSuccess(component);
            return true;
        }
        catch (Exception exception)
        {
            HandleRuntimeApplyFailure(component, renderObject, exception);
            return false;
        }
    }

    private static bool IsCurrentInitializationValid(TerrainComponent component, TerrainRenderObject renderObject)
    {
        return component.IsInitialized
            && component.LoadedConfig == TerrainConfig.Capture(component)
            && component.QuadTree != null
            && IsGpuDataValid(renderObject);
    }

    private static bool IsGpuDataValid(TerrainRenderObject renderObject)
    {
        return renderObject.HeightmapArray != null
            && renderObject.ChunkNodeBuffer != null
            && renderObject.LodLookupBuffer != null
            && renderObject.LodLookupLayoutBuffer != null
            && renderObject.LodMapTexture != null
            && renderObject.PatchVertexBuffer != null
            && renderObject.PatchIndexBuffer != null
            && renderObject.Mesh != null;
    }

    private bool TryLoadTerrainData(TerrainComponent component, out LoadedTerrainData loadedData)
    {
        return TryLoadRuntimeData(
            component,
            () =>
            {
                TerrainRuntimeResourceBundle bundle = component.RuntimeResourceBundle ?? LoadRuntimeResourceBundle();
                foreach (string diagnostic in bundle.Diagnostics)
                {
                    Log.Warning(diagnostic);
                }

                return bundle;
            },
            out loadedData,
            logError: message => Log.Error($"Terrain runtime resources could not be read: {message}"));
    }

    internal static bool ShouldAttemptRuntimeLoad(TerrainComponent component)
    {
        if (!component.HasRuntimeLoadFailure)
            return true;

        return component.FailedRuntimeLoadConfig != TerrainConfig.Capture(component);
    }

    internal static void MarkRuntimeLoadFailure(TerrainComponent component)
    {
        component.HasRuntimeLoadFailure = true;
        component.FailedRuntimeLoadConfig = TerrainConfig.Capture(component);
    }

    internal static void MarkRuntimeLoadSuccess(TerrainComponent component)
    {
        component.HasRuntimeLoadFailure = false;
        component.FailedRuntimeLoadConfig = default;
    }

    internal static bool TryLoadRuntimeData(
        TerrainComponent component,
        Func<TerrainRuntimeResourceBundle> bundleLoader,
        out LoadedTerrainData loadedData,
        Func<string, ITerrainFileReader>? fileReaderFactory = null,
        Action<string>? logError = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(bundleLoader);

        loadedData = default;
        try
        {
            TerrainRuntimeResourceBundle bundle = bundleLoader();
            component.HeightScale = bundle.HeightScale;
            loadedData = CreateLoadedTerrainData(component, bundle, fileReaderFactory);
            return true;
        }
        catch (Exception exception)
        {
            MarkRuntimeLoadFailure(component);
            component.IsInitialized = false;
            logError?.Invoke(FormatRuntimeLoadFailure(exception));
            return false;
        }
    }

    private static void HandleRuntimeApplyFailure(TerrainComponent component, TerrainRenderObject renderObject, Exception exception)
    {
        MarkRuntimeLoadFailure(component);
        component.IsInitialized = false;
        component.QuadTree?.Dispose();
        component.QuadTree = null;
        component.MaterialManager?.Dispose();
        component.MaterialManager = null;
        renderObject.Enabled = false;
        Log.Error($"Terrain runtime resources could not be read: {FormatRuntimeLoadFailure(exception)}");
    }

    private static string FormatRuntimeLoadFailure(Exception exception)
    {
        if (exception is FileNotFoundException { FileName: { Length: > 0 } fileName })
            return $"{exception.Message} ({fileName})";

        return exception.Message;
    }

    internal static LoadedTerrainData CreateLoadedTerrainData(
        TerrainComponent component,
        TerrainRuntimeResourceBundle bundle,
        Func<string, ITerrainFileReader>? fileReaderFactory = null)
    {
        fileReaderFactory ??= static path => new TerrainFileReader(path);

        ITerrainFileReader? fileReader = null;
        bool ownsReader = false;
        try
        {
            fileReader = fileReaderFactory(bundle.TerrainDataPath);
            ownsReader = true;

            TerrainMinMaxErrorMap[] minMaxErrorMaps = fileReader.ReadAllMinMaxErrorMaps();
            if (minMaxErrorMaps.Length == 0)
                throw new InvalidDataException($"Terrain data '{bundle.TerrainDataPath}' does not contain any MinMaxErrorMaps.");

            minMaxErrorMaps[^1].GetGlobalMinMax(out var minHeight, out var maxHeight);
            int maxLod = minMaxErrorMaps.Length - 1;
            int baseChunkSize = fileReader.Header.LeafNodeSize;
            int maxResidentChunks = Math.Max(component.MaxResidentChunks, minMaxErrorMaps[maxLod].Width * minMaxErrorMaps[maxLod].Height);

            var loadedData = new LoadedTerrainData(
                fileReader,
                bundle.MaterialTextureSlots,
                fileReader.Header.Width,
                fileReader.Header.Height,
                minHeight,
                maxHeight,
                maxLod,
                baseChunkSize,
                fileReader.HeightmapHeader.TileSize,
                fileReader.HeightmapHeader.Padding,
                fileReader.DetailIndexMapHeader.TileSize,
                fileReader.DetailIndexMapHeader.Padding,
                maxResidentChunks,
                minMaxErrorMaps);

            ownsReader = false;
            return loadedData;
        }
        finally
        {
            if (ownsReader)
            {
                fileReader?.Dispose();
            }
        }
    }

    private void ApplyLoadedTerrainData(GraphicsDevice graphicsDevice, CommandList commandList, TerrainComponent component, TerrainRenderObject renderObject, LoadedTerrainData loadedData)
    {
        component.QuadTree?.Dispose();
        component.QuadTree = null;

        int chunkNodeCapacity = ComputeChunkNodeCapacity(loadedData.MinMaxErrorMaps, component.MaxVisibleChunkInstances);
        component.ChunkNodeData = new TerrainChunkNode[chunkNodeCapacity];
        int lodLookupEntryCount = ComputeLodLookupEntryCount(loadedData.MinMaxErrorMaps);
        component.BaseChunkSize = loadedData.BaseChunkSize;
        component.HeightmapTileSize = loadedData.HeightmapTileSize;
        component.HeightmapTilePadding = loadedData.HeightmapTilePadding;
        component.SplatmapTileSize = loadedData.SplatmapTileSize;
        component.SplatmapTilePadding = loadedData.SplatmapTilePadding;
        var lodLookupLayouts = CreateLodLookupLayouts(loadedData.MinMaxErrorMaps);

        renderObject.ReinitializeGpuResources(
            graphicsDevice,
            component.BaseChunkSize,
            loadedData.Width,
            loadedData.Height,
            component.HeightmapTileSize,
            component.HeightmapTilePadding,
            component.SplatmapTileSize,
            component.SplatmapTilePadding,
            loadedData.MaxResidentChunks,
            chunkNodeCapacity,
            lodLookupLayouts.Length,
            lodLookupEntryCount);
        renderObject.InitializeLodLookupData(commandList, lodLookupEntryCount);
        renderObject.UpdateLodLookupLayoutData(commandList, lodLookupLayouts);

        Debug.Assert(renderObject.HeightmapArray != null);

        var gpuHeightArray = new GpuVirtualTextureArray(renderObject.HeightmapArray!, component.HeightmapTileSize, component.HeightmapTilePadding, loadedData.MaxResidentChunks);
        GpuVirtualTextureArray? gpuDetailIndexArray = null;
        if (renderObject.DetailIndexMapArray != null)
        {
            gpuDetailIndexArray = new GpuVirtualTextureArray(renderObject.DetailIndexMapArray, component.SplatmapTileSize, component.SplatmapTilePadding, loadedData.MaxResidentChunks);
        }

        TerrainStreamingManager? streamingManager = null;
        try
        {
            streamingManager = new TerrainStreamingManager(
                loadedData.FileReader,
                gpuHeightArray,
                gpuDetailIndexArray,
                renderObject.DetailWeightMapArray,
                component.BaseChunkSize);

            component.HeightmapWidth = loadedData.Width;
            component.HeightmapHeight = loadedData.Height;
            component.MaxLod = loadedData.MaxLod;
            component.MinHeight = loadedData.MinHeight;
            component.MaxHeight = loadedData.MaxHeight;
            component.MinMaxErrorMaps = loadedData.MinMaxErrorMaps;
            var attachedStreamingManager = streamingManager;
            component.QuadTree = new TerrainQuadTree(
                loadedData.MinMaxErrorMaps,
                loadedData.BaseChunkSize,
                loadedData.Width,
                loadedData.Height,
                component,
                attachedStreamingManager);
            streamingManager = null;

            attachedStreamingManager.PreloadTopLevelChunks(commandList, loadedData.MinMaxErrorMaps[loadedData.MaxLod]);
        }
        catch
        {
            streamingManager?.Dispose();
            throw;
        }

        component.MaterialManager?.Dispose();
        component.MaterialManager = null;
        component.MaterialManager = new RuntimeMaterialManager();
        component.MaterialManager.Initialize(
            graphicsDevice,
            commandList,
            loadedData.MaterialTextureSlots);

        // Reinitialization replaces the underlying GPU resources, so the old "material is ready" markers
        // must be cleared or EnsureMaterial() will incorrectly reuse a pass bound to stale buffers/textures.
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
        Debug.Assert(renderObject.HeightmapArray != null);
        Debug.Assert(component.QuadTree != null);
        Debug.Assert(renderObject.ChunkNodeBuffer != null);

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
        if (renderObject.MaterialPass != null
            && renderObject.HeightmapArray != null
            && renderObject.ChunkNodeBuffer != null
            && component.IsInitialized)
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
        return true;
    }

    private void UpdateMaterialParameters(TerrainComponent component, TerrainRenderObject renderObject, GraphicsDevice graphicsDevice)
    {
        var materialPass = renderObject.MaterialPass;
        if (materialPass == null)
        {
            return;
        }

        Debug.Assert(renderObject.HeightmapArray != null);
        Debug.Assert(renderObject.ChunkNodeBuffer != null);

        var parameters = materialPass.Parameters;
        var dimensionsInSamples = new Vector2(component.HeightmapWidth - 1, component.HeightmapHeight - 1);
        parameters.Set(TerrainHeightParametersKeys.HeightmapArray, renderObject.HeightmapArray!);
        parameters.Set(TerrainHeightParametersKeys.HeightScale, component.HeightScale);
        parameters.Set(TerrainHeightParametersKeys.BaseChunkSize, component.BaseChunkSize);
        parameters.Set(TerrainHeightParametersKeys.HeightmapTileSize, component.HeightmapTileSize);
        parameters.Set(TerrainHeightParametersKeys.HeightmapTilePadding, component.HeightmapTilePadding);

        parameters.Set(MaterialTerrainDisplacementKeys.InstanceBuffer, renderObject.ChunkNodeBuffer!);
        parameters.Set(MaterialTerrainDisplacementKeys.HeightmapDimensionsInSamples, dimensionsInSamples);

        // IndexMap / material shader parameters
        parameters.Set(MaterialTerrainDiffuseKeys.DetailIndexMapArray, renderObject.DetailIndexMapArray);
        parameters.Set(MaterialTerrainDiffuseKeys.DetailWeightMapArray, renderObject.DetailWeightMapArray);
        parameters.Set(MaterialTerrainDiffuseKeys.MaterialIndexSampler, graphicsDevice.SamplerStates.PointClamp);
        parameters.Set(MaterialTerrainDiffuseKeys.MaterialAlbedoSampler, graphicsDevice.SamplerStates.LinearWrap);
        parameters.Set(MaterialTerrainDiffuseKeys.MaterialNormalSampler, graphicsDevice.SamplerStates.LinearWrap);
        parameters.Set(MaterialTerrainDiffuseKeys.MaterialPropertiesSampler, graphicsDevice.SamplerStates.LinearWrap);
        parameters.Set(MaterialTerrainDiffuseKeys.MaterialTilingScale, 1.0f);
        parameters.Set(MaterialTerrainDiffuseKeys.DetailBlendRange, 0.5f);
        parameters.Set(MaterialTerrainDiffuseKeys.SplatmapTileSize, component.SplatmapTileSize);
        parameters.Set(MaterialTerrainDiffuseKeys.SplatmapTilePadding, component.SplatmapTilePadding);

        // Bind material texture arrays from RuntimeMaterialManager
        var materialManager = component.MaterialManager;
        if (materialManager != null)
        {
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialDiffuseHeightArray, materialManager.DiffuseHeightArray);
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialNormalArray, materialManager.NormalArray);
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialPropertiesArray, materialManager.PropertiesArray);
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialArraySize, materialManager.DiffuseHeightArray?.ArraySize ?? 0);
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialNormalArraySize, materialManager.NormalArray?.ArraySize ?? 0);
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialPropertiesArraySize, materialManager.PropertiesArray?.ArraySize ?? 0);
        }
        else
        {
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialArraySize, 0);
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialNormalArraySize, 0);
            parameters.Set(MaterialTerrainDiffuseKeys.MaterialPropertiesArraySize, 0);
        }
    }

    private void UpdateBounds(Matrix terrainWorldMatrix, TerrainComponent component, TerrainRenderObject renderObject)
    {
        // The terrain can be drawn by shadow views that see different chunks than the main camera,
        // so shrinking bounds to the current selection would cull shadow-casting terrain too early.
        float worldHeightScale = component.HeightScale * TerrainComponent.HeightSampleNormalization;
        var terrainOffset = terrainWorldMatrix.TranslationVector;
        var boundsMin = new Vector3(
            terrainOffset.X,
            terrainOffset.Y + component.MinHeight * worldHeightScale,
            terrainOffset.Z);
        var boundsMax = new Vector3(
            terrainOffset.X + component.HeightmapWidth - 1,
            terrainOffset.Y + component.MaxHeight * worldHeightScale,
            terrainOffset.Z + component.HeightmapHeight - 1);
        renderObject.BoundingBox = (BoundingBoxExt)new BoundingBox(boundsMin, boundsMax);
    }

    private static int ComputeChunkNodeCapacity(TerrainMinMaxErrorMap[] minMaxErrorMaps, int maxVisibleChunkInstances)
    {
        // Total nodes = sum of all LOD levels' chunk counts
        int totalNodeCount = 0;
        foreach (var map in minMaxErrorMaps)
        {
            totalNodeCount += map.Width * map.Height;
        }

        // Add buffer for visible instances (render nodes) plus internal nodes
        return Math.Max(totalNodeCount, maxVisibleChunkInstances);
    }

    private static int ComputeLodLookupEntryCount(TerrainMinMaxErrorMap[] minMaxErrorMaps)
    {
        int totalNodeCount = 0;
        foreach (var map in minMaxErrorMaps)
        {
            totalNodeCount += map.Width * map.Height;
        }

        return totalNodeCount;
    }

    private static TerrainLodLookupLayout[] CreateLodLookupLayouts(TerrainMinMaxErrorMap[] minMaxErrorMaps)
    {
        var layouts = new TerrainLodLookupLayout[minMaxErrorMaps.Length];
        int offset = 0;
        for (int lodLevel = 0; lodLevel < minMaxErrorMaps.Length; lodLevel++)
        {
            var map = minMaxErrorMaps[lodLevel];
            layouts[lodLevel] = new TerrainLodLookupLayout
            {
                LayoutInfo = new Int4(offset, map.Width, map.Height, 0),
            };

            offset += map.Width * map.Height;
        }

        return layouts;
    }

    private static Matrix CreateTerrainWorldMatrix(Matrix entityWorldMatrix)
    {
        return Matrix.Translation(entityWorldMatrix.TranslationVector);
    }

    private static TerrainRuntimeResourceBundle LoadRuntimeResourceBundle()
    {
        var resolver = GameResourceResolverBootstrap.CreateForTerrainAssemblyDirectory();
        return new GameRuntimeResourceBootstrap(resolver).Load();
    }

    internal readonly record struct LoadedTerrainData(
        ITerrainFileReader FileReader,
        IReadOnlyList<RuntimeMaterialTextureSlot> MaterialTextureSlots,
        int Width,
        int Height,
        float MinHeight,
        float MaxHeight,
        int MaxLod,
        int BaseChunkSize,
        int HeightmapTileSize,
        int HeightmapTilePadding,
        int SplatmapTileSize,
        int SplatmapTilePadding,
        int MaxResidentChunks,
        TerrainMinMaxErrorMap[] MinMaxErrorMaps);
}
