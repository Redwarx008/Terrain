#nullable enable

using System;
using System.Drawing;
using System.IO;
using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain;

public sealed class TerrainProcessor : EntityProcessor<TerrainComponent, TerrainRuntimeData>, IEntityComponentRenderProcessor
{
    private const float DiffuseWorldRepeatSize = 8.0f;
    private static readonly Logger Log = GlobalLogger.GetLogger(nameof(TerrainProcessor));

    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override TerrainRuntimeData GenerateComponentData([NotNull] Entity entity, [NotNull] TerrainComponent component) => new();

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] TerrainComponent component, [NotNull] TerrainRuntimeData data)
    {
        if (data.RenderObject != null)
        {
            VisibilityGroup.RenderObjects.Remove(data.RenderObject);
            data.RenderObject = null;
        }

        data.Dispose();
        base.OnEntityComponentRemoved(entity, component, data);
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var graphicsDevice = Services.GetService<IGraphicsDeviceService>()!.GraphicsDevice;
        var camera = context.GetCurrentCamera();
        if (camera == null)
        {
            return;
        }

        float aspectRatio = graphicsDevice.Presenter.BackBuffer.Width / (float)Math.Max(1, graphicsDevice.Presenter.BackBuffer.Height);
        camera.Update(aspectRatio);

        foreach (var pair in ComponentDatas)
        {
            if (!EnsureInitialized(graphicsDevice, pair.Key, pair.Value))
            {
                continue;
            }

            UpdateRenderObject(pair.Key.Entity, pair.Key, pair.Value, camera, graphicsDevice);
        }
    }

    private bool EnsureInitialized(GraphicsDevice graphicsDevice, TerrainComponent component, TerrainRuntimeData data)
    {
        string resolvedPath = ResolveHeightmapPath(component.HeightmapPath);
        if (data.IsInitialized
            && string.Equals(data.LoadedPath, resolvedPath, StringComparison.OrdinalIgnoreCase)
            && data.LoadedBaseChunkSize == component.BaseChunkSize)
        {
            return true;
        }

        if (!File.Exists(resolvedPath))
        {
            if (data.RenderObject != null)
            {
                data.RenderObject.Enabled = false;
            }

            Log.Warning($"Terrain heightmap was not found at '{resolvedPath}'.");
            return false;
        }

        if (data.RenderObject != null)
        {
            VisibilityGroup.RenderObjects.Remove(data.RenderObject);
            data.RenderObject = null;
        }

        data.Dispose();
        data.IsInitialized = false;
        data.LoadedPath = null;
        data.LoadedBaseChunkSize = 0;

        using var bitmap = new Bitmap(resolvedPath);
        if (bitmap.Width < 2 || bitmap.Height < 2)
        {
            Log.Warning($"Terrain heightmap '{resolvedPath}' is too small.");
            return false;
        }

        int width = bitmap.Width;
        int height = bitmap.Height;
        var heights = new float[width * height];
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = bitmap.GetPixel(x, y).R / 255.0f;
                heights[y * width + x] = value;
                minHeight = MathF.Min(minHeight, value);
                maxHeight = MathF.Max(maxHeight, value);
            }
        }

        int sampleExtent = Math.Max(width - 1, height - 1);
        int rootSampleSize = Math.Max(1, component.BaseChunkSize);
        int maxLod = 0;
        while (rootSampleSize < sampleExtent)
        {
            rootSampleSize <<= 1;
            maxLod++;
        }

        data.HeightmapWidth = width;
        data.HeightmapHeight = height;
        data.MaxLod = maxLod;
        data.MinHeight = minHeight;
        data.MaxHeight = maxHeight;
        data.MinMaxErrorMaps = CreateMinMaxErrorMaps(heights, width, height, component.BaseChunkSize, maxLod);

        data.HeightTexture = Texture.New2D(
            graphicsDevice,
            width,
            height,
            PixelFormat.R32_Float,
            heights,
            TextureFlags.ShaderResource);

        CreatePatchDraw(graphicsDevice, component.BaseChunkSize, data);
        data.RenderObject = CreateRenderObject(data);
        VisibilityGroup.RenderObjects.Add(data.RenderObject);

        data.LoadedPath = resolvedPath;
        data.LoadedBaseChunkSize = component.BaseChunkSize;
        data.IsInitialized = true;
        return true;
    }

    private void UpdateRenderObject(Entity entity, TerrainComponent component, TerrainRuntimeData data, CameraComponent camera, GraphicsDevice graphicsDevice)
    {
        if (data.RenderObject == null || data.HeightTexture == null || data.MinMaxErrorMaps == null)
        {
            return;
        }

        if (!EnsureMaterial(graphicsDevice, component, data))
        {
            data.RenderObject.Enabled = false;
            return;
        }

        entity.Transform.UpdateWorldMatrix();
        var terrainWorldMatrix = CreateTerrainWorldMatrix(entity.Transform.WorldMatrix);
        var renderObject = data.RenderObject;
        renderObject.MaterialPass = data.RuntimeMaterial!.Passes[0];
        renderObject.Enabled = component.Enabled;
        renderObject.RenderGroup = component.RenderGroup;
        renderObject.World = terrainWorldMatrix;
        renderObject.IsScalingNegative = false;
        renderObject.IsShadowCaster = component.CastShadows;
        renderObject.HeightTextureAsset = data.HeightTexture;

        SelectChunks(terrainWorldMatrix, component, data, camera, graphicsDevice);
        UploadChunkBuffer(data);
        UpdateBounds(terrainWorldMatrix, component, data);
        UpdateMaterialParameters(component, data);
    }

    private bool EnsureMaterial(GraphicsDevice graphicsDevice, TerrainComponent component, TerrainRuntimeData data)
    {
        if (component.DefaultDiffuseTexture == null)
        {
            Log.Warning("Terrain component is missing DefaultDiffuseTexture.");
            return false;
        }

        if (data.RuntimeMaterial != null && ReferenceEquals(data.LoadedDiffuseTexture, component.DefaultDiffuseTexture))
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

        data.RuntimeMaterial = Material.New(graphicsDevice, descriptor);
        data.LoadedDiffuseTexture = component.DefaultDiffuseTexture;
        return true;
    }

    private void UpdateMaterialParameters(TerrainComponent component, TerrainRuntimeData data)
    {
        var renderObject = data.RenderObject;
        var materialPass = renderObject?.MaterialPass;
        if (materialPass == null || data.HeightTexture == null || data.ChunkBuffer == null || component.DefaultDiffuseTexture == null)
        {
            return;
        }

        var parameters = materialPass.Parameters;
        parameters.Set(TerrainKeys.HeightTexture, data.HeightTexture);
        parameters.Set(TerrainKeys.ChunkBuffer, data.ChunkBuffer);
        parameters.Set(TerrainKeys.DefaultDiffuseTexture, component.DefaultDiffuseTexture);
        parameters.Set(TerrainKeys.HeightTextureTexelSize, new Vector2(1.0f / data.HeightmapWidth, 1.0f / data.HeightmapHeight));
        parameters.Set(TerrainKeys.HeightmapDimensionsInSamples, new Vector2(data.HeightmapWidth - 1, data.HeightmapHeight - 1));
        parameters.Set(TerrainKeys.HeightScale, component.HeightScale);
        parameters.Set(TerrainKeys.BaseChunkSize, component.BaseChunkSize);
        parameters.Set(TerrainKeys.DiffuseWorldRepeatSize, DiffuseWorldRepeatSize);
        parameters.Set(TerrainKeys.BaseColor, component.BaseColor);
    }

    private void UploadChunkBuffer(TerrainRuntimeData data)
    {
        var graphicsDevice = Services.GetService<IGraphicsDeviceService>()!.GraphicsDevice;

        data.ChunkBuffer?.Dispose();
        if (data.SelectedChunks.Count == 0)
        {
            data.ChunkBuffer = Buffer.Structured.New(graphicsDevice, new[] { new Int4(0, 0, 0, 0) });
            data.RenderObject!.InstanceCount = 0;
        }
        else
        {
            var chunkData = new Int4[data.SelectedChunks.Count];
            for (int i = 0; i < data.SelectedChunks.Count; i++)
            {
                var chunk = data.SelectedChunks[i];
                chunkData[i] = new Int4(chunk.ChunkX, chunk.ChunkY, chunk.LodLevel, 0);
            }

            data.ChunkBuffer = Buffer.Structured.New(graphicsDevice, chunkData);
            data.RenderObject!.InstanceCount = chunkData.Length;
        }

        data.RenderObject!.ChunkBufferAsset = data.ChunkBuffer;
    }

    private void SelectChunks(Matrix terrainWorldMatrix, TerrainComponent component, TerrainRuntimeData data, CameraComponent camera, GraphicsDevice graphicsDevice)
    {
        data.SelectedChunks.Clear();
        if (data.MinMaxErrorMaps == null)
        {
            return;
        }

        float viewHeight = Math.Max(1.0f, graphicsDevice.Presenter.BackBuffer.Height);
        float k = camera.ProjectionMatrix.M44 != 1.0f
            ? viewHeight / (2.0f * MathF.Tan(MathUtil.DegreesToRadians(camera.VerticalFieldOfView) * 0.5f))
            : viewHeight / Math.Max(camera.OrthographicSize, 1e-3f);
        var cameraPosition = camera.Entity.Transform.WorldMatrix.TranslationVector;
        var topMap = data.MinMaxErrorMaps[data.MaxLod];

        for (int y = 0; y < topMap.Height; y++)
        {
            for (int x = 0; x < topMap.Width; x++)
            {
                TraverseChunk(terrainWorldMatrix, component, data, cameraPosition, camera.Frustum, k, component.MaxScreenSpaceErrorPixels, x, y, data.MaxLod);
            }
        }
    }

    private void TraverseChunk(Matrix terrainWorldMatrix, TerrainComponent component, TerrainRuntimeData data, Vector3 cameraPosition, BoundingFrustum frustum, float k, float maxErrorPixels, int chunkX, int chunkY, int lodLevel)
    {
        int sizeInSamples = component.BaseChunkSize << lodLevel;
        int originSampleX = chunkX * sizeInSamples;
        int originSampleY = chunkY * sizeInSamples;
        if (originSampleX >= data.HeightmapWidth - 1 || originSampleY >= data.HeightmapHeight - 1)
        {
            return;
        }

        var minMaxErrorMap = data.MinMaxErrorMaps![lodLevel];
        minMaxErrorMap.Get(chunkX, chunkY, out var minHeight, out var maxHeight, out var geometricError);
        var bounds = ComputeWorldBounds(terrainWorldMatrix, component, data, originSampleX, originSampleY, sizeInSamples, minHeight, maxHeight);
        var boundsExt = (BoundingBoxExt)bounds;
        if (!frustum.Contains(ref boundsExt))
        {
            return;
        }

        float distance = DistanceToAabb(cameraPosition, bounds);
        float sse = distance > 1e-4f ? k * (geometricError * component.HeightScale) / distance : float.MaxValue;
        if (lodLevel == 0 || sse <= maxErrorPixels)
        {
            data.SelectedChunks.Add(new TerrainSelectedChunk
            {
                ChunkX = chunkX,
                ChunkY = chunkY,
                LodLevel = lodLevel,
                MinHeight = minHeight,
                MaxHeight = maxHeight,
                WorldBounds = bounds,
            });
            return;
        }

        var childMap = data.MinMaxErrorMaps[lodLevel - 1];
        childMap.GetSubNodesExist(chunkX, chunkY, out var subTLExist, out var subTRExist, out var subBLExist, out var subBRExist);

        int childChunkX = chunkX * 2;
        int childChunkY = chunkY * 2;
        if (subTLExist)
        {
            TraverseChunk(terrainWorldMatrix, component, data, cameraPosition, frustum, k, maxErrorPixels, childChunkX, childChunkY, lodLevel - 1);
        }

        if (subTRExist)
        {
            TraverseChunk(terrainWorldMatrix, component, data, cameraPosition, frustum, k, maxErrorPixels, childChunkX + 1, childChunkY, lodLevel - 1);
        }

        if (subBLExist)
        {
            TraverseChunk(terrainWorldMatrix, component, data, cameraPosition, frustum, k, maxErrorPixels, childChunkX, childChunkY + 1, lodLevel - 1);
        }

        if (subBRExist)
        {
            TraverseChunk(terrainWorldMatrix, component, data, cameraPosition, frustum, k, maxErrorPixels, childChunkX + 1, childChunkY + 1, lodLevel - 1);
        }
    }

    private static float DistanceToAabb(Vector3 point, BoundingBox bounds)
    {
        float dx = MathF.Max(MathF.Max(bounds.Minimum.X - point.X, 0.0f), point.X - bounds.Maximum.X);
        float dy = MathF.Max(MathF.Max(bounds.Minimum.Y - point.Y, 0.0f), point.Y - bounds.Maximum.Y);
        float dz = MathF.Max(MathF.Max(bounds.Minimum.Z - point.Z, 0.0f), point.Z - bounds.Maximum.Z);
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static BoundingBox ComputeWorldBounds(Matrix terrainWorldMatrix, TerrainComponent component, TerrainRuntimeData data, int originSampleX, int originSampleY, int sizeInSamples, float minHeight, float maxHeight)
    {
        int endSampleX = Math.Min(originSampleX + sizeInSamples, data.HeightmapWidth - 1);
        int endSampleY = Math.Min(originSampleY + sizeInSamples, data.HeightmapHeight - 1);

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
        for (int y = 0; y < baseMap.Height; y++)
        {
            int originY = y * baseChunkSize;
            for (int x = 0; x < baseMap.Width; x++)
            {
                int originX = x * baseChunkSize;
                ComputeMinMax(heights, width, height, originX, originY, baseChunkSize, out var minHeight, out var maxHeight);
                baseMap.Set(x, y, minHeight, maxHeight, 0.0f);
            }
        }

        for (int lod = 1; lod <= maxLod; lod++)
        {
            var childMap = maps[lod - 1];
            var map = maps[lod] = new TerrainMinMaxErrorMap((childMap.Width + 1) / 2, (childMap.Height + 1) / 2);
            int size = baseChunkSize << lod;

            for (int y = 0; y < map.Height; y++)
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
                    float geometricError = ComputeLevelError(heights, width, height, originX, originY, size);
                    map.Set(x, y, minHeight, maxHeight, geometricError);
                }
            }
        }

        return maps;
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

    private static float ComputeLevelError(float[] heights, int width, int height, int originX, int originY, int size)
    {
        int x0 = Math.Clamp(originX, 0, width - 1);
        int y0 = Math.Clamp(originY, 0, height - 1);
        int x1 = Math.Clamp(originX + size, 0, width - 1);
        int y1 = Math.Clamp(originY + size, 0, height - 1);

        float h00 = heights[y0 * width + x0];
        float h10 = heights[y0 * width + x1];
        float h01 = heights[y1 * width + x0];
        float h11 = heights[y1 * width + x1];

        float maxError = 0.0f;
        int maxX = Math.Min(originX + size, width - 1);
        int maxY = Math.Min(originY + size, height - 1);

        for (int y = originY; y <= maxY; y++)
        {
            float fy = size > 0 ? (y - originY) / (float)size : 0.0f;
            for (int x = originX; x <= maxX; x++)
            {
                float fx = size > 0 ? (x - originX) / (float)size : 0.0f;
                float top = MathUtil.Lerp(h00, h10, fx);
                float bottom = MathUtil.Lerp(h01, h11, fx);
                float approx = MathUtil.Lerp(top, bottom, fy);
                float actual = heights[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];
                maxError = MathF.Max(maxError, MathF.Abs(actual - approx));
            }
        }

        return maxError;
    }

    private void UpdateBounds(Matrix terrainWorldMatrix, TerrainComponent component, TerrainRuntimeData data)
    {
        if (data.SelectedChunks.Count > 0)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var chunk in data.SelectedChunks)
            {
                min = Vector3.Min(min, chunk.WorldBounds.Minimum);
                max = Vector3.Max(max, chunk.WorldBounds.Maximum);
            }

            data.RenderObject!.BoundingBox = (BoundingBoxExt)new BoundingBox(min, max);
            return;
        }

        var fallbackBounds = ComputeWorldBounds(
            terrainWorldMatrix,
            component,
            data,
            0,
            0,
            Math.Max(data.HeightmapWidth - 1, data.HeightmapHeight - 1),
            data.MinHeight,
            data.MaxHeight);
        data.RenderObject!.BoundingBox = (BoundingBoxExt)fallbackBounds;
    }

    private static string ResolveHeightmapPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, path),
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(Directory.GetCurrentDirectory(), path),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(path);
    }

    private static TerrainRenderObject CreateRenderObject(TerrainRuntimeData data)
    {
        var mesh = new Mesh(data.PatchDraw!, new ParameterCollection());
        return new TerrainRenderObject
        {
            Mesh = mesh,
            ActiveMeshDraw = mesh.Draw,
            MaterialPass = new MaterialPass
            {
                HasTransparency = false,
                IsLightDependent = true,
                PassIndex = 0,
            },
            InstanceCount = 0,
            World = Matrix.Identity,
            BoundingBox = (BoundingBoxExt)new BoundingBox(Vector3.Zero, Vector3.One),
        };
    }

    private static void CreatePatchDraw(GraphicsDevice graphicsDevice, int baseChunkSize, TerrainRuntimeData data)
    {
        int vertexCountPerAxis = baseChunkSize + 1;
        var vertices = new TerrainPatchVertex[vertexCountPerAxis * vertexCountPerAxis];
        int vertexIndex = 0;

        for (int y = 0; y < vertexCountPerAxis; y++)
        {
            for (int x = 0; x < vertexCountPerAxis; x++)
            {
                vertices[vertexIndex++] = new TerrainPatchVertex
                {
                    Position = new Vector3(x, 0.0f, y),
                };
            }
        }

        var indices = new int[baseChunkSize * baseChunkSize * 6];
        int index = 0;
        for (int y = 0; y < baseChunkSize; y++)
        {
            for (int x = 0; x < baseChunkSize; x++)
            {
                int topLeft = y * vertexCountPerAxis + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + vertexCountPerAxis;
                int bottomRight = bottomLeft + 1;

                indices[index++] = topLeft;
                indices[index++] = bottomRight;
                indices[index++] = bottomLeft;
                indices[index++] = topLeft;
                indices[index++] = topRight;
                indices[index++] = bottomRight;
            }
        }

        data.PatchVertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices);
        data.PatchIndexBuffer = Buffer.Index.New(graphicsDevice, indices);
        data.PatchDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = indices.Length,
            StartLocation = 0,
            VertexBuffers =
            [
                new VertexBufferBinding(data.PatchVertexBuffer, TerrainPatchVertex.Layout, vertices.Length),
            ],
            IndexBuffer = new IndexBufferBinding(data.PatchIndexBuffer, true, indices.Length),
        };
    }

    private static Matrix CreateTerrainWorldMatrix(Matrix entityWorldMatrix)
    {
        entityWorldMatrix.Decompose(out _, out Matrix rotation, out var translation);
        rotation.TranslationVector = translation;
        return rotation;
    }
}
