#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain;

public sealed class TerrainRenderObject : RenderMesh
{
    public Texture? HeightmapArray;
    public Buffer? InstanceBuffer;
    public Buffer? LodLookupNodeBuffer;
    public Buffer? LodLookupBuffer;
    public Buffer? LodLookupLayoutBuffer;
    public Texture? LodMapTexture;
    public Buffer? PatchVertexBuffer;
    public Buffer? PatchIndexBuffer;

    public TerrainRenderObject()
    {
        ResetRenderState();
        World = Matrix.Identity;
        BoundingBox = (BoundingBoxExt)new BoundingBox(Vector3.Zero, Vector3.One);
    }

    public void ReinitializeGpuResources(GraphicsDevice graphicsDevice, int baseChunkSize, int heightmapWidth, int heightmapHeight, int tileSize, int tilePadding, int maxResidentChunks, int instanceCapacity, int lodLookupNodeCapacity, int lodLookupLevelCount, int lodLookupEntryCount)
    {
        ReleaseGpuResources();

        int fullTileSize = tileSize + tilePadding * 2;
        HeightmapArray = Texture.New2D(
            graphicsDevice,
            fullTileSize,
            fullTileSize,
            1,
            PixelFormat.R16_UNorm,
            TextureFlags.ShaderResource,
            maxResidentChunks);

        int lodMapWidth = Math.Max(1, (heightmapWidth - 1 + baseChunkSize - 1) / baseChunkSize);
        int lodMapHeight = Math.Max(1, (heightmapHeight - 1 + baseChunkSize - 1) / baseChunkSize);

        InstanceBuffer = Buffer.Structured.New<TerrainChunkInstance>(graphicsDevice, instanceCapacity, true);
        LodLookupNodeBuffer = Buffer.Structured.New<TerrainLodLookupNode>(graphicsDevice, lodLookupNodeCapacity, true);
        LodLookupBuffer = Buffer.Structured.New<TerrainLodLookupEntry>(graphicsDevice, lodLookupEntryCount, true);
        LodLookupLayoutBuffer = Buffer.Structured.New<TerrainLodLookupLayout>(graphicsDevice, lodLookupLevelCount);
        LodMapTexture = Texture.New2D(
            graphicsDevice,
            lodMapWidth,
            lodMapHeight,
            1,
            PixelFormat.R8_UInt,
            TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);
        CreatePatchGeometry(graphicsDevice, baseChunkSize);
        ResetRenderState();
    }

    private void CreatePatchGeometry(GraphicsDevice graphicsDevice, int baseChunkSize)
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

        var indices = new int[(baseChunkSize / 2) * (baseChunkSize / 2) * 8 * 3];
        int index = 0;
        for (int y = 0; y < baseChunkSize; y += 2)
        {
            for (int x = 0; x < baseChunkSize; x += 2)
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

        var meshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = indices.Length,
            StartLocation = 0,
            VertexBuffers =
            [
                new VertexBufferBinding(PatchVertexBuffer, TerrainPatchVertex.Layout, vertices.Length),
            ],
            IndexBuffer = new IndexBufferBinding(PatchIndexBuffer, true, indices.Length),
        };

        Mesh = new Mesh(meshDraw, new ParameterCollection());
        ActiveMeshDraw = meshDraw;
    }

    internal void UpdateInstanceData(CommandList commandList, TerrainChunkInstance[] data, int count)
    {
        Debug.Assert(InstanceBuffer != null);
        if (count <= 0)
        {
            InstanceCount = 0;
            return;
        }

        InstanceBuffer!.SetData(commandList, new global::System.ReadOnlySpan<TerrainChunkInstance>(data, 0, count));
        InstanceCount = count;
    }

    internal void UpdateLodLookupNodeData(CommandList commandList, TerrainLodLookupNode[] data, int count)
    {
        Debug.Assert(LodLookupNodeBuffer != null);
        if (count <= 0)
        {
            return;
        }

        LodLookupNodeBuffer!.SetData(commandList, new global::System.ReadOnlySpan<TerrainLodLookupNode>(data, 0, count));
    }

    internal void UpdateLodLookupLayoutData(CommandList commandList, TerrainLodLookupLayout[] data)
    {
        Debug.Assert(LodLookupLayoutBuffer != null);
        Debug.Assert(data.Length > 0);
        LodLookupLayoutBuffer!.SetData(commandList, data);
    }

    internal void InitializeLodLookupData(CommandList commandList, int entryCount)
    {
        Debug.Assert(LodLookupBuffer != null);
        Debug.Assert(entryCount > 0);
        LodLookupBuffer!.SetData(commandList, new TerrainLodLookupEntry[entryCount]);
    }

    public void ReleaseGpuResources()
    {
        HeightmapArray?.Dispose();
        HeightmapArray = null;

        InstanceBuffer?.Dispose();
        InstanceBuffer = null;

        LodLookupNodeBuffer?.Dispose();
        LodLookupNodeBuffer = null;

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

        Mesh = null!;
        ActiveMeshDraw = null!;
    }

    public void ResetRenderState()
    {
        MaterialPass = null!;
        InstanceCount = 0;
    }

    public void Dispose()
    {
        ReleaseGpuResources();
        ResetRenderState();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct TerrainPatchVertex
{
    public Vector3 Position;

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector3>());
}
