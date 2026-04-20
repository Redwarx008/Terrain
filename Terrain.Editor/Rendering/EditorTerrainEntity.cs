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
/// 地形数据通道类型。
/// </summary>
public enum TerrainDataChannel
{
    /// <summary>
    /// 高度图。
    /// </summary>
    Height,

    /// <summary>
    /// 材质索引图。
    /// </summary>
    MaterialIndex
}

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
    /// Returns true if any slice has dirty height data that needs to be synced to GPU.
    /// </summary>
    public bool HasAnyDirtySlice => slices.Exists(s => s.Dirty.IsChannelDirty(TerrainDataChannel.Height));

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
    /// 材质索引图切片纹理数组 (R8G8B8A8_UNorm)，每个切片对应一个纹理。
    /// R: 材质索引, G: 权重, B: 投影方向, A: 旋转角度。
    /// 与高度图切片使用相同的 SplitConfig 切分。
    /// </summary>
    public Texture?[] MaterialIndexMapTextures { get; private set; } = Array.Empty<Texture?>();
    public Texture? ClimateMaskTexture { get; private set; }
    public Buffer? ClimateRuleBuffer { get; private set; }
    public int ClimateRuleCount { get; private set; }

    private MaterialIndexMap? materialIndexMap;
    private ClimateMask? climateMask;
    private bool climateMaskTextureDirty;
    private bool climateRulesDirty;

    /// <summary>
    /// 材质索引图引用，由 TerrainManager 设置。
    /// Setting this marks the material-index channel dirty so the texture is uploaded on the next draw.
    /// </summary>
    public MaterialIndexMap? MaterialIndexMap
    {
        get => materialIndexMap;
        set
        {
            materialIndexMap = value;
            if (value != null)
            {
                foreach (var slice in slices)
                    slice.Dirty.MarkFullDirty(TerrainDataChannel.MaterialIndex);
            }
        }
    }

    public bool HasDirtyClimateMaskTexture => climateMaskTextureDirty;
    public bool HasDirtyClimateRules => climateRulesDirty;
    public bool HasDirtyClimateSplatMap => slices.Exists(static s => s.ClimateSplatDirty);

    /// <summary>
    /// 标记指定通道的数据为脏，需要在渲染时同步到 GPU。
    /// </summary>
    public void MarkDataDirty(TerrainDataChannel channel, int centerX = 0, int centerZ = 0, float radius = 0)
    {
        if (radius > 0)
        {
            MarkRegionDirty(channel, centerX, centerZ, radius);
        }
        else
        {
            foreach (var slice in slices)
                slice.Dirty.MarkFullDirty(channel);
        }
    }

    /// <summary>
    /// 检查指定通道是否有脏数据。
    /// </summary>
    public bool IsDataDirty(TerrainDataChannel channel)
    {
        return channel switch
        {
            TerrainDataChannel.Height => slices.Exists(s => s.Dirty.IsChannelDirty(TerrainDataChannel.Height)),
            TerrainDataChannel.MaterialIndex => slices.Exists(s => s.Dirty.IsChannelDirty(TerrainDataChannel.MaterialIndex)),
            _ => false
        };
    }

    /// <summary>
    /// 同步指定通道的脏数据到 GPU。
    /// </summary>
    public void SyncDataToGpu(TerrainDataChannel channel, CommandList commandList)
    {
        switch (channel)
        {
            case TerrainDataChannel.Height:
                if (HasAnyDirtySlice)
                    SyncToGpu(commandList);
                break;
            case TerrainDataChannel.MaterialIndex:
                if (slices.Exists(s => s.Dirty.IsChannelDirty(TerrainDataChannel.MaterialIndex)) && MaterialIndexMap != null)
                    SyncMaterialIndexMapToGpu(commandList, MaterialIndexMap);
                break;
        }
    }

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
    /// 为每个高度图切片创建对应的材质索引图纹理。
    /// IndexMap 纹理与 heightmap 切片保持 1:1 分辨率。
    /// </summary>
    public void InitializeMaterialResources(GraphicsDevice graphicsDevice)
    {
        MaterialIndexMapTextures = new Texture[SplitConfig.TotalSliceCount];
        for (int i = 0; i < SplitConfig.TotalSliceCount; i++)
        {
            var sliceInfo = SplitConfig.Slices[i];
            int indexMapWidth = sliceInfo.Width;
            int indexMapHeight = sliceInfo.Height;
            MaterialIndexMapTextures[i] = Texture.New2D(
                graphicsDevice,
                indexMapWidth,
                indexMapHeight,
                PixelFormat.R8G8B8A8_UNorm,
                TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);
        }
    }

    public void SetClimateMask(GraphicsDevice graphicsDevice, ClimateMask? mask)
    {
        climateMask = mask;

        ClimateMaskTexture?.Dispose();
        ClimateMaskTexture = null;

        if (mask != null)
        {
            ClimateMaskTexture = Texture.New2D(
                graphicsDevice,
                mask.Width,
                mask.Height,
                PixelFormat.R8_UNorm,
                TextureFlags.ShaderResource);
        }

        climateMaskTextureDirty = mask != null;
        climateRulesDirty = true;
        MarkAllClimateSplatDirty();
    }

    public void MarkClimateMaskDirty()
    {
        if (climateMask == null)
            return;

        climateMaskTextureDirty = true;
    }

    public void MarkClimateRulesDirty()
    {
        climateRulesDirty = true;
    }

    public void MarkClimateSplatDirty(int centerX = 0, int centerZ = 0, float radius = 0)
    {
        if (radius <= 0.0f)
        {
            MarkAllClimateSplatDirty();
            return;
        }

        int minX = Math.Max(0, (int)MathF.Floor(centerX - radius));
        int minZ = Math.Max(0, (int)MathF.Floor(centerZ - radius));
        int maxX = Math.Min(HeightmapWidth - 1, (int)MathF.Ceiling(centerX + radius));
        int maxZ = Math.Min(HeightmapHeight - 1, (int)MathF.Ceiling(centerZ + radius));

        foreach (var slice in slices)
        {
            if (slice.Intersects(minX, minZ, maxX, maxZ))
                slice.ClimateSplatDirty = true;
        }
    }

    public void MarkAllClimateSplatDirty()
    {
        foreach (var slice in slices)
            slice.ClimateSplatDirty = true;
    }

    public void ClearClimateSplatDirty(int sliceIndex)
    {
        if ((uint)sliceIndex >= (uint)slices.Count)
            return;

        slices[sliceIndex].ClimateSplatDirty = false;
    }

    public void SyncClimateResourcesToGpu(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        if (climateMaskTextureDirty && climateMask != null && ClimateMaskTexture != null)
        {
            ClimateMaskTexture.SetData(commandList, climateMask.GetRawData());
            climateMaskTextureDirty = false;
        }

        if (climateRulesDirty)
        {
            UploadClimateRuleBuffer(graphicsDevice, commandList);
            climateRulesDirty = false;
        }
    }

    private void UploadClimateRuleBuffer(GraphicsDevice graphicsDevice, CommandList commandList)
    {
        IReadOnlyList<ClimateRuleLayer> sourceRules = ClimateRuleService.Instance.Rules;
        ClimateSplatRuleGpu[] rules = new ClimateSplatRuleGpu[Math.Max(1, sourceRules.Count)];

        for (int i = 0; i < sourceRules.Count; i++)
        {
            ClimateRuleLayer rule = sourceRules[i];
            rules[i] = new ClimateSplatRuleGpu
            {
                ClimateId = rule.ClimateId,
                MaterialSlotIndex = rule.MaterialSlotIndex,
                Enabled = rule.Enabled ? 1 : 0,
                Reserved = 0,
                MinAltitude = rule.MinAltitude,
                MaxAltitude = rule.MaxAltitude,
                MinSlopeDegrees = rule.MinSlopeDegrees,
                MaxSlopeDegrees = rule.MaxSlopeDegrees,
                BlendRange = rule.BlendRange,
                Padding0 = 0.0f,
                Padding1 = 0.0f,
                Padding2 = 0.0f,
            };
        }

        if (ClimateRuleBuffer == null || ClimateRuleCount != rules.Length)
        {
            ClimateRuleBuffer?.Dispose();
            ClimateRuleBuffer = Buffer.Structured.New(graphicsDevice, rules);
        }
        else
        {
            ClimateRuleBuffer.SetData(commandList, rules);
        }

        ClimateRuleCount = rules.Length;
    }

    /// <summary>
    /// 同步材质索引图到 GPU。
    /// IndexMap 与 heightmap 保持 1:1 分辨率。
    /// </summary>
    public void SyncMaterialIndexMapToGpu(CommandList commandList, Services.MaterialIndexMap indexMap)
    {
        var rawData = indexMap.GetRawData(); // uint[]

        for (int i = 0; i < MaterialIndexMapTextures.Length; i++)
        {
            var texture = MaterialIndexMapTextures[i];
            if (texture == null)
                continue;

            var slice = slices[i];
            if (!slice.Dirty.IsChannelDirty(TerrainDataChannel.MaterialIndex))
                continue;

            int splatSliceWidth = slice.Width;
            int splatSliceHeight = slice.Height;

            // splatmap 中该切片的起始位置（相对于 splatmap 原点）
            int splatStartX = slice.StartSampleX;
            int splatStartZ = slice.StartSampleZ;

            if (slice.Dirty.HasRegion)
            {
                int regionLeft = Math.Max(0, slice.Dirty.MinX);
                int regionTop = Math.Max(0, slice.Dirty.MinZ);
                int regionRight = Math.Min(splatSliceWidth - 1, slice.Dirty.MaxX);
                int regionBottom = Math.Min(splatSliceHeight - 1, slice.Dirty.MaxZ);
                int regionWidth = regionRight - regionLeft + 1;
                int regionHeight = regionBottom - regionTop + 1;

                if (regionWidth <= 0 || regionHeight <= 0)
                {
                    slice.Dirty.ClearChannel(TerrainDataChannel.MaterialIndex);
                    continue;
                }

                int regionByteSize = regionWidth * regionHeight * Services.MaterialIndexMap.BytesPerPixel;
                byte[] uploadBuffer = ArrayPool<byte>.Shared.Rent(regionByteSize);
                try
                {
                    CopyMatIndexRegionDataAt(
                        indexMap, splatStartX + regionLeft, splatStartZ + regionTop,
                        regionWidth, regionHeight, uploadBuffer);

                    var region = new ResourceRegion(
                        left: regionLeft,
                        top: regionTop,
                        front: 0,
                        right: regionLeft + regionWidth,
                        bottom: regionTop + regionHeight,
                        back: 1);

                    texture.SetData(commandList, uploadBuffer.AsSpan(0, regionByteSize), arrayIndex: 0, mipLevel: 0, region);
                    slice.Dirty.ClearChannel(TerrainDataChannel.MaterialIndex);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(uploadBuffer);
                }
            }
            else
            {
                // Full slice upload
                int byteSize = splatSliceWidth * splatSliceHeight * Services.MaterialIndexMap.BytesPerPixel;
                byte[] uploadBuffer = ArrayPool<byte>.Shared.Rent(byteSize);
                try
                {
                    CopyMatIndexRegionDataAt(
                        indexMap, splatStartX, splatStartZ,
                        splatSliceWidth, splatSliceHeight, uploadBuffer);

                    texture.SetData(commandList, uploadBuffer.AsSpan(0, byteSize));
                    slice.Dirty.ClearChannel(TerrainDataChannel.MaterialIndex);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(uploadBuffer);
                }
            }
        }
    }

    /// <summary>
    /// 从 MaterialIndexMap 的指定全局位置复制区域数据到 byte 数组。
    /// 直接在 splatmap 坐标空间操作。
    /// </summary>
    private static void CopyMatIndexRegionDataAt(
        Services.MaterialIndexMap indexMap,
        int startX, int startZ,
        int regionWidth, int regionHeight,
        byte[] destination)
    {
        for (int row = 0; row < regionHeight; row++)
        {
            var srcSpan = indexMap.GetSliceBytesPerRow(startX, startZ + row, 0, regionWidth);
            int dstOffset = row * regionWidth * Services.MaterialIndexMap.BytesPerPixel;
            srcSpan.CopyTo(destination.AsSpan(dstOffset));
        }
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

    /// <summary>
    /// 标记指定通道的脏区域，将全局坐标转换为切片本地坐标。
    /// </summary>
    public void MarkRegionDirty(TerrainDataChannel channel, int centerX, int centerZ, float radius)
    {
        int minX = Math.Max(0, (int)MathF.Floor(centerX - radius));
        int minZ = Math.Max(0, (int)MathF.Floor(centerZ - radius));
        int maxX = Math.Min(HeightmapWidth - 1, (int)MathF.Ceiling(centerX + radius));
        int maxZ = Math.Min(HeightmapHeight - 1, (int)MathF.Ceiling(centerZ + radius));

        foreach (var slice in slices)
        {
            if (!slice.Intersects(minX, minZ, maxX, maxZ))
                continue;

            int localMinX = Math.Max(0, minX - slice.StartSampleX);
            int localMinZ = Math.Max(0, minZ - slice.StartSampleZ);
            int localMaxX = Math.Min(slice.Width - 1, maxX - slice.StartSampleX);
            int localMaxZ = Math.Min(slice.Height - 1, maxZ - slice.StartSampleZ);

            slice.Dirty.MarkRegion(channel, localMinX, localMinZ, localMaxX, localMaxZ);
        }

        if (channel == TerrainDataChannel.Height)
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
            if (!slice.Dirty.IsChannelDirty(TerrainDataChannel.Height) || slice.Texture == null)
                continue;

            if (slice.Dirty.HasRegion)
            {
                // Partial region upload for efficiency
                int regionWidth = slice.Dirty.MaxX - slice.Dirty.MinX + 1;
                int regionHeight = slice.Dirty.MaxZ - slice.Dirty.MinZ + 1;
                int sampleCount = regionWidth * regionHeight;

                ushort[] uploadBuffer = ArrayPool<ushort>.Shared.Rent(sampleCount);
                try
                {
                    CopySliceRegionData(slice, slice.Dirty.MinX, slice.Dirty.MinZ,
                        regionWidth, regionHeight, uploadBuffer);

                    // ResourceRegion uses exclusive bounds for Right/Bottom (pixel + 1)
                    var region = new ResourceRegion(
                        left: slice.Dirty.MinX,
                        top: slice.Dirty.MinZ,
                        front: 0,
                        right: slice.Dirty.MinX + regionWidth,
                        bottom: slice.Dirty.MinZ + regionHeight,
                        back: 1);

                    slice.Texture.SetData(commandList,
                        new ReadOnlySpan<ushort>(uploadBuffer, 0, sampleCount),
                        arrayIndex: 0, mipLevel: 0, region);

                    slice.Dirty.ClearChannel(TerrainDataChannel.Height);
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
                    slice.Dirty.ClearChannel(TerrainDataChannel.Height);
                }
                finally
                {
                    ArrayPool<ushort>.Shared.Return(uploadBuffer);
                }
            }
        }

        // Note: Bounds are updated incrementally in MarkRegionDirty, no need to recalculate here
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

    /// <summary>
    /// 运行时修改高度缩放系数，通过缩放缓存值 O(1) 重算包围盒。
    /// </summary>
    public void SetHeightScale(float newScale)
    {
        if (newScale <= 0.0f)
            return;
        if (MathF.Abs(HeightScale - newScale) < 0.001f)
            return;

        // O(1) 缩放：高度数据未变，只是解释比例变了
        float ratio = newScale / HeightScale;
        currentMinHeight *= ratio;
        currentMaxHeight *= ratio;
        HeightScale = newScale;

        Bounds = new BoundingBox(
            new Vector3(WorldOffset.X, WorldOffset.Y + currentMinHeight, WorldOffset.Z),
            new Vector3(WorldOffset.X + HeightmapWidth - 1, WorldOffset.Y + currentMaxHeight, WorldOffset.Z + HeightmapHeight - 1));
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

        if (MaterialIndexMapTextures != null)
        {
            foreach (var tex in MaterialIndexMapTextures)
                tex?.Dispose();
            MaterialIndexMapTextures = Array.Empty<Texture?>();
        }

        ClimateMaskTexture?.Dispose();
        ClimateMaskTexture = null;

        ClimateRuleBuffer?.Dispose();
        ClimateRuleBuffer = null;
        ClimateRuleCount = 0;

        MaterialAlbedoArray?.Dispose();
        MaterialAlbedoArray = null;

        HeightDataCache = null;
        MinMaxErrorMaps = null;
        ChunkNodeData = null;
    }
}

/// <summary>
/// 切片级脏区域追踪器。区域范围对全部通道共用，
/// 用位掩码记录哪些通道在该区域内被修改。
/// 注意：这是一个可变 struct，必须通过 slice.Dirty.Method() 直接调用，
/// 不要赋值到局部变量后再修改（修改的是副本，不会反映回原值）。
/// </summary>
public struct DirtyRegionTracker
{
    private int minX;
    private int minZ;
    private int maxX;
    private int maxZ;
    private int dirtyChannels; // TerrainDataChannel 位掩码

    public DirtyRegionTracker()
    {
        minX = int.MaxValue;
        minZ = int.MaxValue;
        maxX = int.MinValue;
        maxZ = int.MinValue;
        dirtyChannels = 0;
    }

    public bool IsDirty => dirtyChannels != 0;
    /// <summary>是否有有效的脏区域。同时检查通道掩码以防止默认构造的误报。</summary>
    public bool HasRegion => dirtyChannels != 0 && minX <= maxX && minZ <= maxZ;

    public bool IsChannelDirty(TerrainDataChannel channel)
        => (dirtyChannels & (1 << (int)channel)) != 0;

    public int MinX => minX;
    public int MinZ => minZ;
    public int MaxX => maxX;
    public int MaxZ => maxZ;

    /// <summary>标记指定通道在给定区域内被修改。区域自动合并。</summary>
    public void MarkRegion(TerrainDataChannel channel, int localMinX, int localMinZ, int localMaxX, int localMaxZ)
    {
        dirtyChannels |= (1 << (int)channel);
        minX = Math.Min(minX, localMinX);
        minZ = Math.Min(minZ, localMinZ);
        maxX = Math.Max(maxX, localMaxX);
        maxZ = Math.Max(maxZ, localMaxZ);
    }

    /// <summary>标记通道为全量脏（无区域信息，如初始加载）。</summary>
    /// <remarks>全量脏会使区域失效，强制所有通道走全量上传路径。</remarks>
    public void MarkFullDirty(TerrainDataChannel channel)
    {
        dirtyChannels |= (1 << (int)channel);
        // 任何通道标记为全量脏时，区域信息不再可靠，必须回退到全量上传
        minX = int.MaxValue;
        minZ = int.MaxValue;
        maxX = int.MinValue;
        maxZ = int.MinValue;
    }

    /// <summary>清除指定通道的脏标记。如果全部通道已清除，区域也重置。</summary>
    public void ClearChannel(TerrainDataChannel channel)
    {
        dirtyChannels &= ~(1 << (int)channel);
        if (dirtyChannels == 0) Clear();
    }

    /// <summary>清除所有通道和区域。</summary>
    public void Clear()
    {
        dirtyChannels = 0;
        minX = int.MaxValue;
        minZ = int.MaxValue;
        maxX = int.MinValue;
        maxZ = int.MinValue;
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
    public DirtyRegionTracker Dirty;
    public bool ClimateSplatDirty;

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
}

[StructLayout(LayoutKind.Sequential)]
public struct ClimateSplatRuleGpu
{
    public int ClimateId;
    public int MaterialSlotIndex;
    public int Enabled;
    public int Reserved;
    public float MinAltitude;
    public float MaxAltitude;
    public float MinSlopeDegrees;
    public float MaxSlopeDegrees;
    public float BlendRange;
    public float Padding0;
    public float Padding1;
    public float Padding2;
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
    public Int4 SplatInfo;  // 与 StreamInfo 相同（编辑器不使用 VT streaming）
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
