#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Rendering.Ocean;

public sealed class OceanRenderObject : RenderObject, IDisposable
{
    internal const float BoundsVerticalPadding = 8.0f;

    public Buffer? VertexBuffer { get; private set; }
    public Buffer? IndexBuffer { get; private set; }
    public MeshDraw? MeshDraw { get; private set; }
    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }
    public float SeaLevel { get; private set; }
    public Vector2 MapWorldSize { get; private set; }
    public BoundingSphere BoundingSphere { get; private set; } = BoundingSphere.Empty;
    public Matrix World { get; set; } = Matrix.Identity;
    internal bool IsRegisteredWithVisibilityGroup { get; set; }

    public OceanRenderObject()
    {
        BoundingBox = (BoundingBoxExt)new BoundingBox(Vector3.Zero, Vector3.One);
    }

    public bool Matches(OceanRuntimeInput input)
    {
        return IndexCount > 0
            && SeaLevel == input.SeaLevel
            && MapWorldSize == input.MapWorldSize;
    }

    public void Rebuild(GraphicsDevice graphicsDevice, OceanRuntimeInput input)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        ReleaseGpuResources();

        OceanQuadData quad = BuildQuad(input);
        ApplyCpuQuadState(input, quad);

        VertexBuffer = Buffer.Vertex.New(graphicsDevice, quad.Vertices, GraphicsResourceUsage.Dynamic);
        IndexBuffer = Buffer.Index.New(graphicsDevice, quad.Indices);
        MeshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = quad.Indices.Length,
            StartLocation = 0,
            VertexBuffers =
            [
                new VertexBufferBinding(VertexBuffer, OceanVertex.Layout, quad.Vertices.Length),
            ],
            IndexBuffer = new IndexBufferBinding(IndexBuffer, true, quad.Indices.Length),
        };
    }

    internal static OceanQuadData BuildQuad(OceanRuntimeInput input)
    {
        if (input.MapWorldSize.X <= 0.0f || input.MapWorldSize.Y <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(input), "Ocean map world size must be positive.");

        float seaLevel = input.SeaLevel;
        float width = input.MapWorldSize.X;
        float height = input.MapWorldSize.Y;
        var vertices = new[]
        {
            new OceanVertex(new Vector3(0.0f, seaLevel, 0.0f), new Vector2(0.0f, 0.0f)),
            new OceanVertex(new Vector3(width, seaLevel, 0.0f), new Vector2(1.0f, 0.0f)),
            new OceanVertex(new Vector3(width, seaLevel, height), new Vector2(1.0f, 1.0f)),
            new OceanVertex(new Vector3(0.0f, seaLevel, height), new Vector2(0.0f, 1.0f)),
        };
        int[] indices = [0, 1, 2, 0, 2, 3];
        var bounds = new BoundingBox(
            new Vector3(0.0f, seaLevel - BoundsVerticalPadding, 0.0f),
            new Vector3(width, seaLevel + BoundsVerticalPadding, height));

        return new OceanQuadData(vertices, indices, bounds, BoundingSphere.FromBox(bounds));
    }

    internal void ApplyCpuQuadState(OceanRuntimeInput input, OceanQuadData quad)
    {
        SeaLevel = input.SeaLevel;
        MapWorldSize = input.MapWorldSize;
        VertexCount = quad.Vertices.Length;
        IndexCount = quad.Indices.Length;
        BoundingBox = (BoundingBoxExt)quad.BoundingBox;
        BoundingSphere = quad.BoundingSphere;
    }

    public void ReleaseGpuResources()
    {
        VertexBuffer?.Dispose();
        VertexBuffer = null;

        IndexBuffer?.Dispose();
        IndexBuffer = null;

        MeshDraw = null;
        VertexCount = 0;
        IndexCount = 0;
        BoundingSphere = BoundingSphere.Empty;
    }

    public void Dispose()
    {
        ReleaseGpuResources();
    }

    internal readonly record struct OceanQuadData(
        OceanVertex[] Vertices,
        int[] Indices,
        BoundingBox BoundingBox,
        BoundingSphere BoundingSphere);
}
