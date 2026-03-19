#nullable enable

using System;
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
using System.Threading.Tasks;

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

            UpdateRenderObject(pair.Key.Entity, pair.Key, pair.Value, graphicsDevice);
        }
    }

    private bool EnsureInitialized(GraphicsDevice graphicsDevice, CommandList commandList, TerrainComponent component, TerrainRenderObject renderObject)
    {
        if (component.HeightmapTexture == null)
        {
            Log.Warning("Terrain component is missing HeightmapTexture.");
            component.IsInitialized = false;
            renderObject.Enabled = false;
            return false;
        }

        if (IsCurrentInitializationValid(component, renderObject))
        {
            return true;
        }

        if (!TryLoadTerrainData(commandList, component.HeightmapTexture, component.BaseChunkSize, out var loadedData))
        {
            component.IsInitialized = false;
            renderObject.Enabled = false;
            return false;
        }

        ApplyLoadedTerrainData(graphicsDevice, component, renderObject, loadedData);
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
        return renderObject.HeightTexture != null
            && renderObject.InstanceBuffer != null
            && renderObject.PatchVertexBuffer != null
            && renderObject.PatchIndexBuffer != null
            && renderObject.Mesh != null;
    }

    private bool TryLoadTerrainData(CommandList commandList, Texture heightmapTexture, int baseChunkSize, out LoadedTerrainData loadedData)
    {
        loadedData = default;
        try
        {
            using var image = heightmapTexture.GetDataAsImage(commandList);
            var pixelBuffer = image.PixelBuffer[0];

            if (pixelBuffer.Width < 2 || pixelBuffer.Height < 2)
            {
                Log.Warning("Terrain HeightmapTexture is too small.");
                return false;
            }

            if (!TryReadHeightData(pixelBuffer, image.Description.Format, out var heights, out var minHeight, out var maxHeight))
            {
                Log.Warning($"Terrain HeightmapTexture uses unsupported pixel format '{image.Description.Format}'.");
                return false;
            }

            int width = pixelBuffer.Width;
            int height = pixelBuffer.Height;
            int sampleExtent = Math.Max(width - 1, height - 1);
            int rootSampleSize = Math.Max(1, baseChunkSize);
            int maxLod = 0;
            while (rootSampleSize < sampleExtent)
            {
                rootSampleSize <<= 1;
                maxLod++;
            }

            loadedData = new LoadedTerrainData(
                width,
                height,
                heights,
                minHeight,
                maxHeight,
                maxLod,
                ComputeMaxLeafChunkCount(width, height, baseChunkSize),
                CreateMinMaxErrorMaps(heights, width, height, baseChunkSize, maxLod));
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning($"Terrain HeightmapTexture could not be read: {exception.Message}");
            return false;
        }
    }

    private void ApplyLoadedTerrainData(GraphicsDevice graphicsDevice, TerrainComponent component, TerrainRenderObject renderObject, LoadedTerrainData loadedData)
    {
        component.MaxLeafChunkCount = loadedData.MaxLeafChunkCount;
        component.InstanceCapacity = Math.Min(loadedData.MaxLeafChunkCount, Math.Max(1, component.MaxVisibleChunkInstances));
        component.InstanceData = new Int4[component.InstanceCapacity];

        renderObject.ReinitializeGpuResources(
            graphicsDevice,
            component.BaseChunkSize,
            loadedData.Width,
            loadedData.Height,
            loadedData.Heights,
            component.InstanceCapacity);

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
        if (renderObject.HeightTexture == null || component.MinMaxErrorMaps == null)
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
            && renderObject.HeightTexture != null
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
        if (materialPass == null || renderObject.HeightTexture == null || renderObject.InstanceBuffer == null || component.DefaultDiffuseTexture == null)
        {
            return;
        }

        var parameters = materialPass.Parameters;
        var texelSize = new Vector2(1.0f / component.HeightmapWidth, 1.0f / component.HeightmapHeight);
        var dimensionsInSamples = new Vector2(component.HeightmapWidth - 1, component.HeightmapHeight - 1);

        parameters.Set(MaterialTerrainDisplacementKeys.HeightTexture, renderObject.HeightTexture);
        parameters.Set(MaterialTerrainDisplacementKeys.InstanceBuffer, renderObject.InstanceBuffer);
        parameters.Set(MaterialTerrainDisplacementKeys.HeightTextureTexelSize, texelSize);
        parameters.Set(MaterialTerrainDisplacementKeys.HeightmapDimensionsInSamples, dimensionsInSamples);
        parameters.Set(MaterialTerrainDisplacementKeys.HeightScale, component.HeightScale);
        parameters.Set(MaterialTerrainDisplacementKeys.BaseChunkSize, component.BaseChunkSize);

        parameters.Set(TerrainMaterialStreamInitializerKeys.HeightTexture, renderObject.HeightTexture);
        parameters.Set(TerrainMaterialStreamInitializerKeys.HeightTextureTexelSize, texelSize);
        parameters.Set(TerrainMaterialStreamInitializerKeys.HeightScale, component.HeightScale);

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

        Vector3[] corners =
        {
            new(originSampleX, minHeight * component.HeightScale, originSampleY),
            new(endSampleX, minHeight * component.HeightScale, originSampleY),
            new(originSampleX, minHeight * component.HeightScale, endSampleY),
            new(endSampleX, minHeight * component.HeightScale, endSampleY),
            new(originSampleX, maxHeight * component.HeightScale, originSampleY),
            new(endSampleX, maxHeight * component.HeightScale, originSampleY),
            new(originSampleX, maxHeight * component.HeightScale, endSampleY),
            new(endSampleX, maxHeight * component.HeightScale, endSampleY),
        };

        var worldMin = new Vector3(float.MaxValue);
        var worldMax = new Vector3(float.MinValue);
        foreach (var corner in corners)
        {
            var world = Vector3.TransformCoordinate(corner, terrainWorldMatrix);
            worldMin = Vector3.Min(worldMin, world);
            worldMax = Vector3.Max(worldMax, world);
        }

        return new BoundingBox(worldMin, worldMax);
    }

    private static TerrainMinMaxErrorMap[] CreateMinMaxErrorMaps(float[] heights, int width, int height, int baseChunkSize, int maxLod)
    {
        var maps = new TerrainMinMaxErrorMap[maxLod + 1];
        int baseDimX = (width - 1 + baseChunkSize - 1) / baseChunkSize;
        int baseDimY = (height - 1 + baseChunkSize - 1) / baseChunkSize;
        maps[0] = new TerrainMinMaxErrorMap(baseDimX, baseDimY);

        var baseMap = maps[0];
        Parallel.For(0, baseMap.Height, y =>
        {
            int originY = y * baseChunkSize;
            for (int x = 0; x < baseMap.Width; x++)
            {
                int originX = x * baseChunkSize;
                ComputeMinMax(heights, width, height, originX, originY, baseChunkSize, out var minHeight, out var maxHeight);
                // The leaf patch already matches the rendered mesh resolution, so its simplification error is zero.
                baseMap.Set(x, y, minHeight, maxHeight, 0.0f);
            }
        });

        for (int lod = 1; lod <= maxLod; lod++)
        {
            var childMap = maps[lod - 1];
            var map = maps[lod] = new TerrainMinMaxErrorMap((childMap.Width + 1) / 2, (childMap.Height + 1) / 2);
            int size = baseChunkSize << lod;

            Parallel.For(0, map.Height, y =>
            {
                int childY = y * 2;
                int originY = y * size;
                for (int x = 0; x < map.Width; x++)
                {
                    int childX = x * 2;
                    int originX = x * size;

                    float minHeight = float.MaxValue;
                    float maxHeight = float.MinValue;
                    AccumulateChildMinMax(childMap, childX, childY, ref minHeight, ref maxHeight);
                    AccumulateChildMinMax(childMap, childX + 1, childY, ref minHeight, ref maxHeight);
                    AccumulateChildMinMax(childMap, childX, childY + 1, ref minHeight, ref maxHeight);
                    AccumulateChildMinMax(childMap, childX + 1, childY + 1, ref minHeight, ref maxHeight);
                    float geometricError = ComputeLocalError(heights, width, height, originX, originY, size, lod);
                    map.Set(x, y, minHeight, maxHeight, geometricError);
                }
            });
        }

        return maps;
    }

    private static int ComputeMaxLeafChunkCount(int width, int height, int baseChunkSize)
    {
        int baseDimX = (width - 1 + baseChunkSize - 1) / baseChunkSize;
        int baseDimY = (height - 1 + baseChunkSize - 1) / baseChunkSize;
        return baseDimX * baseDimY;
    }

    private static void ComputeMinMax(float[] heights, int width, int height, int originX, int originY, int size, out float minHeight, out float maxHeight)
    {
        minHeight = float.MaxValue;
        maxHeight = float.MinValue;

        int maxX = Math.Min(originX + size, width - 1);
        int maxY = Math.Min(originY + size, height - 1);
        for (int y = originY; y <= maxY; y++)
        {
            int clampedY = Math.Clamp(y, 0, height - 1);
            for (int x = originX; x <= maxX; x++)
            {
                int clampedX = Math.Clamp(x, 0, width - 1);
                float value = heights[clampedY * width + clampedX];
                minHeight = MathF.Min(minHeight, value);
                maxHeight = MathF.Max(maxHeight, value);
            }
        }
    }

    private static void AccumulateChildMinMax(TerrainMinMaxErrorMap map, int x, int y, ref float minHeight, ref float maxHeight)
    {
        if ((uint)x >= (uint)map.Width || (uint)y >= (uint)map.Height)
        {
            return;
        }

        map.Get(x, y, out var childMin, out var childMax, out _);
        minHeight = MathF.Min(minHeight, childMin);
        maxHeight = MathF.Max(maxHeight, childMax);
    }

    private static float ComputeLocalError(float[] heights, int width, int height, int originX, int originY, int size, int lod)
    {
        float maxError = 0.0f;
        int stride = 1 << lod;
        int halfStride = stride >> 1;
        int maxX = Math.Min(originX + size, width - 1);
        int maxY = Math.Min(originY + size, height - 1);

        // Horizontal edge midpoints that disappear when this LOD collapses the finer mesh.
        for (int y = originY; y <= maxY; y += stride)
        {
            for (int x = originX + halfStride; x <= maxX - halfStride; x += stride)
            {
                float actual = GetHeightClamped(heights, width, height, x, y);
                float left = GetHeightClamped(heights, width, height, x - halfStride, y);
                float right = GetHeightClamped(heights, width, height, x + halfStride, y);
                float simplified = (left + right) * 0.5f;
                maxError = MathF.Max(maxError, MathF.Abs(actual - simplified));
            }
        }

        // Vertical edge midpoints removed by the same simplification step.
        for (int y = originY + halfStride; y <= maxY - halfStride; y += stride)
        {
            for (int x = originX; x <= maxX; x += stride)
            {
                float actual = GetHeightClamped(heights, width, height, x, y);
                float top = GetHeightClamped(heights, width, height, x, y - halfStride);
                float bottom = GetHeightClamped(heights, width, height, x, y + halfStride);
                float simplified = (top + bottom) * 0.5f;
                maxError = MathF.Max(maxError, MathF.Abs(actual - simplified));
            }
        }

        // Cell centers are evaluated against the diagonal implied by the patch triangulation.
        for (int y = originY + halfStride; y <= maxY - halfStride; y += stride)
        {
            for (int x = originX + halfStride; x <= maxX - halfStride; x += stride)
            {
                float actual = GetHeightClamped(heights, width, height, x, y);
                float topLeft = GetHeightClamped(heights, width, height, x - halfStride, y - halfStride);
                float bottomRight = GetHeightClamped(heights, width, height, x + halfStride, y + halfStride);
                float simplified = (topLeft + bottomRight) * 0.5f;
                maxError = MathF.Max(maxError, MathF.Abs(actual - simplified));
            }
        }

        return maxError;
    }

    private static float GetHeightClamped(float[] heights, int width, int height, int x, int y)
    {
        int clampedX = Math.Clamp(x, 0, width - 1);
        int clampedY = Math.Clamp(y, 0, height - 1);
        return heights[clampedY * width + clampedX];
    }

    private static bool TryReadHeightData(PixelBuffer pixelBuffer, PixelFormat format, out float[] heights, out float minHeight, out float maxHeight)
    {
        switch (format)
        {
            case PixelFormat.R8_UNorm:
                return TryConvertHeights(pixelBuffer.GetPixels<byte>(), static value => value / (float)byte.MaxValue, out heights, out minHeight, out maxHeight);
            case PixelFormat.R16_UNorm:
                return TryConvertHeights(pixelBuffer.GetPixels<ushort>(), static value => value / (float)ushort.MaxValue, out heights, out minHeight, out maxHeight);
            case PixelFormat.R16_Float:
                return TryConvertHeights(pixelBuffer.GetPixels<global::System.Half>(), static value => (float)value, out heights, out minHeight, out maxHeight);
            case PixelFormat.R32_Float:
                return TryConvertHeights(pixelBuffer.GetPixels<float>(), static value => value, out heights, out minHeight, out maxHeight);
            case PixelFormat.B8G8R8A8_UNorm:
            case PixelFormat.B8G8R8A8_UNorm_SRgb:
            case PixelFormat.B8G8R8X8_UNorm:
            case PixelFormat.B8G8R8X8_UNorm_SRgb:
            case PixelFormat.R8G8B8A8_UNorm:
            case PixelFormat.R8G8B8A8_UNorm_SRgb:
                return TryConvertHeights(pixelBuffer.GetPixels<Color>(), static value => value.R / (float)byte.MaxValue, out heights, out minHeight, out maxHeight);
            default:
                heights = Array.Empty<float>();
                minHeight = 0.0f;
                maxHeight = 0.0f;
                return false;
        }
    }

    private static bool TryConvertHeights<T>(T[] source, Func<T, float> convert, out float[] heights, out float minHeight, out float maxHeight)
    {
        if (source.Length == 0)
        {
            heights = Array.Empty<float>();
            minHeight = 0.0f;
            maxHeight = 0.0f;
            return false;
        }

        heights = new float[source.Length];
        minHeight = float.MaxValue;
        maxHeight = float.MinValue;
        for (int index = 0; index < source.Length; index++)
        {
            float value = convert(source[index]);
            heights[index] = value;
            minHeight = MathF.Min(minHeight, value);
            maxHeight = MathF.Max(maxHeight, value);
        }

        return true;
    }

    private static Matrix CreateTerrainWorldMatrix(Matrix entityWorldMatrix)
    {
        entityWorldMatrix.Decompose(out _, out Matrix rotation, out var translation);
        rotation.TranslationVector = translation;
        return rotation;
    }

    private readonly record struct LoadedTerrainData(
        int Width,
        int Height,
        float[] Heights,
        float MinHeight,
        float MaxHeight,
        int MaxLod,
        int MaxLeafChunkCount,
        TerrainMinMaxErrorMap[] MinMaxErrorMaps);
}
