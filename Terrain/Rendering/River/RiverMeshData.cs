#nullable enable

using System;
using Stride.Core.Mathematics;

namespace Terrain.Rendering.River;

public sealed class RiverMeshData
{
    public int SegmentId { get; init; }
    public RiverVertex[] Vertices { get; init; } = Array.Empty<RiverVertex>();
    public int[] Indices { get; init; } = Array.Empty<int>();
    public BoundingBox BoundingBox { get; init; } = BoundingBox.Empty;
    public BoundingSphere BoundingSphere { get; init; } = BoundingSphere.Empty;
    public float WorldLength { get; init; }
    public float AvgHalfWidth { get; init; }
    public float MapExtent { get; init; } = 4096.0f;
    public Vector2 MapWorldSize { get; init; } = new(4096.0f, 4096.0f);
    public float RefractionMaxCameraHeight { get; init; } = 50.0f;

    public RiverMeshData CloneSnapshot()
    {
        return new RiverMeshData
        {
            SegmentId = SegmentId,
            Vertices = (RiverVertex[])Vertices.Clone(),
            Indices = (int[])Indices.Clone(),
            BoundingBox = BoundingBox,
            BoundingSphere = BoundingSphere,
            WorldLength = WorldLength,
            AvgHalfWidth = AvgHalfWidth,
            MapExtent = MapExtent,
            MapWorldSize = MapWorldSize,
            RefractionMaxCameraHeight = RefractionMaxCameraHeight,
        };
    }
}
