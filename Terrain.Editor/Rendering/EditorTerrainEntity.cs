#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Terrain.Editor.Services;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Represents one logical editor terrain. Large heightmaps are internally split into heightmap slices,
/// but all LOD selection and rendering still operate as a single terrain.
/// </summary>
public sealed class EditorTerrainEntity : IDisposable
{
    private const float HeightSampleNormalization = 1.0f / ushort.MaxValue;
    public const int MaxBoundHeightSlices = 8;

    public int ChunkX { get; init; }
    public int ChunkZ { get; init; }
    public Vector3 WorldOffset { get; init; }

    public int HeightmapWidth { get; private set; }
    public int HeightmapHeight { get; private set; }
    public ushort[]? HeightDataCache { get; private set; }

    public SplitTerrainConfig SplitConfig { get; private set; } = null!;
    public IReadOnlyList<EditorTerrainSlice> Slices => slices;

    /// <summary>
    /// Returns true if any slice has dirty data that needs to be synced to GPU.
    /// </summary>
    public bool HasAnyDirtySlice => slices.Exists(s => s.IsDirty);

    public EditorMinMaxErrorMap[]? MinMaxErrorMaps { get; private set; }
    public int MaxLod { get; private set; }
    public int BaseChunkSize { get; private set; }

    public Buffer? ChunkNodeBuffer { get; private set; }
    public Buffer? LodLookupBuffer { get; private set; }
    public Buffer? LodLookupLayoutBuffer { get; private set; }
    public Texture? LodMapTexture { get; private set; }
    public Buffer? PatchVertexBuffer { get; private set; }
    public Buffer? PatchIndexBuffer { get; private set; }

    /// <summary>
    /// 材质索引图纹理 (R8_UInt)，存储每个像素的材质槽位索引。
    /// </summary>
    public Texture? MaterialIndexMapTexture { get; private set; }

    /// <summary>
    /// 材质纹理数组，包含所有活动材质的 Albedo 纹理。
    /// </summary>
    public Texture? MaterialAlbedoArray { get; private set; }

    public TerrainChunkNode[]? ChunkNodeData { get; private set; }
    public int RenderCount { get; set; }
    public BoundingBox Bounds { get; private set; }

    public float HeightScale { get; private set; } = 100.0f;
    public float MaxScreenSpaceErrorPixels { get; private set; } = 8.0f;

    private readonly List<EditorTerrainSlice> slices = new();

    // Cached bounds values for incremental updates
    private float currentMinHeight = float.MaxValue;
    private float currentMaxHeight = float.MinValue;

    private EditorTerrainEntity() { }

    public static EditorTerrainEntity? CreateFromHeightmapData(
        GraphicsDevice graphicsDevice,
        ushort[] heightData,
        int width,
        int height,
        SplitTerrainConfig splitConfig,
        int baseChunkSize = 32,
        float heightScale = 100.0f,
        float maxScreenSpaceErrorPixels = 8.0f,
        EditorMinMaxErrorMap[]? precomputedMaps = null)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(heightData);
        ArgumentNullException.ThrowIfNull(splitConfig);

        if (heightData.Length != width * height)
            throw new ArgumentException("Height data size mismatch.", nameof(heightData));
        if (splitConfig.TotalSliceCount > MaxBoundHeightSlices)
            throw new InvalidOperationException($"Editor terrain currently supports up to {MaxBoundHeightSlices} bound heightmap slices, but {splitConfig.TotalSliceCount} were requested.");

        var entity = new EditorTerrainEntity
        {
            ChunkX = 0,
            ChunkZ = 0,
            WorldOffset = Vector3.Zero,
            HeightmapWidth = width,
            HeightmapHeight = height,
            HeightDataCache = heightData,
            SplitConfig = splitConfig,
            BaseChunkSize = baseChunkSize,
            HeightScale = heightScale,
            MaxScreenSpaceErrorPixels = maxScreenSpaceErrorPixels,
        };

        entity.MinMaxErrorMaps = precomputedMaps ?? HeightmapLoader.GenerateMinMaxErrorMaps(heightData, width, height, baseChunkSize);
        entity.MaxLod = entity.MinMaxErrorMaps.Length - 1;

        entity.CreateSliceTextures(graphicsDevice);
        entity.InitializeGpuResources(graphicsDevice);
        entity.InitializeMaterialResources(graphicsDevice);
        entity.CalculateBounds();
        return entity;
    }

    /// <summary>
    /// 初始化材质相关 GPU 资源。
    /// </summary>
    public void InitializeMaterialResources(GraphicsDevice graphicsDevice)
    {
        // R8_UInt 索引图 - 初始化为 0（默认材质）
        var initialData = new byte[HeightmapWidth * HeightmapHeight];
        MaterialIndexMapTexture = Texture.New2D(
            graphicsDevice,
            HeightmapWidth,
            HeightmapHeight,
            PixelFormat.R8_UInt,
            initialData,
            TextureFlags.ShaderResource);
    }

    /// <summary>
    /// 同步材质索引图到 GPU。
    /// </summary>
    public void SyncMaterialIndexMapToGpu(CommandList commandList, byte[] indexData)
    {
        if (MaterialIndexMapTexture == null || indexData.Length != HeightmapWidth * HeightmapHeight)
            return;

        MaterialIndexMapTexture.SetData(commandList, indexData);
    }

    public bool TryResolveNodeSlice(int originSampleX, int originSampleZ, int sizeInSamples, out EditorTerrainSlice slice, out int localOriginX, out int localOriginZ)
    {
        if (SplitConfig.TryGetOwningSliceForNode(originSampleX, originSampleZ, sizeInSamples, out var sliceInfo))
        {
            slice = slices[sliceInfo.Index];
            localOriginX = originSampleX - slice.StartSampleX;
            localOriginZ = originSampleZ - slice.StartSampleZ;
            return true;
        }

        slice = null!;
        localOriginX = 0;
        localOriginZ = 0;
        return false;
    }

    public bool TryResolveSampleSlice(int sampleX, int sampleZ, out EditorTerrainSlice slice)
    {
        foreach (var candidate in slices)
        {
            if (sampleX < candidate.StartSampleX || sampleX > candidate.EndSampleX)
                continue;

            if (sampleZ < candidate.StartSampleZ || sampleZ > candidate.EndSampleZ)
                continue;

            slice = candidate;
            return true;
        }

        slice = null!;
        return false;
    }

    public void MarkHeightRegionDirty(int modifiedX, int modifiedZ, float radius)
    {
        if (HeightDataCache == null)
            return;

        int minX = Math.Max(0, (int)MathF.Floor(modifiedX - radius));
        int minZ = Math.Max(0, (int)MathF.Floor(modifiedZ - radius));
        int maxX = Math.Min(HeightmapWidth - 1, (int)MathF.Ceiling(modifiedX + radius));
        int maxZ = Math.Min(HeightmapHeight - 1, (int)MathF.Ceiling(modifiedZ + radius));

        foreach (var slice in slices)
        {
            if (slice.Intersects(minX, minZ, maxX, maxZ))
            {
                // Convert global coordinates to slice-local coordinates
                int localMinX = Math.Max(0, minX - slice.StartSampleX);
                int localMinZ = Math.Max(0, minZ - slice.StartSampleZ);
                int localMaxX = Math.Min(slice.Width - 1, maxX - slice.StartSampleX);
                int localMaxZ = Math.Min(slice.Height - 1, maxZ - slice.StartSampleZ);

                slice.MarkDirtyRegion(localMinX, localMinZ, localMaxX, localMaxZ);
            }
        }

        // Incremental bounds update - only check the modified region
        UpdateBoundsForRegion(minX, minZ, maxX, maxZ);
    }

    /// <summary>
    /// Updates bounds incrementally by only checking the modified region.
    /// This is O(region area) instead of O(total heightmap area).
    /// </summary>
    private void UpdateBoundsForRegion(int minX, int minZ, int maxX, int maxZ)
    {
        Debug.Assert(HeightDataCache != null);

        // Find min/max heights in the modified region only
        float regionMin = float.MaxValue;
        float regionMax = float.MinValue;

        for (int z = minZ; z <= maxZ; z++)
        {
            int rowOffset = z * HeightmapWidth;
            for (int x = minX; x <= maxX; x++)
            {
                float h = HeightDataCache![rowOffset + x] * HeightSampleNormalization * HeightScale;
                regionMin = MathF.Min(regionMin, h);
                regionMax = MathF.Max(regionMax, h);
            }
        }

        // Update cached bounds
        currentMinHeight = MathF.Min(currentMinHeight, regionMin);
        currentMaxHeight = MathF.Max(currentMaxHeight, regionMax);

        // Update Bounds structure
        Bounds = new BoundingBox(
            new Vector3(WorldOffset.X, WorldOffset.Y + currentMinHeight, WorldOffset.Z),
            new Vector3(WorldOffset.X + HeightmapWidth - 1, WorldOffset.Y + currentMaxHeight, WorldOffset.Z + HeightmapHeight - 1));
    }

    public void UpdateChunkNodeData(CommandList commandList, TerrainChunkNode[] data, int renderCount, int nodeCount)
    {
        Debug.Assert(ChunkNodeBuffer != null);
        if (nodeCount <= 0)
        {
            RenderCount = 0;
            return;
        }

        ChunkNodeBuffer!.SetData(commandList, new ReadOnlySpan<TerrainChunkNode>(data, 0, nodeCount));
        RenderCount = renderCount;
    }

    public void SyncToGpu(CommandList commandList)
    {
        if (HeightDataCache == null)
            return;

        foreach (var slice in slices)
        {
            if (!slice.IsDirty || slice.Texture == null)
                continue;

            if (slice.HasDirtyRegion)
            {
                // Partial region upload for efficiency
                int regionWidth = slice.DirtyMaxX - slice.DirtyMinX + 1;
                int regionHeight = slice.DirtyMaxZ - slice.DirtyMinZ + 1;
                int sampleCount = regionWidth * regionHeight;

                ushort[] uploadBuffer = ArrayPool<ushort>.Shared.Rent(sampleCount);
                try
                {
                    CopySliceRegionData(slice, slice.DirtyMinX, slice.DirtyMinZ,
                        regionWidth, regionHeight, uploadBuffer);

                    // ResourceRegion uses exclusive bounds for Right/Bottom (pixel + 1)
                    var region = new ResourceRegion(
                        left: slice.DirtyMinX,
                        top: slice.DirtyMinZ,
                        front: 0,
                        right: slice.DirtyMinX + regionWidth,
                        bottom: slice.DirtyMinZ + regionHeight,
                        back: 1);

                    slice.Texture.SetData(commandList,
                        new ReadOnlySpan<ushort>(uploadBuffer, 0, sampleCount),
                        arrayIndex: 0, mipLevel: 0, region);

                    slice.ClearDirtyRegion();
                }
                finally
                {
                    ArrayPool<ushort>.Shared.Return(uploadBuffer);
                }
            }
            else
            {
                // Fallback to full slice upload
                int sampleCount = slice.Width * slice.Height;
                ushort[] uploadBuffer = ArrayPool<ushort>.Shared.Rent(sampleCount);
                try
                {
                    CopySliceData(slice, uploadBuffer);
                    slice.Texture.SetData(commandList, new ReadOnlySpan<ushort>(uploadBuffer, 0, sampleCount));
                    slice.IsDirty = false;
                }
                finally
                {
                    ArrayPool<ushort>.Shared.Return(uploadBuffer);
                }
            }
        }

        // Note: Bounds are updated incrementally in MarkHeightRegionDirty, no need to recalculate here
    }

    private void CopySliceRegionData(EditorTerrainSlice slice,
        int localStartX, int localStartZ, int regionWidth, int regionHeight, ushort[] destination)
    {
        Debug.Assert(HeightDataCache != null);
        Debug.Assert(destination.Length >= regionWidth * regionHeight);

        for (int row = 0; row < regionHeight; row++)
        {
            int globalZ = slice.StartSampleZ + localStartZ + row;
            int srcOffset = globalZ * HeightmapWidth + slice.StartSampleX + localStartX;
            int dstOffset = row * regionWidth;
            Array.Copy(HeightDataCache!, srcOffset, destination, dstOffset, regionWidth);
        }
    }

    private void CreateSliceTextures(GraphicsDevice graphicsDevice)
    {
        Debug.Assert(HeightDataCache != null);
        slices.Clear();

        foreach (var sliceInfo in SplitConfig.Slices)
        {
            var sliceData = new ushort[sliceInfo.Width * sliceInfo.Height];
            CopySliceData(sliceInfo, sliceData);
            var texture = HeightmapLoader.CreateHeightmapTexture(graphicsDevice, sliceData, sliceInfo.Width, sliceInfo.Height);
            slices.Add(new EditorTerrainSlice(sliceInfo, texture));
        }
    }

    private void CopySliceData(SplitTerrainSliceInfo sliceInfo, ushort[] destination)
    {
        Debug.Assert(HeightDataCache != null);
        Debug.Assert(destination.Length >= sliceInfo.Width * sliceInfo.Height);

        for (int row = 0; row < sliceInfo.Height; row++)
        {
            int srcOffset = (sliceInfo.StartSampleZ + row) * HeightmapWidth + sliceInfo.StartSampleX;
            int dstOffset = row * sliceInfo.Width;
            Array.Copy(HeightDataCache!, srcOffset, destination, dstOffset, sliceInfo.Width);
        }
    }

    private void CopySliceData(EditorTerrainSlice slice, ushort[] destination)
    {
        CopySliceData(slice.Info, destination);
    }

    private void InitializeGpuResources(GraphicsDevice graphicsDevice)
    {
        int maxChunkNodeCount = CalculateMaxChunkNodeCount();
        int lodLookupLevelCount = MaxLod + 1;
        int lodLookupEntryCount = maxChunkNodeCount;

        ChunkNodeBuffer = Buffer.Structured.New<TerrainChunkNode>(graphicsDevice, maxChunkNodeCount, true);
        LodLookupBuffer = Buffer.Structured.New<TerrainLodLookupEntry>(graphicsDevice, lodLookupEntryCount, true);
        LodLookupLayoutBuffer = Buffer.Structured.New<TerrainLodLookupLayout>(graphicsDevice, lodLookupLevelCount);

        int lodMapWidth = Math.Max(1, (HeightmapWidth - 1 + BaseChunkSize - 1) / BaseChunkSize);
        int lodMapHeight = Math.Max(1, (HeightmapHeight - 1 + BaseChunkSize - 1) / BaseChunkSize);
        LodMapTexture = Texture.New2D(
            graphicsDevice,
            lodMapWidth,
            lodMapHeight,
            1,
            PixelFormat.R8_UInt,
            TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);

        CreatePatchGeometry(graphicsDevice);
        ChunkNodeData = new TerrainChunkNode[maxChunkNodeCount];
    }

    private int CalculateMaxChunkNodeCount()
    {
        int count = 0;
        int chunksX = (HeightmapWidth - 1 + BaseChunkSize - 1) / BaseChunkSize;
        int chunksY = (HeightmapHeight - 1 + BaseChunkSize - 1) / BaseChunkSize;

        for (int lod = 0; lod <= MaxLod; lod++)
        {
            count += chunksX * chunksY;
            chunksX = Math.Max(1, (chunksX + 1) / 2);
            chunksY = Math.Max(1, (chunksY + 1) / 2);
        }

        return Math.Max(count, 1024);
    }

    private void CreatePatchGeometry(GraphicsDevice graphicsDevice)
    {
        int vertexCountPerAxis = BaseChunkSize + 1;
        var vertices = new EditorPatchVertex[vertexCountPerAxis * vertexCountPerAxis];
        int vertexIndex = 0;

        for (int y = 0; y < vertexCountPerAxis; y++)
        {
            for (int x = 0; x < vertexCountPerAxis; x++)
            {
                vertices[vertexIndex++] = new EditorPatchVertex
                {
                    Position = new Vector3(x, 0.0f, y),
                };
            }
        }

        var indices = new int[(BaseChunkSize / 2) * (BaseChunkSize / 2) * 8 * 3];
        int index = 0;
        for (int y = 0; y < BaseChunkSize; y += 2)
        {
            for (int x = 0; x < BaseChunkSize; x += 2)
            {
                int a = y * vertexCountPerAxis + x;
                int b = a + vertexCountPerAxis;
                int c = a + vertexCountPerAxis * 2;
                int d = a + 1 + vertexCountPerAxis * 2;
                int e = a + 1 + vertexCountPerAxis;
                int f = a + 1;
                int g = a + 2;
                int h = a + 2 + vertexCountPerAxis;
                int i = a + 2 + vertexCountPerAxis * 2;

                indices[index++] = e;
                indices[index++] = a;
                indices[index++] = f;

                indices[index++] = e;
                indices[index++] = b;
                indices[index++] = a;

                indices[index++] = e;
                indices[index++] = c;
                indices[index++] = b;

                indices[index++] = e;
                indices[index++] = d;
                indices[index++] = c;

                indices[index++] = e;
                indices[index++] = i;
                indices[index++] = d;

                indices[index++] = e;
                indices[index++] = h;
                indices[index++] = i;

                indices[index++] = e;
                indices[index++] = g;
                indices[index++] = h;

                indices[index++] = e;
                indices[index++] = f;
                indices[index++] = g;
            }
        }

        PatchVertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices);
        PatchIndexBuffer = Buffer.Index.New(graphicsDevice, indices);
    }

    private void CalculateBounds()
    {
        if (HeightDataCache == null || HeightDataCache.Length == 0)
        {
            Bounds = new BoundingBox(WorldOffset, WorldOffset);
            currentMinHeight = 0.0f;
            currentMaxHeight = 0.0f;
            return;
        }

        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;
        foreach (ushort height in HeightDataCache)
        {
            float h = height * HeightSampleNormalization * HeightScale;
            minHeight = MathF.Min(minHeight, h);
            maxHeight = MathF.Max(maxHeight, h);
        }

        if (minHeight == float.MaxValue)
            minHeight = 0.0f;

        // Cache the min/max heights for incremental updates
        currentMinHeight = minHeight;
        currentMaxHeight = maxHeight;

        Bounds = new BoundingBox(
            new Vector3(WorldOffset.X, WorldOffset.Y + minHeight, WorldOffset.Z),
            new Vector3(WorldOffset.X + HeightmapWidth - 1, WorldOffset.Y + maxHeight, WorldOffset.Z + HeightmapHeight - 1));
    }

    public void Dispose()
    {
        foreach (var slice in slices)
        {
            slice.Texture?.Dispose();
        }
        slices.Clear();

        ChunkNodeBuffer?.Dispose();
        ChunkNodeBuffer = null;

        LodLookupBuffer?.Dispose();
        LodLookupBuffer = null;

        LodLookupLayoutBuffer?.Dispose();
        LodLookupLayoutBuffer = null;

        LodMapTexture?.Dispose();
        LodMapTexture = null;

        PatchVertexBuffer?.Dispose();
        PatchVertexBuffer = null;

        PatchIndexBuffer?.Dispose();
        PatchIndexBuffer = null;

        MaterialIndexMapTexture?.Dispose();
        MaterialIndexMapTexture = null;

        MaterialAlbedoArray?.Dispose();
        MaterialAlbedoArray = null;

        HeightDataCache = null;
        MinMaxErrorMaps = null;
        ChunkNodeData = null;
    }
}

public sealed class EditorTerrainSlice
{
    public EditorTerrainSlice(SplitTerrainSliceInfo info, Texture texture)
    {
        Info = info;
        Texture = texture;
    }

    public SplitTerrainSliceInfo Info { get; }
    public Texture Texture { get; }
    public bool IsDirty { get; set; }

    /// <summary>
    /// Dirty region tracking for partial GPU uploads (slice-local coordinates).
    /// </summary>
    public int DirtyMinX { get; set; } = int.MaxValue;
    public int DirtyMinZ { get; set; } = int.MaxValue;
    public int DirtyMaxX { get; set; } = int.MinValue;
    public int DirtyMaxZ { get; set; } = int.MinValue;

    public int Index => Info.Index;
    public int StartSampleX => Info.StartSampleX;
    public int StartSampleZ => Info.StartSampleZ;
    public int Width => Info.Width;
    public int Height => Info.Height;
    public int EndSampleX => Info.EndSampleX;
    public int EndSampleZ => Info.EndSampleZ;

    public bool Intersects(int minX, int minZ, int maxX, int maxZ)
    {
        return minX <= EndSampleX && maxX >= StartSampleX && minZ <= EndSampleZ && maxZ >= StartSampleZ;
    }

    /// <summary>
    /// Marks a region as dirty, merging with any existing dirty region.
    /// </summary>
    public void MarkDirtyRegion(int localMinX, int localMinZ, int localMaxX, int localMaxZ)
    {
        IsDirty = true;
        DirtyMinX = Math.Min(DirtyMinX, localMinX);
        DirtyMinZ = Math.Min(DirtyMinZ, localMinZ);
        DirtyMaxX = Math.Max(DirtyMaxX, localMaxX);
        DirtyMaxZ = Math.Max(DirtyMaxZ, localMaxZ);
    }

    /// <summary>
    /// Clears the dirty region tracking after GPU upload.
    /// </summary>
    public void ClearDirtyRegion()
    {
        IsDirty = false;
        DirtyMinX = int.MaxValue;
        DirtyMinZ = int.MaxValue;
        DirtyMaxX = int.MinValue;
        DirtyMaxZ = int.MinValue;
    }

    /// <summary>
    /// Returns true if a dirty region has been tracked.
    /// </summary>
    public bool HasDirtyRegion => DirtyMinX <= DirtyMaxX && DirtyMinZ <= DirtyMaxZ;
}

[StructLayout(LayoutKind.Sequential)]
internal struct EditorPatchVertex
{
    public Vector3 Position;

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector3>());
}

[StructLayout(LayoutKind.Sequential)]
public struct TerrainChunkNode
{
    public Int4 NodeInfo;
    public Int4 StreamInfo;
}

public enum TerrainLodLookupNodeState : uint
{
    Stop = 0,
    Subdivided = 1,
}

[StructLayout(LayoutKind.Sequential)]
public struct TerrainLodLookupLayout
{
    public Int4 LayoutInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct TerrainLodLookupEntry
{
    public uint Subdivided;
}
