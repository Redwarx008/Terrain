#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverRenderObject : RenderObject, IDisposable
{
    public int SegmentId { get; private set; }
    public int SourceVersion { get; private set; } = -1;
    public Buffer? VertexBuffer { get; private set; }
    public Buffer? IndexBuffer { get; private set; }
    public MeshDraw? MeshDraw { get; private set; }
    public int IndexCount { get; private set; }
    public BoundingSphere BoundingSphere { get; private set; } = BoundingSphere.Empty;
    public Matrix World { get; set; } = Matrix.Identity;

    public RiverRenderObject()
    {
        BoundingBox = (BoundingBoxExt)new BoundingBox(Vector3.Zero, Vector3.One);
    }

    public void Rebuild(GraphicsDevice graphicsDevice, RiverMeshData mesh, int sourceVersion)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(mesh);

        ReleaseGpuResources();

        SegmentId = mesh.SegmentId;
        SourceVersion = sourceVersion;
        IndexCount = mesh.Indices.Length;
        BoundingBox = (BoundingBoxExt)mesh.BoundingBox;
        BoundingSphere = mesh.BoundingSphere;

        if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
        {
            Enabled = false;
            return;
        }

        VertexBuffer = Buffer.Vertex.New(graphicsDevice, mesh.Vertices, GraphicsResourceUsage.Dynamic);
        IndexBuffer = Buffer.Index.New(graphicsDevice, mesh.Indices);
        MeshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = mesh.Indices.Length,
            StartLocation = 0,
            VertexBuffers =
            [
                new VertexBufferBinding(VertexBuffer, RiverVertex.Layout, mesh.Vertices.Length),
            ],
            IndexBuffer = new IndexBufferBinding(IndexBuffer, true, mesh.Indices.Length),
        };
    }

    public void ReleaseGpuResources()
    {
        VertexBuffer?.Dispose();
        VertexBuffer = null;

        IndexBuffer?.Dispose();
        IndexBuffer = null;

        MeshDraw = null;
        IndexCount = 0;
    }

    public void Dispose()
    {
        ReleaseGpuResources();
    }
}
