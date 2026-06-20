#nullable enable

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Diagnostics;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

public sealed class RiverMapService
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");
    public RiverCell[,]? Cells { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public List<string> Errors { get; } = new();
    public int SystemCount { get; private set; }

    public bool Load(string pngPath)
    {
        Errors.Clear();
        Cells = null;

        if (!System.IO.File.Exists(pngPath))
        {
            Errors.Add($"File not found: {pngPath}");
            return false;
        }

        try
        {
            using var image = Image.Load<Rgba32>(pngPath);
            Width = image.Width;
            Height = image.Height;
            Cells = new RiverCell[Width, Height];

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    Cells[x, y] = RiverCell.FromRgba32(image[x, y]);

            Validate(); // Validation errors collected but data is still available
            return true;
        }
        catch (Exception ex)
        {
            Errors.Add($"Load error: {ex.Message}");
            return false;
        }
    }

    public bool Load(RiverCell[,] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        Errors.Clear();
        Width = cells.GetLength(0);
        Height = cells.GetLength(1);
        Cells = (RiverCell[,])cells.Clone();
        Validate();
        return true;
    }

    public bool Validate()
    {
        if (Cells == null) return false;
        Errors.Clear();

        int w = Width, h = Height;

        // Check orthogonal adjacency for all river pixels
        int[] dx = [0, 1, 0, -1];
        int[] dy = [-1, 0, 1, 0];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Cells[x, y].IsFilled) continue;

                int orthoCount = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h && Cells[nx, ny].IsFilled)
                        orthoCount++;
                }
                if (orthoCount > 3)
                    Errors.Add($"Pixel ({x},{y}): {orthoCount} orthogonal neighbors (max 3 for T-junctions at confluences)");

                // Special pixels are semantic endpoints; extraction starts from adjacent River pixels.
                if (Cells[x, y].Type is RiverPixelType.Confluence or RiverPixelType.Bifurcation or RiverPixelType.Source)
                {
                    bool hasRiverNeighbor = false;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + dx[d], ny = y + dy[d];
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && Cells[nx, ny].Type == RiverPixelType.River)
                        {
                            hasRiverNeighbor = true;
                            break;
                        }
                    }
                    if (!hasRiverNeighbor)
                        Errors.Add($"{Cells[x, y].Type} at ({x},{y}) has no River neighbor");
                }
            }

        // Check each river system has exactly 1 source
        var visited = new bool[w, h];
        SystemCount = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Cells[x, y].IsFilled || visited[x, y]) continue;

                // Flood fill to find connected component
                var component = new List<(int X, int Y)>();
                FloodFill(x, y, Cells, w, h, visited, component);
                SystemCount++;

                int sourceCount = 0;
                foreach (var (cx, cy) in component)
                    if (Cells[cx, cy].Type == RiverPixelType.Source)
                        sourceCount++;

                if (sourceCount == 0)
                    Log.Warning($"River system at ({x},{y}): no source pixel (isolated fragment or tributary without explicit source marker)");
                else if (sourceCount > 1)
                    Log.Warning($"River system at ({x},{y}): {sourceCount} source pixels (only first will be used as genuine source)");
            }

        return Errors.Count == 0;
    }

    private static void FloodFill(int sx, int sy, RiverCell[,] cells, int w, int h, bool[,] visited, List<(int, int)> result)
    {
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || x >= w || y < 0 || y >= h) continue;
            if (visited[x, y] || !cells[x, y].IsFilled) continue;
            visited[x, y] = true;
            result.Add((x, y));
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
    }

    public List<RiverSegment> ExtractSegments()
    {
        var segments = new List<RiverSegment>();
        if (Cells == null) return segments;

        int w = Width, h = Height;
        var visited = new bool[w, h];

        // Collect all special pixels (Source, Confluence, Bifurcation)
        var specialPixels = new Dictionary<int, (int X, int Y, RiverPixelType Kind)>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = Cells[x, y].Type;
                if (t is RiverPixelType.Source or RiverPixelType.Confluence or RiverPixelType.Bifurcation)
                {
                    int key = y * 65536 + x;
                    specialPixels[key] = (x, y, t);
                }
            }

        // Trace from each special pixel: walk along River cells
        int[] dx = [0, 1, 0, -1];
        int[] dy = [-1, 0, 1, 0];

        foreach (var sp in specialPixels.Values)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = sp.X + dx[d], ny = sp.Y + dy[d];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (Cells[nx, ny].Type != RiverPixelType.River) continue;
                if (visited[nx, ny]) continue;

                // Trace this path
                var seg = TracePath(nx, ny, sp.X, sp.Y, Cells, w, h, visited, specialPixels, dx, dy);
                if (seg != null && seg.Cells.Count > 0)
                {
                    seg.StartKind = KindFromPixel(Cells[sp.X, sp.Y].Type);
                    seg.StartNodeKey = sp.Y * 65536 + sp.X;
                    NormalizeDirection(seg);
                    seg.AvgHalfWidth = ComputeAvgWidth(seg, Cells);
                    segments.Add(seg);
                }
            }
        }

        // Assign system IDs
        AssignSystemIds(segments);

        return segments;
    }

    private static RiverSegment? TracePath(
        int startX, int startY, int fromX, int fromY,
        RiverCell[,] cells, int w, int h, bool[,] visited,
        Dictionary<int, (int X, int Y, RiverPixelType Kind)> specialPixels,
        int[] dx, int[] dy)
    {
        var seg = new RiverSegment();
        seg.Cells.Add((fromX, fromY));
        int cx = startX, cy = startY, px = fromX, py = fromY;

        while (cx >= 0 && cx < w && cy >= 0 && cy < h)
        {
            visited[cx, cy] = true;
            seg.Cells.Add((cx, cy));

            // Check if we hit a special pixel (end of segment)
            int key = cy * 65536 + cx;
            if (specialPixels.ContainsKey(key))
            {
                seg.EndKind = KindFromPixel(cells[cx, cy].Type);
                seg.EndNodeKey = key;
                break;
            }

            // Prefer stepping into an adjacent semantic node before considering side continuations.
            // Real rivers.png layouts can place a confluence/bifurcation marker next to the branch
            // while another River pixel also touches the last branch cell.
            int nextX = -1, nextY = -1;
            int specialNeighborCount = 0;
            int filledNeighborCount = 0;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d], ny = cy + dy[d];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (nx == px && ny == py) continue;

                RiverPixelType neighborType = cells[nx, ny].Type;
                if (neighborType is RiverPixelType.Source or RiverPixelType.Confluence or RiverPixelType.Bifurcation)
                {
                    nextX = nx; nextY = ny;
                    specialNeighborCount++;
                }
                else if (cells[nx, ny].IsFilled)
                {
                    if (specialNeighborCount == 0)
                    {
                        nextX = nx; nextY = ny;
                    }
                    filledNeighborCount++;
                }
            }

            if (specialNeighborCount == 1)
            {
                px = cx; py = cy;
                cx = nextX; cy = nextY;
                continue;
            }

            if (specialNeighborCount > 1 || filledNeighborCount != 1) break;

            px = cx; py = cy;
            cx = nextX; cy = nextY;
        }

        return seg;
    }

    private static void NormalizeDirection(RiverSegment seg)
    {
        if (GetDirectionRank(seg.StartKind) > GetDirectionRank(seg.EndKind))
        {
            seg.Cells.Reverse();
            (seg.StartKind, seg.EndKind) = (seg.EndKind, seg.StartKind);
            (seg.StartNodeKey, seg.EndNodeKey) = (seg.EndNodeKey, seg.StartNodeKey);
        }
    }

    private static int GetDirectionRank(SegmentEndKind kind) => kind switch
    {
        SegmentEndKind.Source => 0,
        SegmentEndKind.None => 1,
        SegmentEndKind.Confluence => 2,
        SegmentEndKind.Bifurcation => 2,
        _ => 1,
    };

    private static float ComputeAvgWidth(RiverSegment seg, RiverCell[,] cells)
    {
        float total = 0;
        int count = 0;
        foreach (var (x, y) in seg.Cells)
        {
            if (cells[x, y].Type != RiverPixelType.River) continue;
            total += RiverCell.GetHalfWidth(cells[x, y].Width);
            count++;
        }
        return count > 0 ? total / count : 0.625f;
    }

    private static SegmentEndKind KindFromPixel(RiverPixelType t) => t switch
    {
        RiverPixelType.Source => SegmentEndKind.Source,
        RiverPixelType.Confluence => SegmentEndKind.Confluence,
        RiverPixelType.Bifurcation => SegmentEndKind.Bifurcation,
        _ => SegmentEndKind.None,
    };

    private void AssignSystemIds(List<RiverSegment> segments)
    {
        int nextId = 1;
        var assigned = new HashSet<RiverSegment>();
        foreach (var seg in segments)
        {
            if (assigned.Contains(seg)) continue;

            var queue = new Queue<RiverSegment>();
            queue.Enqueue(seg);
            int sysId = nextId++;

            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                if (!assigned.Add(s)) continue;
                s.SystemId = sysId;

                foreach (var other in segments)
                {
                    if (assigned.Contains(other)) continue;
                    if (other.StartNodeKey == s.StartNodeKey || other.StartNodeKey == s.EndNodeKey ||
                        other.EndNodeKey == s.StartNodeKey || other.EndNodeKey == s.EndNodeKey)
                        queue.Enqueue(other);
                }
            }
        }
    }
}
