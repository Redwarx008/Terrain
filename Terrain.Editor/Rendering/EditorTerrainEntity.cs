#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Terrain.Editor.Services;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Represents a single terrain entity for editor rendering.
/// Per CONTEXT.md: Single Texture2D per entity (no Texture2DArray, no streaming).
/// </summary>
public sealed class EditorTerrainEntity : IDisposable
{
    private const float HeightSampleNormalization = 1.0f / ushort.MaxValue;

    // Identity
    public int ChunkX { get; init; }
    public int ChunkZ { get; init; }
    public Vector3 WorldOffset { get; init; }

    // Heightmap data
    public Texture? HeightmapTexture { get; private set; }
    public int HeightmapWidth { get; private set; }
    public int HeightmapHeight { get; private set; }
    public ushort[]? HeightDataCache { get; private set; }

    // LOD data (generated at load time)
    public EditorMinMaxErrorMap[]? MinMaxErrorMaps { get; private set; }
    public int MaxLod { get; private set; }
    public int BaseChunkSize { get; private set; }

    // GPU resources (owned by this entity)
    public Buffer? ChunkNodeBuffer { get; private set; }
    public Buffer? LodLookupBuffer { get; private set; }
    public Buffer? LodLookupLayoutBuffer { get; private set; }
    public Texture? LodMapTexture { get; private set; }
    public Buffer? PatchVertexBuffer { get; private set; }
    public Buffer? PatchIndexBuffer { get; private set; }

    // Runtime state
    public TerrainChunkNode[]? ChunkNodeData { get; private set; }
    public int RenderCount { get; set; }
    public BoundingBox Bounds { get; private set; }

    // Configuration
    public float HeightScale { get; private set; } = 100.0f;
    public float MaxScreenSpaceErrorPixels { get; private set; } = 8.0f;

    private EditorTerrainEntity() { }

    /// <summary>
    /// Creates an EditorTerrainEntity from a heightmap PNG file.
    /// </summary>
    public static EditorTerrainEntity? CreateFromHeightmap(
        GraphicsDevice graphicsDevice,
        string pngPath,
        int chunkX = 0,
        int chunkZ = 0,
        Vector3? worldOffset = null,
        int baseChunkSize = 32,
        float heightScale = 100.0f,
        float maxScreenSpaceErrorPixels = 8.0f)
    {
        if (graphicsDevice == null)
            throw new ArgumentNullException(nameof(graphicsDevice));

        if (string.IsNullOrEmpty(pngPath))
            throw new ArgumentNullException(nameof(pngPath));

        // Load height data using HeightmapLoader
        var heightData = HeightmapLoader.LoadHeightData(pngPath, out int width, out int height);
        if (heightData == null || heightData.Length == 0)
            return null;

        var entity = new EditorTerrainEntity
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            WorldOffset = worldOffset ?? Vector3.Zero,
            HeightmapWidth = width,
            HeightmapHeight = height,
            HeightDataCache = heightData,
            BaseChunkSize = baseChunkSize,
            HeightScale = heightScale,
            MaxScreenSpaceErrorPixels = maxScreenSpaceErrorPixels,
        };

        // Generate MinMaxErrorMaps
        entity.MinMaxErrorMaps = HeightmapLoader.GenerateMinMaxErrorMaps(heightData, width, height, baseChunkSize);
        entity.MaxLod = entity.MinMaxErrorMaps.Length - 1;

        // Create GPU texture
        entity.HeightmapTexture = HeightmapLoader.CreateHeightmapTexture(graphicsDevice, heightData, width, height);

        // Initialize GPU resources
        entity.InitializeGpuResources(graphicsDevice);

        // Calculate bounds
        entity.CalculateBounds();

        return entity;
    }

    private void InitializeGpuResources(GraphicsDevice graphicsDevice)
    {
        int maxChunkNodeCount = CalculateMaxChunkNodeCount();
        int lodLookupLevelCount = MaxLod + 1;
        int lodLookupEntryCount = maxChunkNodeCount;

        // Create chunk node buffer
        ChunkNodeBuffer = Buffer.Structured.New<TerrainChunkNode>(graphicsDevice, maxChunkNodeCount, true);

        // Create LOD lookup buffers
        LodLookupBuffer = Buffer.Structured.New<TerrainLodLookupEntry>(graphicsDevice, lodLookupEntryCount, true);
        LodLookupLayoutBuffer = Buffer.Structured.New<TerrainLodLookupLayout>(graphicsDevice, lodLookupLevelCount);

        // Create LOD map texture
        int lodMapWidth = Math.Max(1, (HeightmapWidth - 1 + BaseChunkSize - 1) / BaseChunkSize);
        int lodMapHeight = Math.Max(1, (HeightmapHeight - 1 + BaseChunkSize - 1) / BaseChunkSize);
        LodMapTexture = Texture.New2D(
            graphicsDevice,
            lodMapWidth,
            lodMapHeight,
            1,
            PixelFormat.R8_UInt,
            TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);

        // Create patch geometry
        CreatePatchGeometry(graphicsDevice);

        // Allocate chunk node data array
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
            minHeight = 0;

        Bounds = new BoundingBox(
            new Vector3(WorldOffset.X, WorldOffset.Y + minHeight, WorldOffset.Z),
            new Vector3(WorldOffset.X + HeightmapWidth - 1, WorldOffset.Y + maxHeight, WorldOffset.Z + HeightmapHeight - 1));
    }

    /// <summary>
    /// Updates chunk node data on the GPU.
    /// </summary>
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

    /// <summary>
    /// Updates height data (for editing).
    /// </summary>
    public void UpdateHeightData(ushort[] newData)
    {
        if (newData == null || newData.Length != HeightDataCache?.Length)
            throw new ArgumentException("Height data size mismatch");

        Array.Copy(newData, HeightDataCache, newData.Length);
    }

    /// <summary>
    /// Syncs modified height data to GPU.
    /// </summary>
    public void SyncToGpu(CommandList commandList)
    {
        if (HeightmapTexture == null || HeightDataCache == null)
            return;

        HeightmapTexture.SetData(commandList, HeightDataCache);
        CalculateBounds(); // Recalculate bounds after height data change
    }

    public void Dispose()
    {
        HeightmapTexture?.Dispose();
        HeightmapTexture = null;

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

        HeightDataCache = null;
        MinMaxErrorMaps = null;
        ChunkNodeData = null;
    }
}

/// <summary>
/// Patch vertex for editor terrain rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct EditorPatchVertex
{
    public Vector3 Position;

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector3>());
}

/// <summary>
/// Unified terrain chunk node structure for both rendering and LOD lookup.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TerrainChunkNode
{
    public Int4 NodeInfo;    // chunkX, chunkY, lodLevel, state (Stop=0, Subdivided=1)
    public Int4 StreamInfo;  // sliceIndex, pageOffsetX, pageOffsetY, pageTexelStride
}

/// <summary>
/// LOD lookup node state.
/// </summary>
public enum TerrainLodLookupNodeState : uint
{
    Stop = 0,
    Subdivided = 1,
}

/// <summary>
/// LOD lookup layout entry.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TerrainLodLookupLayout
{
    public Int4 LayoutInfo;
}

/// <summary>
/// LOD lookup entry.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TerrainLodLookupEntry
{
    public uint Subdivided;
}
