#nullable enable

using System;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain;

public sealed class TerrainRenderObject : RenderMesh
{
    public Texture? HeightTexture;
    public Buffer? InstanceBuffer;
    public Texture? LodMapTexture;
    public Buffer? PatchVertexBuffer;
    public Buffer? PatchIndexBuffer;

    public TerrainRenderObject()
    {
        ResetRenderState();
        World = Matrix.Identity;
        BoundingBox = (BoundingBoxExt)new BoundingBox(Vector3.Zero, Vector3.One);
    }

    public void ReinitializeGpuResources(GraphicsDevice graphicsDevice, int baseChunkSize, int heightmapWidth, int heightmapHeight, float[] heights, int instanceCapacity)
    {
        ReleaseGpuResources();

        HeightTexture = Texture.New2D(
            graphicsDevice,
            heightmapWidth,
            heightmapHeight,
            PixelFormat.R32_Float,
            heights,
            TextureFlags.ShaderResource);

        int lodMapWidth = Math.Max(1, (heightmapWidth - 1 + baseChunkSize - 1) / baseChunkSize);
        int lodMapHeight = Math.Max(1, (heightmapHeight - 1 + baseChunkSize - 1) / baseChunkSize);

        InstanceBuffer = Buffer.Structured.New<Int4>(graphicsDevice, instanceCapacity, true);
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

    public void UpdateInstanceData(CommandList commandList, Int4[] data, int count)
    {
        if (InstanceBuffer == null || count <= 0)
        {
            InstanceCount = 0;
            return;
        }

        InstanceBuffer.SetData(commandList, new global::System.ReadOnlySpan<Int4>(data, 0, count));
        InstanceCount = count;
    }

    public void ReleaseGpuResources()
    {
        HeightTexture?.Dispose();
        HeightTexture = null;

        InstanceBuffer?.Dispose();
        InstanceBuffer = null;

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
