using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Mathematics;
using Terrain.Editor.Models;
using Terrain.Editor.Services;
using Terrain.Editor.Tests;
using Terrain.Editor.Tests.VirtualResources;

var tempDir = Path.Combine(Path.GetTempPath(), "terrain-river-tests");
Directory.CreateDirectory(tempDir);

Run("source-to-confluence segment includes semantic endpoints", SourceToConfluenceSegmentIncludesEndpoints);
Run("confluence creates source-to-confluence branch segments", ConfluenceCreatesSourceToConfluenceBranchSegments);
Run("semantic endpoints do not shrink average river width", SemanticEndpointsDoNotShrinkAverageRiverWidth);
Run("special endpoints require adjacent river pixels", SpecialEndpointsRequireAdjacentRiverPixels);
Run("centerline simplification removes pixel stair steps", CenterlineSimplificationRemovesPixelStairSteps);
Run("centerline smoothing cuts hard corners", CenterlineSmoothingCutsHardCorners);
Run("ribbon indices preserve reference strip organization with stride-visible winding", RibbonIndicesPreserveReferenceStripOrganizationWithStrideVisibleWinding);
Run("mitered corner preserves river half width", MiteredCornerPreservesRiverHalfWidth);
Run("tapered river endpoints keep visible cap width", TaperedRiverEndpointsKeepVisibleCapWidth);
Run("river vertex layout exposes reference semantics", RiverVertexLayoutExposesReferenceSemantics);
Run("river vertex position uses homogeneous coordinates", RiverVertexPositionUsesHomogeneousCoordinates);
Run("river vertex mesh exposes reference attributes", RiverVertexMeshExposesReferenceAttributes);
Run("river render resources compute half resolution", RiverRenderResourcesComputeHalfResolution);
Run("river component uses river processor", RiverComponentUsesRiverProcessor);
Run("river component versioning", TestRiverComponentVersioning);
Run("river component exposes mesh snapshots", RiverComponentExposesMeshSnapshots);
Run("river rendering service updates component visibility", RiverRenderingServiceUpdatesComponentVisibility);
Run("river rendering service updates and clears component meshes", RiverRenderingServiceUpdatesAndClearsComponentMeshes);
RiverViewModelAutoGenerationTests.RunAll();
RiverWorkspaceDiagnosticsTests.RunAll();
LaunchSettingsResolverTests.RunAll();
GameResourceRootLocatorTests.RunAll();
DescriptorReaderTests.RunAll();
GameRuntimeResourceBootstrapTests.RunAll();
TerrainRuntimeLoadBehaviorTests.RunAll();
RuntimeMigrationTextTests.RunAll();
RuntimeBiomeMaskReaderTests.RunAll();
EditorWorkflowTextTests.RunAll();
ExportWorkflowTests.RunAll();
EditorResourceWriterTests.RunAll();
AtomicResourceWriteTransactionTests.RunAll();
EditorResourceSaveServiceTests.RunAll();
EditorAuthoringResourceMapperTests.RunAll();
EditorMaterialLoadStateTests.RunAll();
EditorMaterialRecoveryTests.RunAll();
MaterialSlotManagerFallbackTextTests.RunAll();
EditorMissingMaterialWorkflowTests.RunAll();
GameResourceGitIgnoreTextTests.RunAll();
LocalLaunchSettingsBootstrapTests.RunAll();
EditorMapDataScaffoldTests.RunAll();
EditorPendingResourceWorkflowTests.RunAll();

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

void RibbonIndicesPreserveReferenceStripOrganizationWithStrideVisibleWinding()
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
    AssertSequenceEqual([0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5], indices, "indices should preserve reference left/right strip organization with Stride-visible triangle winding");
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

void TaperedRiverEndpointsKeepVisibleCapWidth()
{
    var segment = new RiverSegment
    {
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
        ],
        WorldLength = 1,
        AvgHalfWidth = 0.5f,
        TaperStart = true,
        TaperEnd = true,
    };

    var (vertices, _) = BuildRibbonMeshForTest(segment);
    float startWidth = Vector3.Distance(vertices[0].Position, vertices[1].Position);
    float endWidth = Vector3.Distance(vertices[^2].Position, vertices[^1].Position);

    Assert(startWidth > 0.1f, "tapered source cap should keep visible width");
    Assert(endWidth > 0.1f, "tapered end cap should keep visible width");
}

void RiverVertexLayoutExposesReferenceSemantics()
{
    var elements = Terrain.Editor.Rendering.River.RiverVertex.Layout.VertexElements;

    AssertEqual(7, elements.Length, "RiverVertex layout element count");
    AssertEqual("POSITION", elements[0].SemanticAsText, "RiverVertex position semantic");
    AssertEqual("TEXCOORD", elements[1].SemanticAsText, "RiverVertex transparency semantic");
    AssertEqual("TEXCOORD1", elements[2].SemanticAsText, "RiverVertex UV semantic");
    AssertEqual("TEXCOORD2", elements[3].SemanticAsText, "RiverVertex tangent semantic");
    AssertEqual("TEXCOORD3", elements[4].SemanticAsText, "RiverVertex normal semantic");
    AssertEqual("TEXCOORD4", elements[5].SemanticAsText, "RiverVertex width semantic");
    AssertEqual("TEXCOORD5", elements[6].SemanticAsText, "RiverVertex distance-to-main semantic");
}

void RiverVertexPositionUsesHomogeneousCoordinates()
{
    var vertex = new Terrain.Editor.Rendering.River.RiverVertex(new Vector3(1, 2, 3), 1.0f, Vector2.Zero, Vector3.UnitX, Vector3.UnitY, 0.5f, 1.0f);

    AssertEqual(new Vector4(1, 2, 3, 1), vertex.Position, "RiverVertex stores POSITION as float4 with w=1");
    AssertEqual(System.Runtime.InteropServices.Marshal.SizeOf<Terrain.Editor.Rendering.River.RiverVertex>(), Terrain.Editor.Rendering.River.RiverVertex.Layout.VertexStride, "RiverVertex layout stride matches struct size");
}

void RiverVertexMeshExposesReferenceAttributes()
{
    var segment = new RiverSegment
    {
        SystemId = 9,
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(2, 0, 0),
        ],
        WorldLength = 2,
        AvgHalfWidth = 0.5f,
        TaperStart = true,
    };

    var mesh = new RiverMeshService(null!).BuildRiverMesh(segment, 1.0f);

    AssertEqual(9, mesh.SegmentId, "BuildRiverMesh preserves segment id");
    AssertEqual(6, mesh.Vertices.Length, "BuildRiverMesh emits interleaved left/right river vertices");
    AssertEqual(12, mesh.Indices.Length, "BuildRiverMesh preserves triangle-list topology");
    AssertSequenceEqual([0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5], mesh.Indices, "BuildRiverMesh preserves river index organization");

    var first = mesh.Vertices[0];
    AssertEqual(Vector2.Zero, first.UV, "first river vertex stores UV in TEXCOORD1-compatible field");
    AssertNearlyEqual(1.0f, first.Transparency, 0.0001f, "river vertex stores transparency");
    AssertNearlyEqual(1.0f, first.Tangent.Length(), 0.0001f, "river vertex tangent is normalized");
    AssertNearlyEqual(1.0f, first.Normal.Length(), 0.0001f, "river vertex normal is normalized");
    Assert(first.Width > 0.0f, "river vertex stores normalized width");
    Assert(first.Width < mesh.Vertices[^1].Width, "river vertex width follows tapered geometry width");
    AssertNearlyEqual(0.0f, first.DistanceToMain, 0.0001f, "tapered source starts with zero distance-to-main fade");
    AssertNearlyEqual(1.0f, mesh.Vertices[^1].DistanceToMain, 0.0001f, "non-tapered downstream end keeps full distance-to-main fade");
    Assert(mesh.BoundingSphere.Radius > 0.0f, "BuildRiverMesh computes bounds");
}

void RiverRenderResourcesComputeHalfResolution()
{
    AssertEqual((960, 540), Terrain.Editor.Rendering.River.RiverRenderResources.ComputeHalfResolutionSize(1920, 1080), "RiverRenderResources halves even viewport dimensions");
    AssertEqual((961, 541), Terrain.Editor.Rendering.River.RiverRenderResources.ComputeHalfResolutionSize(1921, 1081), "RiverRenderResources rounds odd viewport dimensions up");
    AssertEqual((1, 1), Terrain.Editor.Rendering.River.RiverRenderResources.ComputeHalfResolutionSize(0, 0), "RiverRenderResources keeps minimum render target size");
    AssertEqual(Stride.Graphics.PixelFormat.R16G16B16A16_Float, Terrain.Editor.Rendering.River.RiverRenderResources.RefractionFormat, "RiverRenderResources uses half-float refraction format");
}

void RiverComponentUsesRiverProcessor()
{
    var attributes = typeof(Terrain.Editor.Rendering.River.RiverComponent).GetCustomAttributes(false);
    var rendererAttribute = attributes.OfType<Stride.Engine.Design.DefaultEntityComponentRendererAttribute>().FirstOrDefault();

    Assert(rendererAttribute != null, "RiverComponent should declare a default processor");
    AssertEqual(typeof(Terrain.Editor.Rendering.River.RiverProcessor).AssemblyQualifiedName, rendererAttribute!.TypeName, "RiverComponent should use RiverProcessor");
}

void TestRiverComponentVersioning()
{
    var component = new Terrain.Editor.Rendering.River.RiverComponent();
    int initial = component.Version;
    var vertices = new[]
    {
        new Terrain.Editor.Rendering.River.RiverVertex(new Vector3(1, 2, 3), 1.0f, Vector2.Zero, Vector3.UnitX, Vector3.UnitY, 0.5f, 1.0f)
    };
    var indices = new[] { 0 };
    var meshes = new List<Terrain.Editor.Rendering.River.RiverMeshData>
    {
        new()
        {
            SegmentId = 1,
            Vertices = vertices,
            Indices = indices,
            BoundingBox = BoundingBox.Empty,
            BoundingSphere = BoundingSphere.Empty,
        }
    };

    component.SetMeshes(meshes);

    Assert(component.Version == initial + 1, "RiverComponent.SetMeshes increments Version");
    Assert(component.Meshes.Count == 1, "RiverComponent stores meshes");

    meshes.Clear();
    vertices[0] = new Terrain.Editor.Rendering.River.RiverVertex(new Vector3(9, 9, 9), 0.0f, Vector2.One, Vector3.UnitZ, Vector3.UnitY, 0.0f, 0.0f);
    indices[0] = 99;

    Assert(component.Meshes.Count == 1, "RiverComponent snapshots the mesh list");
    AssertEqual(new Vector4(1, 2, 3, 1), component.Meshes[0].Vertices[0].Position, "RiverComponent snapshots vertex arrays");
    AssertEqual(0, component.Meshes[0].Indices[0], "RiverComponent snapshots index arrays");

    component.Clear();

    Assert(component.Version == initial + 2, "RiverComponent.Clear increments Version");
    Assert(component.Meshes.Count == 0, "RiverComponent.Clear removes meshes");
}

void RiverComponentExposesMeshSnapshots()
{
    var component = new Terrain.Editor.Rendering.River.RiverComponent();
    component.SetMeshes(
    [
        new Terrain.Editor.Rendering.River.RiverMeshData
        {
            SegmentId = 7,
            Vertices =
            [
                new Terrain.Editor.Rendering.River.RiverVertex(new Vector3(1, 2, 3), 1.0f, Vector2.Zero, Vector3.UnitX, Vector3.UnitY, 0.5f, 1.0f)
            ],
            Indices = [0],
        }
    ]);

    var publishedMeshes = component.Meshes;
    publishedMeshes[0].Vertices[0] = new Terrain.Editor.Rendering.River.RiverVertex(new Vector3(9, 9, 9), 0.0f, Vector2.One, Vector3.UnitZ, Vector3.UnitY, 0.0f, 0.0f);
    publishedMeshes[0].Indices[0] = 99;

    AssertEqual(new Vector4(1, 2, 3, 1), component.Meshes[0].Vertices[0].Position, "RiverComponent.Meshes returns vertex snapshots");
    AssertEqual(0, component.Meshes[0].Indices[0], "RiverComponent.Meshes returns index snapshots");
}

void RiverRenderingServiceUpdatesComponentVisibility()
{
    var component = new Terrain.Editor.Rendering.River.RiverComponent();
    var service = new RiverRenderingService(component);

    service.SetVisible(false);

    Assert(!component.Enabled, "RiverRenderingService.SetVisible(false) disables RiverComponent");
    Assert(!component.Settings.Visible, "RiverRenderingService.SetVisible(false) updates RiverRenderSettings.Visible");

    service.SetVisible(true);

    Assert(component.Enabled, "RiverRenderingService.SetVisible(true) enables RiverComponent");
    Assert(component.Settings.Visible, "RiverRenderingService.SetVisible(true) updates RiverRenderSettings.Visible");
}

void RiverRenderingServiceUpdatesAndClearsComponentMeshes()
{
    var component = new Terrain.Editor.Rendering.River.RiverComponent();
    var service = new RiverRenderingService(component);
    var segment = new RiverSegment
    {
        SystemId = 42,
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
        ],
        WorldLength = 1,
        AvgHalfWidth = 0.5f,
    };

    service.UpdateMeshes([segment], new RiverMeshService(null!), 1.0f);

    AssertEqual(1, component.Meshes.Count, "RiverRenderingService.UpdateMeshes stores component meshes");
    AssertEqual(42, component.Meshes[0].SegmentId, "RiverRenderingService.UpdateMeshes preserves segment id");
    Assert(component.Meshes[0].Vertices.Length > 0, "RiverRenderingService.UpdateMeshes stores river vertices");
    Assert(component.Meshes[0].Indices.Length > 0, "RiverRenderingService.UpdateMeshes stores indices");
    Assert(component.Enabled, "RiverRenderingService.UpdateMeshes applies current visibility to component");

    service.ClearMeshes();

    AssertEqual(0, component.Meshes.Count, "RiverRenderingService.ClearMeshes clears component meshes");

    service.UpdateMeshes([segment], new RiverMeshService(null!), 1.0f);
    service.Dispose();

    AssertEqual(0, component.Meshes.Count, "RiverRenderingService.Dispose clears component meshes");
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
