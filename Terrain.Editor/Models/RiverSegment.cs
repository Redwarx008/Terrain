#nullable enable

using System.Collections.Generic;
using Stride.Core.Mathematics;

namespace Terrain.Editor.Models;

public sealed class RiverSegment
{
    public List<(int X, int Y)> Cells { get; set; } = new();
    public SegmentEndKind StartKind { get; set; } = SegmentEndKind.None;
    public SegmentEndKind EndKind { get; set; } = SegmentEndKind.None;
    public int StartNodeKey { get; set; } = -1;
    public int EndNodeKey { get; set; } = -1;
    public float AvgHalfWidth { get; set; } = 0.625f;
    public List<float> CellHalfWidths { get; set; } = new();
    public List<Vector3> Centerline { get; set; } = new();
    public List<float> CenterlineHalfWidths { get; set; } = new();
    public float WorldLength { get; set; }
    public bool TaperStart { get; set; }
    public bool TaperEnd { get; set; }
    public bool IsLoop { get; set; }

    /// <summary>River system ID this segment belongs to (1-based)</summary>
    public int SystemId { get; set; }
}

public readonly record struct RiverJunction(int X, int Y, RiverPixelType Kind)
{
    public int Key => (Y << 16) | X;
}
