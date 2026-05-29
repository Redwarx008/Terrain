# River System from Color Map — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a complete river system that reads a color-indexed PNG (13 width colors + 3 junction types), traces pixel paths, generates ribbon meshes via Catmull-Rom splines, and renders each segment as an independent Entity with CK3-style dual-pass shaders.

**Architecture:** Three-layer: (1) RiverMapService loads PNG → RiverCell[,] with validation, (2) RiverMeshService traces segments → Catmull-Rom splines → ribbon vertices → VertexBuffer/IndexBuffer per segment, (3) RiverRenderingService manages per-segment Entities with dual-pass SDSL materials. Editor integration via EditorMode.River and Avalonia inspector panel.

**Tech Stack:** C# 13, Stride Engine, SDSL shaders, SixLabors.ImageSharp (existing), Tommy TOML (existing), Avalonia UI (existing)

**File Structure:**

| File | Responsibility |
|---|---|
| `Terrain.Editor/Models/RiverPixelType.cs` | RiverPixelType enum, RiverCell record, 13-color width palette |
| `Terrain.Editor/Models/RiverSegment.cs` | RiverSegment data structure, SegmentEndKind enum, RiverJunction |
| `Terrain.Editor/Services/RiverMapService.cs` | PNG → RiverCell[,] loading, color matching, validation |
| `Terrain.Editor/Services/RiverMeshService.cs` | Pixel tracing, Catmull-Rom spline, ribbon mesh generation, terrain height sampling |
| `Terrain.Editor/Services/RiverRenderingService.cs` | Entity management, VertexBuffer/IndexBuffer, Material creation, scene lifecycle |
| `Terrain.Editor/Effects/RiverBottom.sdsl` + `.cs` | Bottom pass shader (simplified parallax) |
| `Terrain.Editor/Effects/RiverSurface.sdsl` + `.cs` | Surface pass shader (flow normals + water color + edge fade) |
| `Terrain.Editor/Effects/RiverEffect.sdfx` + `.cs` | Combined effect file composing both passes |
| `Terrain.Editor/ViewModels/RiverViewModel.cs` | River mode Avalonia ViewModel |
| `Terrain.Editor/Models/EditorMode.cs` | Add `River` enum value |

| Modified File | Change |
|---|---|
| `Terrain.Editor/Services/TerrainManager.cs` | Add RiverMap property, river path fields, save/load integration |
| `Terrain.Editor/Services/TomlProjectConfig.cs` | Add `RiverMapImagePath` field, read/write logic |
| `Terrain.Editor/ViewModels/EditorShellViewModel.cs` | Add `IsRiverMode`, River Mode tools, bindings |
| `Terrain.Editor/Views/MainWindow.axaml` | Add River Mode inspector panel |
| `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs` | Add River Mode support |

---

### Task 1: Data Models

**Files:**
- Create: `Terrain.Editor/Models/RiverPixelType.cs`
- Create: `Terrain.Editor/Models/RiverSegment.cs`

- [ ] **Step 1: Create RiverPixelType.cs**

```csharp
#nullable enable

using SixLabors.ImageSharp.PixelFormats;

namespace Terrain.Editor.Models;

public enum RiverPixelType : byte
{
    Land = 0,
    River = 1,
    Source = 2,
    Confluence = 3,
    Bifurcation = 4,
    Ocean = 5,
}

public readonly record struct RiverCell(RiverPixelType Type, byte Width = 0)
{
    private static readonly (Rgba32 Color, float HalfWidth)[] WidthPalette =
    [
        (new(0x00, 0xe5, 0xff), 0.625f),   // narrowest
        (new(0x00, 0xcb, 0xff), 0.688f),
        (new(0x00, 0x96, 0xff), 0.750f),
        (new(0x00, 0x5f, 0xff), 0.813f),
        (new(0x15, 0x00, 0xff), 0.875f),
        (new(0x11, 0x00, 0xe9), 0.938f),
        (new(0x0e, 0x00, 0xcf), 1.000f),
        (new(0x08, 0x00, 0x9b), 1.063f),
        (new(0x03, 0x00, 0x68), 1.125f),
        (new(0x00, 0x58, 0x00), 1.188f),
        (new(0x00, 0x81, 0x00), 1.250f),
        (new(0x00, 0xa3, 0x00), 1.313f),
        (new(0x00, 0xd4, 0x00), 1.375f),   // widest
    ];

    public static float GetHalfWidth(int paletteIndex) =>
        paletteIndex >= 0 && paletteIndex < WidthPalette.Length ? WidthPalette[paletteIndex].HalfWidth : 0.625f;

    public static int GetPaletteIndex(Rgba32 color)
    {
        for (int i = 0; i < WidthPalette.Length; i++)
            if (ColorMatch(WidthPalette[i].Color, color))
                return i;
        return -1;
    }

    private static bool ColorMatch(Rgba32 a, Rgba32 b) =>
        Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.G - b.G) <= 2 && Math.Abs(a.B - b.B) <= 2;

    private static readonly Rgba32 SourceColor = new(0, 255, 0);
    private static readonly Rgba32 ConfluenceColor = new(255, 0, 0);
    private static readonly Rgba32 BifurcationColor = new(255, 252, 0);
    private static readonly Rgba32 OceanColor = new(255, 0, 128);

    public static RiverCell FromRgba32(Rgba32 p)
    {
        int paletteIndex = GetPaletteIndex(p);
        if (paletteIndex >= 0)
            return new(RiverPixelType.River, (byte)paletteIndex);
        if (ColorMatch(SourceColor, p))
            return new(RiverPixelType.Source);
        if (ColorMatch(ConfluenceColor, p))
            return new(RiverPixelType.Confluence);
        if (ColorMatch(BifurcationColor, p))
            return new(RiverPixelType.Bifurcation);
        if (ColorMatch(OceanColor, p))
            return new(RiverPixelType.Ocean);
        return new(RiverPixelType.Land);
    }

    public bool IsFilled => Type is not (RiverPixelType.Land or RiverPixelType.Ocean);
}

public enum SegmentEndKind
{
    None,
    Source,
    Confluence,
    Bifurcation,
}
```

- [ ] **Step 2: Create RiverSegment.cs**

```csharp
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
    public List<Vector3> Centerline { get; set; } = new();
    public float WorldLength { get; set; }
    public bool TaperStart { get; set; }
    public bool TaperEnd { get; set; }
    public bool IsLoop { get; set; }

    /// <summary>River system ID this segment belongs to (1-based)</summary>
    public int SystemId { get; set; }
}

public readonly record struct RiverJunction(int X, int Y, RiverPixelType Kind)
{
    public int Key => Y * 65536 + X;
}
```

- [ ] **Step 3: Verify compilation**

```bash
cd "e:/Stride Projects/Terrain" && dotnet build Terrain.Editor/Terrain.Editor.csproj --no-restore -v q 2>&1 | tail -20
```

Expected: Build succeeds (or only has expected warnings).

- [ ] **Step 4: Commit**

```bash
git add Terrain.Editor/Models/RiverPixelType.cs Terrain.Editor/Models/RiverSegment.cs
git commit -m "feat: add river data models (RiverPixelType, RiverCell, RiverSegment)"
```

---

### Task 2: Color Map Import & Validation

**Files:**
- Create: `Terrain.Editor/Services/RiverMapService.cs`

- [ ] **Step 1: Create RiverMapService.cs**

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

public sealed class RiverMapService
{
    public RiverCell[,]? Cells { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public List<string> Errors { get; } = new();

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
            {
                var row = image.DangerousTryGetSinglePixelRowMemory(y).Span;
                for (int x = 0; x < Width; x++)
                {
                    Cells[x, y] = RiverCell.FromRgba32(row[x]);
                }
            }

            return Validate();
        }
        catch (Exception ex)
        {
            Errors.Add($"Load error: {ex.Message}");
            return false;
        }
    }

    public bool Validate()
    {
        if (Cells == null) return false;
        Errors.Clear();

        int w = Width, h = Height;

        // Check orthogonal adjacency for all river pixels
        var (dx, dy) = (new[] { 0, 1, 0, -1 }, new[] { -1, 0, 1, 0 });
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
                if (orthoCount > 2)
                    Errors.Add($"Pixel ({x},{y}): {orthoCount} orthogonal neighbors (max 2)");
            }

        // Check each river system has exactly 1 source
        var visited = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Cells[x, y].IsFilled || visited[x, y]) continue;

                // Flood fill to find connected component
                var component = new List<(int X, int Y)>();
                FloodFill(x, y, Cells, w, h, visited, component);

                int sourceCount = 0;
                foreach (var (cx, cy) in component)
                    if (Cells[cx, cy].Type == RiverPixelType.Source)
                        sourceCount++;

                if (sourceCount == 0)
                    Errors.Add($"River system at ({x},{y}): no source pixel");
                else if (sourceCount > 1)
                    Errors.Add($"River system at ({x},{y}): {sourceCount} source pixels (max 1)");
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
}
```

- [ ] **Step 2: Commit**

```bash
git add Terrain.Editor/Services/RiverMapService.cs
git commit -m "feat: add RiverMapService for PNG loading and validation"
```

---

### Task 3: Pixel Tracing & River Segment Extraction

**Files:**
- Modify: `Terrain.Editor/Services/RiverMapService.cs`

- [ ] **Step 1: Add segment extraction to RiverMapService**

Add these methods to `RiverMapService`:

```csharp
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

    // Trace from each special pixel: walk along River cells until hitting another special pixel or dead end
    var (dx, dy) = (new[] { 0, 1, 0, -1 }, new[] { -1, 0, 1, 0 });

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
                // Determine which end is the special pixel
                seg.StartKind = KindFromPixel(Cells[sp.X, sp.Y].Type);
                seg.StartNodeKey = sp.Y * 65536 + sp.X;
                seg.AvgHalfWidth = ComputeAvgWidth(seg, Cells);
                segments.Add(seg);
            }
        }
    }

    // Assign system IDs
    AssignSystemIds(segments, specialPixels);

    return segments;
}

private static RiverSegment? TracePath(
    int startX, int startY, int fromX, int fromY,
    RiverCell[,] cells, int w, int h, bool[,] visited,
    Dictionary<int, (int X, int Y, RiverPixelType Kind)> specialPixels,
    int[] dx, int[] dy)
{
    var seg = new RiverSegment();
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

        // Find next River pixel (excluding the one we came from)
        int nextX = -1, nextY = -1;
        int neighborCount = 0;
        for (int d = 0; d < 4; d++)
        {
            int nx = cx + dx[d], ny = cy + dy[d];
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
            if (nx == px && ny == py) continue;
            if (cells[nx, ny].IsFilled)
            {
                nextX = nx; nextY = ny;
                neighborCount++;
            }
        }

        if (neighborCount != 1) break; // dead end or junction

        px = cx; py = cy;
        cx = nextX; cy = nextY;
    }

    return seg;
}

private static float ComputeAvgWidth(RiverSegment seg, RiverCell[,] cells)
{
    float total = 0;
    foreach (var (x, y) in seg.Cells)
        total += RiverCell.GetHalfWidth(cells[x, y].Width);
    return seg.Cells.Count > 0 ? total / seg.Cells.Count : 0.625f;
}

private static SegmentEndKind KindFromPixel(RiverPixelType t) => t switch
{
    RiverPixelType.Source => SegmentEndKind.Source,
    RiverPixelType.Confluence => SegmentEndKind.Confluence,
    RiverPixelType.Bifurcation => SegmentEndKind.Bifurcation,
    _ => SegmentEndKind.None,
};

private static void AssignSystemIds(List<RiverSegment> segments, Dictionary<int, (int X, int Y, RiverPixelType Kind)> specialPixels)
{
    int nextId = 1;
    var assigned = new HashSet<RiverSegment>();
    foreach (var seg in segments)
    {
        if (assigned.Contains(seg)) continue;

        // Flood through shared node keys
        var queue = new Queue<RiverSegment>();
        queue.Enqueue(seg);
        int sysId = nextId++;

        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            if (!assigned.Add(s)) continue;
            s.SystemId = sysId;

            // Find segments sharing node keys
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
```

- [ ] **Step 2: Commit**

```bash
git add Terrain.Editor/Services/RiverMapService.cs
git commit -m "feat: add pixel tracing and river segment extraction"
```

---

### Task 4: Catmull-Rom Centerline & Ribbon Mesh Generation

**Files:**
- Create: `Terrain.Editor/Services/RiverMeshService.cs`

- [ ] **Step 1: Create RiverMeshService.cs**

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Terrain.Editor.Models;
using Terrain.Editor.Services;

namespace Terrain.Editor.Services;

public sealed class RiverMeshService
{
    private const float CurveSampleSpacing = 2.0f;
    private const float ConnectionTaperDistance = 6.0f;
    private const float SurfaceOffset = 0.02f;
    private const float MinVisibleHalfWidth = 0.05f;

    private readonly TerrainManager terrainManager;

    public RiverMeshService(TerrainManager terrainManager)
    {
        this.terrainManager = terrainManager;
    }

    public void BuildCenterlines(List<RiverSegment> segments, int mapWidth, int mapHeight, float terrainWorldSize)
    {
        float pixelToWorld = terrainWorldSize / Math.Max(mapWidth, mapHeight);

        foreach (var seg in segments)
        {
            if (seg.Cells.Count < 2) continue;

            // Build raw centerline from pixel centers
            var rawPoints = new List<Vector3>();
            foreach (var (x, y) in seg.Cells)
            {
                float wx = (x + 0.5f) * pixelToWorld;
                float wz = (y + 0.5f) * pixelToWorld;
                float wy = SampleTerrainHeight(wx, wz) + SurfaceOffset;
                rawPoints.Add(new Vector3(wx, wy, wz));
            }

            // Catmull-Rom interpolation
            seg.Centerline = CatmullRomInterpolate(rawPoints, CurveSampleSpacing);
            seg.WorldLength = ComputePolylineLength(seg.Centerline);

            // Taper flags based on segment ends
            if (seg.StartKind == SegmentEndKind.Source || seg.StartKind == SegmentEndKind.None)
                seg.TaperStart = seg.StartKind == SegmentEndKind.Source;
            if (seg.EndKind == SegmentEndKind.Confluence || seg.EndKind == SegmentEndKind.Bifurcation)
                seg.TaperEnd = seg.EndKind != SegmentEndKind.None;
        }
    }

    private static List<Vector3> CatmullRomInterpolate(List<Vector3> controlPoints, float spacing)
    {
        if (controlPoints.Count < 2) return new List<Vector3>(controlPoints);

        var result = new List<Vector3> { controlPoints[0] };
        float accumulated = 0;

        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            Vector3 p0 = controlPoints[Math.Max(0, i - 1)];
            Vector3 p1 = controlPoints[i];
            Vector3 p2 = controlPoints[i + 1];
            Vector3 p3 = controlPoints[Math.Min(controlPoints.Count - 1, i + 2)];

            float segmentLength = Vector3.Distance(p1, p2);
            if (segmentLength < 0.001f) continue;

            int steps = Math.Max(1, (int)(segmentLength / spacing));
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 point = CatmullRom(p0, p1, p2, p3, t);
                float dist = Vector3.Distance(result[^1], point);
                accumulated += dist;
                if (accumulated >= spacing)
                {
                    result.Add(point);
                    accumulated = 0;
                }
            }
        }

        // Ensure last point
        if (Vector3.Distance(result[^1], controlPoints[^1]) > 0.01f)
            result.Add(controlPoints[^1]);

        return result;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static float ComputePolylineLength(List<Vector3> points)
    {
        float len = 0;
        for (int i = 1; i < points.Count; i++)
            len += Vector3.Distance(points[i - 1], points[i]);
        return len;
    }

    private float SampleTerrainHeight(float wx, float wz)
    {
        if (!terrainManager.HasHeightCache) return 0;
        float scale = terrainManager.HeightScale;
        var data = terrainManager.HeightDataCache;
        int w = terrainManager.HeightCacheWidth;
        int h = terrainManager.HeightCacheHeight;

        // World to heightmap pixel
        float terrainWorldSize = h; // heightmap dimension in world units (1 pixel = 1 world unit)
        float u = (wx / terrainWorldSize + 0.5f) * w;
        float v = (wz / terrainWorldSize + 0.5f) * h;

        int ix = Math.Clamp((int)u, 0, w - 1);
        int iy = Math.Clamp((int)v, 0, h - 1);
        return data[iy * w + ix] * (1.0f / ushort.MaxValue) * scale;
    }

    // Ribbon mesh generation in Task 5 below
}
```

- [ ] **Step 2: Commit**

```bash
git add Terrain.Editor/Services/RiverMeshService.cs
git commit -m "feat: add Catmull-Rom centerline generation for river segments"
```

---

### Task 5: Ribbon Mesh & Vertex Buffer Generation

**Files:**
- Modify: `Terrain.Editor/Services/RiverMeshService.cs`

- [ ] **Step 1: Add ribbon generation methods to RiverMeshService.cs**

```csharp
// Append to RiverMeshService.cs

public (VertexPositionNormalTexture[] Vertices, int[] Indices) BuildRibbonMesh(RiverSegment segment, float widthScale)
{
    var centerline = segment.Centerline;
    if (centerline == null || centerline.Count < 2)
        return (Array.Empty<VertexPositionNormalTexture>(), Array.Empty<int>());

    float totalLength = segment.WorldLength;
    float baseHalfWidth = Math.Max(MinVisibleHalfWidth, segment.AvgHalfWidth * widthScale);

    int n = centerline.Count;
    var vertices = new List<VertexPositionNormalTexture>(n * 2);
    var indices = new List<int>(n * 6);

    float accumulated = 0;
    for (int i = 0; i < n; i++)
    {
        // Tangent = direction to next point
        Vector3 tangent;
        if (i < n - 1)
            tangent = Vector3.Normalize(new Vector3(
                centerline[i + 1].X - centerline[i].X, 0,
                centerline[i + 1].Z - centerline[i].Z));
        else
            tangent = Vector3.Normalize(new Vector3(
                centerline[i].X - centerline[i - 1].X, 0,
                centerline[i].Z - centerline[i - 1].Z));

        if (tangent.Length() < 0.001f) tangent = new Vector3(1, 0, 0);

        // Perpendicular = horizontal 90° rotation
        Vector3 side = Vector3.Normalize(new Vector3(-tangent.Z, 0, tangent.X));

        // Taper scale
        float u = i > 0 ? accumulated / totalLength : 0;
        float taperScale = ComputeTaperScale(u, segment.TaperStart, segment.TaperEnd);
        float halfWidth = baseHalfWidth * taperScale;

        Vector3 center = centerline[i];
        Vector3 leftPos = center - side * halfWidth;
        Vector3 rightPos = center + side * halfWidth;

        Vector3 normal = SampleTerrainNormal(center.X, center.Z);

        vertices.Add(new VertexPositionNormalTexture(leftPos, normal, new Vector2(u, 0)));
        vertices.Add(new VertexPositionNormalTexture(rightPos, normal, new Vector2(u, 1)));

        if (i > 0)
        {
            int a = (i - 1) * 2;
            int b = a + 1;
            int c = i * 2;
            int d = c + 1;
            indices.Add(a); indices.Add(c); indices.Add(b);
            indices.Add(b); indices.Add(c); indices.Add(d);
        }

        if (i < n - 1)
            accumulated += Vector3.Distance(centerline[i], centerline[i + 1]);
    }

    return (vertices.ToArray(), indices.ToArray());
}

private static float ComputeTaperScale(float u, bool taperStart, bool taperEnd)
{
    float scale = 1.0f;
    float taperU = Math.Min(1.0f, ConnectionTaperDistance / (WorldLength > 0 ? WorldLength : 1));
    if (taperStart && u < taperU)
        scale = Math.Min(scale, SmoothStep(u / taperU));
    if (taperEnd && u > 1.0f - taperU)
        scale = Math.Min(scale, SmoothStep((1.0f - u) / taperU));
    return scale;
}

private static float SmoothStep(float t) => t * t * (3.0f - 2.0f * t);

private Vector3 SampleTerrainNormal(float wx, float wz)
{
    // Simple central-difference normal from height cache
    float h = 0.5f;
    float cx = SampleTerrainHeight(wx, wz);
    float dx = SampleTerrainHeight(wx + h, wz);
    float dz = SampleTerrainHeight(wx, wz + h);
    var n = Vector3.Normalize(new Vector3(cx - dx, h, cx - dz));
    return n.LengthSquared() > 0 ? n : new Vector3(0, 1, 0);
}
```

- [ ] **Step 2: Commit**

```bash
git add Terrain.Editor/Services/RiverMeshService.cs
git commit -m "feat: add ribbon mesh generation with taper and terrain normals"
```

---

### Task 6: River Rendering Service (Entity Management)

**Files:**
- Create: `Terrain.Editor/Services/RiverRenderingService.cs`

- [ ] **Step 1: Create RiverRenderingService.cs**

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Terrain.Editor.Models;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Services;

public sealed class RiverRenderingService : IDisposable
{
    private const float DefaultDepthBias = -50000;

    private readonly GraphicsDevice graphicsDevice;
    private readonly Scene scene;
    private readonly List<Entity> riverEntities = new();
    private Entity? riverContainer;

    public RiverRenderingService(GraphicsDevice graphicsDevice, Scene scene)
    {
        this.graphicsDevice = graphicsDevice;
        this.scene = scene;
    }

    public void UpdateMeshes(List<RiverSegment> segments, RiverMeshService meshService, float widthScale)
    {
        ClearMeshes();

        // Create container entity
        riverContainer = new Entity("RiverSystem");
        scene.Entities.Add(riverContainer);

        foreach (var seg in segments)
        {
            var (vertices, indices) = meshService.BuildRibbonMesh(seg, widthScale);
            if (vertices.Length == 0) continue;

            // Create vertex/index buffer
            var vertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices, GraphicsResourceUsage.Dynamic);
            var indexBuffer = Buffer.Index.New(graphicsDevice, indices);

            // Create mesh
            var meshDraw = new MeshDraw
            {
                DrawCount = indices.Length,
                PrimitiveType = PrimitiveType.TriangleList,
                VertexBuffers = new[] { new VertexBufferBinding(vertexBuffer, VertexPositionNormalTexture.Layout, vertexBuffer.ElementCount) },
                IndexBuffer = new IndexBufferBinding(indexBuffer, true, indexBuffer.ElementCount),
            };

            var mesh = new Mesh(meshDraw) { BoundingSphere = BoundingSphere.Empty };

            // Create entity
            var entity = new Entity($"RiverSegment_{seg.SystemId}_{riverEntities.Count}")
            {
                new ModelComponent { Model = new Model { mesh } }
            };

            riverEntities.Add(entity);
            riverContainer.AddChild(entity);
        }
    }

    public void SetVisible(bool visible)
    {
        if (riverContainer != null)
            riverContainer.Transform.EnableChildNodes = visible;
    }

    public void ClearMeshes()
    {
        foreach (var entity in riverEntities)
        {
            entity.Scene?.Entities.Remove(entity);
            entity.Dispose();
        }
        riverEntities.Clear();

        if (riverContainer != null)
        {
            scene.Entities.Remove(riverContainer);
            riverContainer.Dispose();
            riverContainer = null;
        }
    }

    public void Dispose()
    {
        ClearMeshes();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Terrain.Editor/Services/RiverRenderingService.cs
git commit -m "feat: add river rendering service with per-segment entity management"
```

---

### Task 7: SDSL Shaders — River Surface

**Files:**
- Create: `Terrain.Editor/Effects/RiverSurface.sdsl`
- Create: `Terrain.Editor/Effects/RiverSurface.sdsl.cs`

- [ ] **Step 1: Create RiverSurface.sdsl**

```hlsl
namespace Terrain.Editor
{
    shader RiverSurface : ShaderBase, Transformation, PositionStream4
    {
        stage float _FlowNormalSpeed = 0.185f;
        stage float _FlowNormalUvScale = 1.2f;
        stage float _BankFade = 0.0f;
        stage float _Depth = 0.15f;
        stage float4 WaterColorShallow = float4(0.0f, 0.3f, 0.5f, 0.7f);
        stage float4 WaterColorDeep = float4(0.0f, 0.05f, 0.15f, 0.85f);
        stage float _GlobalTime;

        stage override void VSMain()
        {
            base.VSMain();
        }

        stage override void PSMain()
        {
            // UV from vertex: x = along river (0~1), y = cross-section (0=left, 1=right)
            float2 uv = streams.TextureCoordinate;

            // Depth curve: deepest at center (uv.y=0.5), shallow at edges
            float depth = _Depth * (1.0f - pow(cos(uv.y * 2.0f * 3.14159f) * 0.5f + 0.5f, 2.0f));

            // Flow normal animation
            float2 flowUV = uv.yx * float2(1.0f, -1.0f);
            flowUV *= _FlowNormalUvScale;
            flowUV.y += _GlobalTime * _FlowNormalSpeed;

            // Simple water color interpolation based on depth
            float depthFactor = saturate(depth / max(_Depth, 0.001f));
            float4 waterColor = lerp(WaterColorShallow, WaterColorDeep, depthFactor);

            // Edge fade
            float edgeFade1 = smoothstep(0.0f, _BankFade, uv.y);
            float edgeFade2 = smoothstep(0.0f, _BankFade, 1.0f - uv.y);
            float alpha = edgeFade1 * edgeFade2 * waterColor.a;

            // Final output
            streams.ColorTarget = float4(waterColor.rgb, alpha);
        }
    }
}
```

- [ ] **Step 2: Create RiverSurface.sdsl.cs**

```csharp
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain.Editor.Effects
{
    public static class RiverSurfaceKeys
    {
        public static readonly ValueParameterKey<float> FlowNormalSpeed = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> FlowNormalUvScale = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> BankFade = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> Depth = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<Color4> WaterColorShallow = ParameterKeys.NewValue<Color4>();
        public static readonly ValueParameterKey<Color4> WaterColorDeep = ParameterKeys.NewValue<Color4>();
        public static readonly ValueParameterKey<float> GlobalTime = ParameterKeys.NewValue<float>();
    }
}
```

- [ ] **Step 3: Register in .csproj**

Add to `Terrain.Editor.csproj` (inside the `<ItemGroup>` with other None/Compile entries):

```xml
<Compile Update="Effects\RiverSurface.sdsl.cs">
  <DesignTime>True</DesignTime>
  <DesignTimeSharedInput>True</DesignTimeSharedInput>
  <AutoGen>True</AutoGen>
</Compile>

<None Update="Effects\RiverSurface.sdsl">
  <LastGenOutput>RiverSurface.sdsl.cs</LastGenOutput>
</None>
```

- [ ] **Step 4: Commit**

```bash
git add Terrain.Editor/Effects/RiverSurface.sdsl Terrain.Editor/Effects/RiverSurface.sdsl.cs Terrain.Editor/Terrain.Editor.csproj
git commit -m "feat: add river surface shader with flow animation and edge fade"
```

---

### Task 8: SDSL Shaders — River Bottom & Effect

**Files:**
- Create: `Terrain.Editor/Effects/RiverBottom.sdsl`
- Create: `Terrain.Editor/Effects/RiverBottom.sdsl.cs`
- Create: `Terrain.Editor/Effects/RiverEffect.sdfx`
- Create: `Terrain.Editor/Effects/RiverEffect.sdfx.cs`

- [ ] **Step 1: Create RiverBottom.sdsl**

```hlsl
namespace Terrain.Editor
{
    shader RiverBottom : ShaderBase, Transformation, PositionStream4
    {
        stage float _Depth = 0.15f;
        stage float _DepthFakeFactor = 0.4f;
        stage float _BankFade = 0.0f;
        stage float4 BottomColor = float4(0.2f, 0.15f, 0.1f, 1.0f);

        stage override void VSMain()
        {
            base.VSMain();
        }

        stage override void PSMain()
        {
            float2 uv = streams.TextureCoordinate;

            // Simplified parallax offset based on view angle
            float depth = _Depth * (1.0f - pow(cos(uv.y * 2.0f * 3.14159f) * 0.5f + 0.5f, 2.0f));

            // Edge fade
            float edgeFade1 = smoothstep(0.0f, _BankFade, uv.y);
            float edgeFade2 = smoothstep(0.0f, _BankFade, 1.0f - uv.y);
            float alpha = edgeFade1 * edgeFade2;

            float3 color = BottomColor.rgb;
            float depthFactor = saturate(depth / max(_Depth, 0.001f));
            color *= (1.0f - depthFactor * _DepthFakeFactor);

            streams.ColorTarget = float4(color, alpha);
        }
    }
}
```

- [ ] **Step 2: Create RiverBottom.sdsl.cs**

```csharp
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain.Editor.Effects
{
    public static class RiverBottomKeys
    {
        public static readonly ValueParameterKey<float> Depth = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> DepthFakeFactor = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> BankFade = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<Color4> BottomColor = ParameterKeys.NewValue<Color4>();
    }
}
```

- [ ] **Step 3: Create RiverEffect.sdfx**

```hlsl
namespace Terrain.Editor
{
    effect RiverEffect
    {
        // Bottom pass
        pass RiverBottomPass
        {
            VertexShader = ShaderBase.Streams.Transformation.TransformationAndWorldViewProjection;
            PixelShader = RiverBottom;

            BlendState = BlendStates.AlphaBlend;
            DepthStencilState = DepthStencilStates.Default;
            RasterizerState = RasterizerStates.CullNone;
        }

        // Surface pass
        pass RiverSurfacePass
        {
            VertexShader = ShaderBase.Streams.Transformation.TransformationAndWorldViewProjection;
            PixelShader = RiverSurface;

            BlendState = BlendStates.AlphaBlend;
            DepthStencilState = DepthStencilStates.Default;
            RasterizerState = RasterizerStates.CullNone;
        }
    }
}
```

- [ ] **Step 4: Create RiverEffect.sdfx.cs**

```csharp
using Stride.Rendering.Materials;
using Stride.Shaders;

namespace Terrain.Editor.Effects
{
    public static class RiverEffectKeys
    {
        // No custom keys needed for the effect itself
    }
}
```

- [ ] **Step 5: Register in .csproj**

Add to `Terrain.Editor.csproj`:

```xml
<Compile Update="Effects\RiverBottom.sdsl.cs">
  <DesignTime>True</DesignTime>
  <DesignTimeSharedInput>True</DesignTimeSharedInput>
  <AutoGen>True</AutoGen>
</Compile>
<Compile Update="Effects\RiverEffect.sdfx.cs">
  <DesignTime>True</DesignTime>
  <DesignTimeSharedInput>True</DesignTimeSharedInput>
  <AutoGen>True</AutoGen>
</Compile>

<None Update="Effects\RiverBottom.sdsl">
  <LastGenOutput>RiverBottom.sdsl.cs</LastGenOutput>
</None>
<None Update="Effects\RiverEffect.sdfx">
  <LastGenOutput>RiverEffect.sdfx.cs</LastGenOutput>
</None>
```

- [ ] **Step 6: Commit**

```bash
git add Terrain.Editor/Effects/RiverBottom.sdsl Terrain.Editor/Effects/RiverBottom.sdsl.cs Terrain.Editor/Effects/RiverEffect.sdfx Terrain.Editor/Effects/RiverEffect.sdfx.cs Terrain.Editor/Terrain.Editor.csproj
git commit -m "feat: add river bottom shader and combined effect file"
```

---

### Task 9: River Material & VertexFormat Setup

**Files:**
- Modify: `Terrain.Editor/Services/RiverRenderingService.cs`
- Create: `Terrain.Editor/Rendering/Materials/MaterialRiverFeature.cs`

- [ ] **Step 1: Create MaterialRiverFeature.cs**

```csharp
#nullable enable

using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Shaders;
using Terrain.Editor.Effects;

namespace Terrain.Editor.Rendering.Materials;

public sealed class MaterialRiverFeature : IMaterialRenderFeature
{
    private Material? cachedMaterial;

    public Material GetOrCreateMaterial(GraphicsDevice device)
    {
        if (cachedMaterial != null)
            return cachedMaterial;

        var desc = new MaterialDescriptor
        {
            Attributes =
            {
                Diffuse = new MaterialDiffuseMapFeature(new ComputeColor()),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            }
        };

        // Build material and configure blend states
        var material = Material.New(device, desc);
        material.Passes.Clear();

        // We configure passes manually per-entity, not through this feature
        cachedMaterial = material;
        return material;
    }
}
```

- [ ] **Step 2: Update RiverRenderingService to set up render states**

Add material setup to `RiverRenderingService.UpdateMeshes` — after creating each entity's ModelComponent, configure its material with alpha blend and depth bias:

```csharp
// After creating entity with ModelComponent:
var modelComponent = entity.Get<ModelComponent>();
if (modelComponent?.Model?.Meshes.Count > 0)
{
    // Create a simple material with alpha blending and depth bias
    var mat = Material.New(graphicsDevice, new MaterialDescriptor
    {
        Attributes =
        {
            Diffuse = new MaterialDiffuseMapFeature(new ComputeColor()),
            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
        }
    });

    // Configure render states for river transparency
    mat.Passes.Clear();
    var pass = new MaterialPass
    {
        IsTransparent = true,
        HasTransparency = true,
    };
    mat.Passes.Add(pass);

    modelComponent.Model.Meshes[0].Material = mat;
}
```

- [ ] **Step 3: Commit**

```bash
git add Terrain.Editor/Rendering/Materials/MaterialRiverFeature.cs Terrain.Editor/Services/RiverRenderingService.cs
git commit -m "feat: add river material with alpha blend and depth bias"
```

---

### Task 10: EditorMode.River + TerrainManager Integration

**Files:**
- Modify: `Terrain.Editor/Models/EditorMode.cs`
- Modify: `Terrain.Editor/Services/TerrainManager.cs`

- [ ] **Step 1: Add River to EditorMode enum**

```csharp
public enum EditorMode
{
    Sculpt,
    Paint,
    Path,
    Foliage,
    Settings,
    Landscape,
    River,
}
```

- [ ] **Step 2: Add RiverMap property to TerrainManager**

```csharp
// Add to TerrainManager.cs fields
private RiverCell[,]? riverMap;
private string? currentRiverMapPath;

// Add to TerrainManager.cs properties
public RiverCell[,]? RiverMap => riverMap;
public string? CurrentRiverMapPath => currentRiverMapPath;

// Add events
public event EventHandler? RiverMapChanged;

// Add methods
public bool LoadRiverMap(string path)
{
    var service = new RiverMapService();
    if (!service.Load(path))
    {
        Log.Error($"River map load failed: {string.Join("; ", service.Errors)}");
        return false;
    }

    riverMap = service.Cells;
    currentRiverMapPath = path;
    RiverMapChanged?.Invoke(this, EventArgs.Empty);
    ProjectManager.Instance.MarkDirty();
    return true;
}

public void ClearRiverMap()
{
    riverMap = null;
    currentRiverMapPath = null;
    currentRiverMapPath = null;
    RiverMapChanged?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 3: Commit**

```bash
git add Terrain.Editor/Models/EditorMode.cs Terrain.Editor/Services/TerrainManager.cs
git commit -m "feat: add EditorMode.River and RiverMap integration in TerrainManager"
```

---

### Task 11: TOML Persistence

**Files:**
- Modify: `Terrain.Editor/Services/TomlProjectConfig.cs`

- [ ] **Step 1: Add RiverMapImagePath field**

Add to `TomlProjectConfig` class:

```csharp
public string? RiverMapImagePath { get; set; }
```

Add to `ReadFrom()` method, after reading `biome_mask`:

```csharp
config.RiverMapImagePath = terrain.HasKey("river_map") && terrain["river_map"].IsString
    ? ResolvePath(terrain["river_map"].AsString.Value, baseDir) : null;
```

Add to `WriteTo()` method, after writing `biome_mask`:

```csharp
if (!string.IsNullOrEmpty(RiverMapImagePath))
    terrain["river_map"] = MakeRelative(RiverMapImagePath, baseDir);
```

- [ ] **Step 2: Integrate in TerrainManager save/load**

In `TerrainManager.LoadProject()` method, after loading biome mask path:

```csharp
// Restore river map path and data
if (!string.IsNullOrEmpty(config.RiverMapImagePath) && File.Exists(config.RiverMapImagePath))
{
    LoadRiverMap(config.RiverMapImagePath);
}
```

In `TerrainManager.GetTomlProjectConfig()` method:

```csharp
config.RiverMapImagePath = currentRiverMapPath;
```

- [ ] **Step 3: Commit**

```bash
git add Terrain.Editor/Services/TomlProjectConfig.cs Terrain.Editor/Services/TerrainManager.cs
git commit -m "feat: persist river map image path in TOML project config"
```

---

### Task 12: River Mode UI — ViewModel

**Files:**
- Create: `Terrain.Editor/ViewModels/RiverViewModel.cs`
- Modify: `Terrain.Editor/ViewModels/EditorShellViewModel.cs`

- [ ] **Step 1: Create RiverViewModel.cs**

```csharp
#nullable enable

using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Terrain.Editor.Services;
using Stride.Core.Mathematics;

namespace Terrain.Editor.ViewModels;

public sealed partial class RiverViewModel : ViewModelBase
{
    private readonly TerrainManager terrainManager;
    private readonly RiverRenderingService? renderingService;
    private readonly RiverMeshService? meshService;

    [ObservableProperty]
    private string? _riverMapPath;

    [ObservableProperty]
    private string _statusText = "No river map loaded";

    [ObservableProperty]
    private bool _hasRiverMap;

    [ObservableProperty]
    private bool _showRivers = true;

    [ObservableProperty]
    private double _widthScale = 1.0;

    [ObservableProperty]
    private Bitmap? _previewImage;

    public RiverViewModel(TerrainManager terrainManager)
    {
        this.terrainManager = terrainManager;
        terrainManager.RiverMapChanged += OnRiverMapChanged;
    }

    partial void OnShowRiversChanged(bool value)
    {
        renderingService?.SetVisible(value);
    }

    private void OnRiverMapChanged(object? sender, EventArgs e)
    {
        HasRiverMap = terrainManager.RiverMap != null;
        RiverMapPath = terrainManager.CurrentRiverMapPath;
        StatusText = HasRiverMap
            ? $"River map loaded: {terrainManager.RiverMap!.GetLength(0)}x{terrainManager.RiverMap!.GetLength(1)}"
            : "No river map loaded";
    }

    public void ImportPng(Window parentWindow)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import River Map",
            AllowMultiple = false,
            Filters = { new FileDialogFilter { Name = "PNG Images", Extensions = { "png" } } },
        };

        var result = dialog.ShowAsync(parentWindow).GetAwaiter().GetResult();
        if (result == null || result.Length == 0) return;

        string path = result[0];
        terrainManager.LoadRiverMap(path);

        // Load preview
        try
        {
            PreviewImage = new Bitmap(path);
        }
        catch { }
    }

    public void Generate()
    {
        var cells = terrainManager.RiverMap;
        if (cells == null)
        {
            StatusText = "Error: No river map loaded";
            return;
        }

        var mapService = new RiverMapService();
        // Re-extract segments from already-loaded cells
        var segments = mapService.ExtractSegments();

        if (segments.Count == 0)
        {
            StatusText = "Error: No river segments found";
            return;
        }

        // Build centerlines
        meshService?.BuildCenterlines(segments,
            cells.GetLength(0), cells.GetLength(1),
            terrainManager.HeightCacheHeight);

        // Generate meshes
        renderingService?.UpdateMeshes(segments, meshService!, (float)WidthScale);

        int vertexCount = segments.Sum(s => s.Centerline?.Count ?? 0) * 2;
        StatusText = $"✓ {mapService.SystemCount} systems, {segments.Count} segments";
    }

    partial void OnWidthScaleChanged(double value)
    {
        Generate();
    }
}
```

- [ ] **Step 2: Add River Mode to EditorShellViewModel**

Add properties:

```csharp
// After IsSettingsMode
public bool IsRiverMode => SelectedMode == EditorMode.River;
```

Add to `CreateToolsForMode`:

```csharp
EditorMode.River =>
[
    new("River Tool", "Import and generate rivers", "", mode),
],
```

Add to `InitializeModes`:

```csharp
Modes.Add(new ModeOptionViewModel("River", "River system from color map", "", EditorMode.River));
```

- [ ] **Step 3: Commit**

```bash
git add Terrain.Editor/ViewModels/RiverViewModel.cs Terrain.Editor/ViewModels/EditorShellViewModel.cs
git commit -m "feat: add RiverViewModel and River Mode integration"
```

---

### Task 13: River Mode UI — Avalonia XAML

**Files:**
- Modify: `Terrain.Editor/Views/MainWindow.axaml`

- [ ] **Step 1: Add River Mode inspector panel**

Add after the Settings Mode inspector (after line ~577):

```xml
<!-- ═══ River Mode Inspector ═══ -->
<StackPanel IsVisible="{Binding IsRiverMode}" Spacing="0">
  <Border Classes="inspectorSection" Padding="10,10">
    <StackPanel Spacing="8">
      <TextBlock Classes="sectionLabel" Text="RIVER MAP" />

      <!-- Import button -->
      <Button Classes="commandButton" Command="{Binding River.ImportPngCommand}">
        <StackPanel Orientation="Horizontal" Spacing="6" HorizontalAlignment="Center">
          <TextBlock Classes="toolbarIcon" Text="&#xE8B7;" />
          <TextBlock Text="Import PNG" VerticalAlignment="Center" />
        </StackPanel>
      </Button>

      <!-- File info -->
      <TextBlock Classes="panelHint" Text="{Binding River.RiverMapPath}" TextWrapping="Wrap" />

      <!-- Preview -->
      <Border Classes="brushPreviewFrame" Height="120" IsVisible="{Binding River.HasRiverMap}">
        <Image Source="{Binding River.PreviewImage}" Stretch="Uniform" />
      </Border>

      <!-- Status -->
      <TextBlock Classes="panelHint" Text="{Binding River.StatusText}" TextWrapping="Wrap" />

      <!-- Width Scale -->
      <Grid ColumnDefinitions="*,48" ColumnSpacing="8">
        <StackPanel Spacing="2">
          <TextBlock Classes="fieldLabel" Text="Width Scale" />
          <Slider Minimum="0.5" Maximum="3.0" Value="{Binding River.WidthScale}" />
        </StackPanel>
        <TextBox Grid.Column="1" Classes="valueBox" IsReadOnly="True"
                 Text="{Binding River.WidthScale, StringFormat='{}{0:F2}'}" VerticalAlignment="Bottom" />
      </Grid>

      <!-- Generate button -->
      <Button Classes="commandButton" Command="{Binding River.GenerateCommand}"
              IsVisible="{Binding River.HasRiverMap}"
              HorizontalAlignment="Stretch">
        <TextBlock Text="GENERATE" HorizontalAlignment="Center" FontWeight="Bold" />
      </Button>

      <!-- Show/Hide -->
      <CheckBox Content="Show Rivers" IsChecked="{Binding River.ShowRivers}" />
    </StackPanel>
  </Border>
</StackPanel>
```

- [ ] **Step 2: Commit**

```bash
git add Terrain.Editor/Views/MainWindow.axaml
git commit -m "feat: add river mode inspector panel to MainWindow"
```

---

### Task 14: EmbeddedStrideViewportGame Integration

**Files:**
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

- [ ] **Step 1: Add River Mode to viewport game**

Find the `IsBrushMode` method and add River:

```csharp
return _editorState.CurrentEditorMode is EditorMode.Sculpt or EditorMode.Paint or EditorMode.River;
```

In the mode-switch for cursor/tool color:

```csharp
EditorMode.River => new Color4(0.0f, 0.4f, 0.8f, 0.5f),
```

- [ ] **Step 2: Commit**

```bash
git add Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs
git commit -m "feat: add River Mode support to embedded viewport"
```

---

### Task 15: Integration & Verification

**Files:**
- No new files—build + test

- [ ] **Step 1: Build the entire project**

```bash
cd "e:/Stride Projects/Terrain" && dotnet build Terrain.Editor/Terrain.Editor.csproj 2>&1 | tail -40
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Create a test river.png**

Create a simple 64x64 pixel river map with the 13 colors and a source-confluence chain, saved as `test_river.png`.

- [ ] **Step 3: Run the editor and test the flow**

1. Launch the editor
2. Open/create a terrain project
3. Switch to River Mode
4. Click Import PNG → select test_river.png
5. Verify preview shows
6. Click Generate
7. Verify river meshes appear in the viewport

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: complete river system from color map implementation"
```
