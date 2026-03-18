#nullable enable

using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;

namespace Terrain;

public sealed class TerrainRenderObject : RenderMesh
{
    public Texture? HeightTexture;
    public Buffer? InstanceBuffer;
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

        InstanceBuffer = Buffer.Structured.New<Int4>(graphicsDevice, instanceCapacity);
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
