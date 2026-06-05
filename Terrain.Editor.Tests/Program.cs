using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Mathematics;
using Terrain.Editor.Models;
using Terrain.Editor.Services;

var tempDir = Path.Combine(Path.GetTempPath(), "terrain-river-tests");
Directory.CreateDirectory(tempDir);

Run("source-to-confluence segment includes semantic endpoints", SourceToConfluenceSegmentIncludesEndpoints);
Run("confluence creates source-to-confluence branch segments", ConfluenceCreatesSourceToConfluenceBranchSegments);
Run("semantic endpoints do not shrink average river width", SemanticEndpointsDoNotShrinkAverageRiverWidth);
Run("special endpoints require adjacent river pixels", SpecialEndpointsRequireAdjacentRiverPixels);
Run("centerline simplification removes pixel stair steps", CenterlineSimplificationRemovesPixelStairSteps);
Run("centerline smoothing cuts hard corners", CenterlineSmoothingCutsHardCorners);
Run("ribbon indices preserve ck3 strip organization with stride-visible winding", RibbonIndicesPreserveCk3StripOrganizationWithStrideVisibleWinding);
Run("mitered corner preserves river half width", MiteredCornerPreservesRiverHalfWidth);

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

void SourceToConfluenceSegmentIncludesEndpoints()
{
    var path = Path.Combine(tempDir, "source-confluence.png");
    using (var image = new Image<Rgba32>(5, 3))
    {
        image[0, 1] = new Rgba32(0, 255, 0);
        image[1, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[2, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[3, 1] = new Rgba32(255, 0, 0);
        image.Save(path);
    }

    var service = new RiverMapService();
    Assert(service.Load(path), "river map should load");
    var segments = service.ExtractSegments();

    AssertEqual(1, segments.Count, "segment count");
    var segment = segments[0];
    AssertEqual(SegmentEndKind.Source, segment.StartKind, "start kind");
    AssertEqual(SegmentEndKind.Confluence, segment.EndKind, "end kind");
    AssertEqual((0, 1), segment.Cells[0], "first cell should be source pixel center");
    AssertEqual((3, 1), segment.Cells[^1], "last cell should be confluence pixel center");
}

void ConfluenceCreatesSourceToConfluenceBranchSegments()
{
    var path = Path.Combine(tempDir, "t-confluence.png");
    using (var image = new Image<Rgba32>(5, 5))
    {
        image[0, 2] = new Rgba32(0, 255, 0);
        image[1, 2] = new Rgba32(0x00, 0xe1, 0xff);
        image[2, 2] = new Rgba32(255, 0, 0);
        image[2, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[2, 0] = new Rgba32(0, 255, 0);
        image[3, 2] = new Rgba32(0x00, 0xe1, 0xff);
        image[4, 2] = new Rgba32(0, 255, 0);
        image.Save(path);
    }

    var service = new RiverMapService();
    Assert(service.Load(path), "river map should load");
    var segments = service.ExtractSegments();

    AssertEqual(3, segments.Count, "T confluence should produce three independently tapered branch segments");
    Assert(segments.All(s => s.StartKind == SegmentEndKind.Source), "every branch should start at a source after direction normalization");
    Assert(segments.All(s => s.EndKind == SegmentEndKind.Confluence), "every branch should end at the confluence after direction normalization");
    Assert(segments.All(s => s.Cells[0] != (2, 2)), "no branch should start at the confluence after direction normalization");
    Assert(segments.All(s => s.Cells[^1] == (2, 2)), "every branch should include the confluence as its final endpoint");
}

void SemanticEndpointsDoNotShrinkAverageRiverWidth()
{
    var path = Path.Combine(tempDir, "wide-source-confluence.png");
    using (var image = new Image<Rgba32>(4, 3))
    {
        image[0, 1] = new Rgba32(0, 255, 0);
        image[1, 1] = new Rgba32(0x18, 0xce, 0x00);
        image[2, 1] = new Rgba32(255, 0, 0);
        image.Save(path);
    }

    var service = new RiverMapService();
    Assert(service.Load(path), "river map should load");
    var segments = service.ExtractSegments();

    AssertEqual(1, segments.Count, "segment count");
    AssertNearlyEqual(1.375f, segments[0].AvgHalfWidth, 0.0001f, "average width should only use river palette pixels");
}

void SpecialEndpointsRequireAdjacentRiverPixels()
{
    var path = Path.Combine(tempDir, "source-confluence-no-river.png");
    using (var image = new Image<Rgba32>(3, 3))
    {
        image[0, 1] = new Rgba32(0, 255, 0);
        image[1, 1] = new Rgba32(255, 0, 0);
        image.Save(path);
    }

    var service = new RiverMapService();
    Assert(service.Load(path), "river map should load even when validation reports errors");
    Assert(service.Errors.Any(error => error.Contains("has no River neighbor", StringComparison.Ordinal)), "special endpoints without River pixels should be validation errors");
    AssertEqual(0, service.ExtractSegments().Count, "invalid special-to-special adjacency should not silently produce a segment");
}

void CenterlineSimplificationRemovesPixelStairSteps()
{
    var points = new List<Vector3>
    {
        new(0, 0, 0),
        new(1, 0, 0),
        new(1, 0, 1),
        new(2, 0, 1),
        new(2, 0, 2),
        new(3, 0, 2),
        new(3, 0, 3),
        new(4, 0, 3),
        new(4, 0, 4),
    };

    var simplified = RiverMeshService.SimplifyCenterline(points, 1.5f);

    Assert(simplified.Count < points.Count, "simplification should remove pixel-level stair-step control points");
    AssertEqual(points[0], simplified[0], "simplification should preserve the start point");
    AssertEqual(points[^1], simplified[^1], "simplification should preserve the end point");
}

void CenterlineSmoothingCutsHardCorners()
{
    var points = new List<Vector3>
    {
        new(0, 0, 0),
        new(1, 0, 0),
        new(1, 0, 1),
    };

    var smoothed = RiverMeshService.SmoothCenterline(points, 1);

    AssertEqual(points[0], smoothed[0], "smoothing should preserve the start point");
    AssertEqual(points[^1], smoothed[^1], "smoothing should preserve the end point");
    Assert(!smoothed.Contains(points[1]), "smoothing should cut away the original hard corner control point");
    Assert(smoothed.Any(point => Math.Abs(point.X - 1.0f) < 0.0001f && Math.Abs(point.Z - 0.25f) < 0.0001f), "smoothing should create a point after the corner");
}

void RibbonIndicesPreserveCk3StripOrganizationWithStrideVisibleWinding()
{
    var segment = new RiverSegment
    {
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(2, 0, 0),
        ],
        WorldLength = 2,
        AvgHalfWidth = 0.5f,
    };

    var (vertices, indices) = BuildRibbonMeshForTest(segment);

    AssertEqual(6, vertices.Length, "three centerline samples should produce interleaved left/right boundary vertices");
    AssertEqual(12, indices.Length, "three centerline samples should produce four triangle-list faces");
    AssertSequenceEqual([0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5], indices, "indices should preserve CK3 left/right strip organization with Stride-visible triangle winding");
}

void MiteredCornerPreservesRiverHalfWidth()
{
    var segment = new RiverSegment
    {
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(1, 0, 1),
        ],
        WorldLength = 2,
        AvgHalfWidth = 0.5f,
    };

    var (vertices, _) = BuildRibbonMeshForTest(segment);
    Vector3 leftCorner = vertices[2].Position;
    Vector3 rightCorner = vertices[3].Position;

    AssertNearlyEqual(0.5f, MathF.Abs(leftCorner.Z), 0.0001f, "left miter should preserve width against incoming segment");
    AssertNearlyEqual(0.5f, MathF.Abs(rightCorner.Z), 0.0001f, "right miter should preserve width against incoming segment");
    AssertNearlyEqual(0.5f, MathF.Abs(leftCorner.X - 1), 0.0001f, "left miter should preserve width against outgoing segment");
    AssertNearlyEqual(0.5f, MathF.Abs(rightCorner.X - 1), 0.0001f, "right miter should preserve width against outgoing segment");
}

static (Stride.Graphics.VertexPositionNormalTexture[] Vertices, int[] Indices) BuildRibbonMeshForTest(RiverSegment segment)
{
    var meshService = new RiverMeshService(null!);
    return meshService.BuildRibbonMesh(segment, 1.0f);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void AssertNearlyEqual(float expected, float actual, float tolerance, string message)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void AssertSequenceEqual(IReadOnlyList<int> expected, IReadOnlyList<int> actual, string message)
{
    if (expected.Count != actual.Count)
        throw new InvalidOperationException($"{message}: expected {expected.Count} items, actual {actual.Count}");

    for (int i = 0; i < expected.Count; i++)
    {
        if (expected[i] != actual[i])
            throw new InvalidOperationException($"{message}: item {i} expected {expected[i]}, actual {actual[i]}");
    }
}
