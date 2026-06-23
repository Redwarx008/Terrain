using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Mathematics;
using Terrain.Rivers;
using Terrain.Editor.Services;
using Terrain.Editor.Tests;
using Terrain.Editor.Tests.VirtualResources;

var tempDir = Path.Combine(Path.GetTempPath(), "terrain-river-tests");
Directory.CreateDirectory(tempDir);

Run("source-to-confluence segment includes semantic endpoints", SourceToConfluenceSegmentIncludesEndpoints);
Run("confluence creates source-to-confluence branch segments", ConfluenceCreatesSourceToConfluenceBranchSegments);
Run("branch honors adjacent confluence marker before side continuation", BranchHonorsAdjacentConfluenceMarkerBeforeSideContinuation);
Run("confluence to none segment is reversed to flow into confluence", ConfluenceToNoneSegmentIsReversedToFlowIntoConfluence);
Run("bifurcation to none segment is reversed to flow into bifurcation", BifurcationToNoneSegmentIsReversedToFlowIntoBifurcation);
Run("semantic endpoints do not shrink average river width", SemanticEndpointsDoNotShrinkAverageRiverWidth);
Run("river palette maps to configured local width samples", RiverPaletteMapsToConfiguredLocalWidthSamples);
Run("river palette width mapping rejects invalid configured width range", RiverPaletteWidthMappingRejectsInvalidConfiguredWidthRange);
Run("river map service validates configured width range", RiverMapServiceValidatesConfiguredWidthRange);
Run("special endpoints require adjacent river pixels", SpecialEndpointsRequireAdjacentRiverPixels);
Run("centerline simplification removes pixel stair steps", CenterlineSimplificationRemovesPixelStairSteps);
Run("centerline smoothing cuts hard corners", CenterlineSmoothingCutsHardCorners);
Run("centerline smoothing limits repeated river bend angles", CenterlineSmoothingLimitsRepeatedRiverBendAngles);
Run("centerline smoothing stays near original river corridor", CenterlineSmoothingStaysNearOriginalRiverCorridor);
Run("river centerline generation preserves palette width gradient", RiverCenterlineGenerationPreservesPaletteWidthGradient);
Run("river centerline width interpolation stays within palette range", RiverCenterlineWidthInterpolationStaysWithinPaletteRange);
Run("ribbon indices preserve boundary strip organization with stride-visible winding", RibbonIndicesPreserveBoundaryStripOrganizationWithStrideVisibleWinding);
Run("mitered corner preserves river half width", MiteredCornerPreservesRiverHalfWidth);
Run("river mesh preserves local width gradient", RiverMeshPreservesLocalWidthGradient);
Run("river mesh boundaries stay smooth across repeated bends", RiverMeshBoundariesStaySmoothAcrossRepeatedBends);
Run("tapered river endpoints keep visible cap width", TaperedRiverEndpointsKeepVisibleCapWidth);
Run("river vertex layout exposes target semantics", RiverVertexLayoutExposesTargetSemantics);
Run("river vertex position uses homogeneous coordinates", RiverVertexPositionUsesHomogeneousCoordinates);
Run("river vertex mesh exposes target attributes", RiverVertexMeshExposesTargetAttributes);
Run("river vertex longitudinal uv uses river define scale", RiverVertexLongitudinalUvUsesRiverDefineScale);
Run("river vertex mesh preserves sloped ribbon basis", RiverVertexMeshPreservesSlopedRibbonBasis);
Run("river render resources compute half resolution", RiverRenderResourcesComputeHalfResolution);
Run("river component uses river processor", RiverComponentUsesRiverProcessor);
Run("river component versioning", TestRiverComponentVersioning);
Run("river component records runtime load failure config", RiverComponentRecordsRuntimeLoadFailureConfig);
Run("river component exposes mesh snapshots", RiverComponentExposesMeshSnapshots);
Run("river rendering service updates component visibility", RiverRenderingServiceUpdatesComponentVisibility);
Run("river rendering service updates and clears component meshes", RiverRenderingServiceUpdatesAndClearsComponentMeshes);
EditorTerrainShadowCasterTests.RunAll();
RiverViewModelAutoGenerationTests.RunAll();
RiverWorkspaceDiagnosticsTests.RunAll();
RiverRenderFeatureRuntimeTests.RunAll();
RuntimeRiverAssetTests.RunAll();
RiverShaderTextTests.RunAll();
RiverShaderCompileTests.RunAll();
LaunchSettingsResolverTests.RunAll();
GameResourceRootLocatorTests.RunAll();
DescriptorReaderTests.RunAll();
GameRuntimeResourceBootstrapTests.RunAll();
TerrainRuntimeLoadBehaviorTests.RunAll();
RuntimeMigrationTextTests.RunAll();
EditorWorkflowTextTests.RunAll();
ExportWorkflowTests.RunAll();
BakedDetailTerrainFormatTests.RunAll();
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

void BranchHonorsAdjacentConfluenceMarkerBeforeSideContinuation()
{
    var path = Path.Combine(tempDir, "adjacent-confluence-priority.png");
    using (var image = new Image<Rgba32>(5, 4))
    {
        image[0, 1] = new Rgba32(0, 255, 0);
        image[1, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[2, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[3, 1] = new Rgba32(255, 0, 0);
        image[2, 2] = new Rgba32(0x00, 0xe1, 0xff);
        image.Save(path);
    }

    var service = new RiverMapService();
    Assert(service.Load(path), "river map should load");
    var segments = service.ExtractSegments();

    AssertEqual(1, segments.Count, "segment count");
    var segment = segments[0];
    AssertEqual(SegmentEndKind.Source, segment.StartKind, "start kind");
    AssertEqual(SegmentEndKind.Confluence, segment.EndKind, "end kind");
    AssertEqual((0, 1), segment.Cells[0], "segment should still start at source marker");
    AssertEqual((3, 1), segment.Cells[^1], "segment should prioritize the adjacent confluence marker");
}

void ConfluenceToNoneSegmentIsReversedToFlowIntoConfluence()
{
    var path = Path.Combine(tempDir, "confluence-none-direction.png");
    using (var image = new Image<Rgba32>(4, 3))
    {
        image[0, 1] = new Rgba32(255, 0, 0);
        image[1, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[2, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image.Save(path);
    }

    var service = new RiverMapService();
    Assert(service.Load(path), "river map should load");
    var segments = service.ExtractSegments();

    AssertEqual(1, segments.Count, "segment count");
    var segment = segments[0];
    AssertEqual(SegmentEndKind.None, segment.StartKind, "start kind");
    AssertEqual(SegmentEndKind.Confluence, segment.EndKind, "end kind");
    AssertEqual((2, 1), segment.Cells[0], "segment should start from the open river end after normalization");
    AssertEqual((0, 1), segment.Cells[^1], "segment should end at the confluence marker");
}

void BifurcationToNoneSegmentIsReversedToFlowIntoBifurcation()
{
    var path = Path.Combine(tempDir, "bifurcation-none-direction.png");
    using (var image = new Image<Rgba32>(4, 3))
    {
        image[0, 1] = new Rgba32(255, 252, 0);
        image[1, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[2, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image.Save(path);
    }

    var service = new RiverMapService();
    Assert(service.Load(path), "river map should load");
    var segments = service.ExtractSegments();

    AssertEqual(1, segments.Count, "segment count");
    var segment = segments[0];
    AssertEqual(SegmentEndKind.None, segment.StartKind, "start kind");
    AssertEqual(SegmentEndKind.Bifurcation, segment.EndKind, "end kind");
    AssertEqual((2, 1), segment.Cells[0], "segment should start from the open river end after normalization");
    AssertEqual((0, 1), segment.Cells[^1], "segment should end at the bifurcation marker");
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
    AssertNearlyEqual(2.0f, segments[0].AvgHalfWidth, 0.0001f, "average width should only use river palette pixels");
}

void RiverPaletteMapsToConfiguredLocalWidthSamples()
{
    var path = Path.Combine(tempDir, "width-gradient.png");
    using (var image = new Image<Rgba32>(5, 3))
    {
        image[0, 1] = new Rgba32(0, 255, 0);
        image[1, 1] = new Rgba32(0x00, 0xe1, 0xff);
        image[2, 1] = new Rgba32(0x00, 0x00, 0xff);
        image[3, 1] = new Rgba32(0x18, 0xce, 0x00);
        image[4, 1] = new Rgba32(255, 0, 0);
        image.Save(path);
    }

    var service = new RiverMapService(riverMinWidth: 1.0f, riverMaxWidth: 4.0f);
    Assert(service.Load(path), "river map should load");
    var segments = service.ExtractSegments();

    AssertEqual(1, segments.Count, "segment count");
    var samples = segments[0].CellHalfWidths;
    AssertEqual(5, samples.Count, "width samples include semantic endpoint cells");
    AssertNearlyEqual(0.5f, samples[0], 0.0001f, "source endpoint should inherit first river width");
    AssertNearlyEqual(0.5f, samples[1], 0.0001f, "light blue should map to min half-width");
    AssertNearlyEqual(1.1f, samples[2], 0.0001f, "middle blue should interpolate configured half-width");
    AssertNearlyEqual(2.0f, samples[3], 0.0001f, "green should map to max half-width");
    AssertNearlyEqual(2.0f, samples[4], 0.0001f, "confluence endpoint should inherit previous river width");
}

void RiverPaletteWidthMappingRejectsInvalidConfiguredWidthRange()
{
    AssertThrows<ArgumentOutOfRangeException>(
        () => RiverCell.GetHalfWidth(0, 0.0f, 4.0f),
        "palette width mapping should reject non-positive min full width");
    AssertThrows<ArgumentOutOfRangeException>(
        () => RiverCell.GetHalfWidth(0, 5.0f, 4.0f),
        "palette width mapping should reject max full width below min full width");
}

void RiverMapServiceValidatesConfiguredWidthRange()
{
    AssertThrows<ArgumentOutOfRangeException>(
        () => new RiverMapService(0.0f, 4.0f),
        "river min width must be positive");
    AssertThrows<ArgumentOutOfRangeException>(
        () => new RiverMapService(5.0f, 4.0f),
        "river max width must be greater than or equal to min width");
    AssertThrows<ArgumentOutOfRangeException>(
        () => new RiverMapService(float.NaN, 4.0f),
        "river min width must be finite");
    AssertThrows<ArgumentOutOfRangeException>(
        () => new RiverMapService(1.0f, float.PositiveInfinity),
        "river max width must be finite");
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

void CenterlineSmoothingLimitsRepeatedRiverBendAngles()
{
    var points = new List<Vector3>
    {
        new(0, 0, 0),
        new(3, 0, 0),
        new(4, 0, 2),
        new(7, 0, 0),
        new(8, 0, 2),
        new(11, 0, 0),
    };

    var smoothed = RiverMeshService.SmoothCenterline(points, 2);
    float maxAngle = MaxHorizontalTurnAngle(smoothed);

    Assert(maxAngle <= 15.0f, $"smoothed repeated river bends should stay CK3-style gradual, actual max angle {maxAngle:0.00}");
}

void CenterlineSmoothingStaysNearOriginalRiverCorridor()
{
    var points = new List<Vector3>
    {
        new(0, 0, 0),
        new(3, 0, 0),
        new(4, 0, 2),
        new(7, 0, 0),
        new(8, 0, 2),
        new(11, 0, 0),
    };

    var smoothed = RiverMeshService.SmoothCenterline(points, 2);
    float maxOffset = MaxHorizontalDistanceToPolyline(smoothed, points);

    Assert(maxOffset <= 0.625f, $"smoothed centerline should stay inside the default river half-width corridor, actual max offset {maxOffset:0.00}");
}

void RiverCenterlineGenerationPreservesPaletteWidthGradient()
{
    var segment = new RiverSegment
    {
        Cells = [(0, 1), (1, 1), (2, 1), (3, 1)],
        CellHalfWidths = [0.5f, 0.75f, 1.25f, 2.0f],
    };

    var service = new RiverMeshService(null!);
    service.BuildCenterlines([segment], mapWidth: 4, mapHeight: 3);

    Assert(segment.Centerline.Count > 2, "centerline generation should produce samples");
    AssertEqual(segment.Centerline.Count, segment.CenterlineHalfWidths.Count, "centerline widths should align with centerline samples");
    Assert(segment.CenterlineHalfWidths[0] < segment.CenterlineHalfWidths[^1], "width gradient should remain increasing after resampling");
}

void RiverCenterlineWidthInterpolationStaysWithinPaletteRange()
{
    var controlPoints = new List<Vector3>
    {
        new(0, 0, 0),
        new(10, 0, 0),
        new(20, 0, 0),
        new(30, 0, 0),
    };
    var controlWidths = new List<float> { 0.5f, 2.0f, 2.0f, 0.5f };

    var method = typeof(RiverMeshService).GetMethod(
        "CatmullRomInterpolateWithWidths",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    Assert(method != null, "width interpolation helper should exist");
    var result = ((List<Vector3> Points, List<float> Widths))method!.Invoke(null, [controlPoints, controlWidths])!;

    float maxWidth = result.Widths.Max();
    Assert(maxWidth <= 2.0001f, $"interpolated centerline widths should not overshoot palette maximum, actual {maxWidth:0.0000}");
}

void RibbonIndicesPreserveBoundaryStripOrganizationWithStrideVisibleWinding()
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
    AssertSequenceEqual([0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5], indices, "indices should preserve boundary left/right strip organization with Stride-visible triangle winding");
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

void RiverMeshPreservesLocalWidthGradient()
{
    var segment = new RiverSegment
    {
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(2, 0, 0),
            new Vector3(4, 0, 0),
        ],
        CenterlineHalfWidths = [0.5f, 1.0f, 2.0f],
        WorldLength = 2,
        AvgHalfWidth = 1.0f,
    };

    var mesh = new RiverMeshService(null!).BuildRiverMesh(segment, 1.0f);

    float mapExtent = mesh.MapExtent;
    float firstHalfWidth = mesh.Vertices[0].Width * mapExtent;
    float middleHalfWidth = mesh.Vertices[2].Width * mapExtent;
    float lastHalfWidth = mesh.Vertices[^1].Width * mapExtent;

    AssertNearlyEqual(0.5f, firstHalfWidth, 0.0001f, "first mesh sample should use local narrow half-width");
    AssertNearlyEqual(1.0f, middleHalfWidth, 0.0001f, "middle mesh sample should use local intermediate half-width");
    AssertNearlyEqual(2.0f, lastHalfWidth, 0.0001f, "last mesh sample should use local wide half-width");
}

void RiverMeshBoundariesStaySmoothAcrossRepeatedBends()
{
    var smoothed = RiverMeshService.SmoothCenterline(
    [
        new Vector3(0, 0, 0),
        new Vector3(3, 0, 0),
        new Vector3(4, 0, 2),
        new Vector3(7, 0, 0),
        new Vector3(8, 0, 2),
        new Vector3(11, 0, 0),
    ], 2);
    var segment = new RiverSegment
    {
        Centerline = smoothed,
        WorldLength = 11,
        AvgHalfWidth = 0.5f,
    };

    var mesh = new RiverMeshService(null!).BuildRiverMesh(segment, 1.0f);
    float maxBoundaryAngle = MaxRiverBoundaryTurnAngle(mesh.Vertices);

    Assert(maxBoundaryAngle <= 15.0f, $"river mesh boundaries should not reintroduce hard corners, actual max angle {maxBoundaryAngle:0.00}");
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

void RiverVertexLayoutExposesTargetSemantics()
{
    var elements = Terrain.Rendering.River.RiverVertex.Layout.VertexElements;

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
    var vertex = new Terrain.Rendering.River.RiverVertex(new Vector3(1, 2, 3), 1.0f, Vector2.Zero, Vector3.UnitX, Vector3.UnitY, 0.5f, 1.0f);

    AssertEqual(new Vector4(1, 2, 3, 1), vertex.Position, "RiverVertex stores POSITION as float4 with w=1");
    AssertEqual(System.Runtime.InteropServices.Marshal.SizeOf<Terrain.Rendering.River.RiverVertex>(), Terrain.Rendering.River.RiverVertex.Layout.VertexStride, "RiverVertex layout stride matches struct size");
}

void RiverVertexMeshExposesTargetAttributes()
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

void RiverVertexLongitudinalUvUsesRiverDefineScale()
{
    var segment = new RiverSegment
    {
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(10, 0, 0),
        ],
        WorldLength = 10,
        AvgHalfWidth = 0.5f,
        TaperStart = true,
    };

    var mesh = new RiverMeshService(null!).BuildRiverMesh(segment, 1.0f);

    AssertNearlyEqual(0.0f, mesh.Vertices[0].UV.X, 0.0001f, "river longitudinal UV should start at zero");
    AssertNearlyEqual(4.0f, mesh.Vertices[^1].UV.X, 0.0001f, "river longitudinal UV should use map-unit distance times river UV scale");
    AssertNearlyEqual(0.0f, mesh.Vertices[0].DistanceToMain, 0.0001f, "distance-to-main should still use normalized progress");
    AssertNearlyEqual(1.0f, mesh.Vertices[^1].DistanceToMain, 0.0001f, "distance-to-main should not use scaled longitudinal UV");
}

void RiverVertexMeshPreservesSlopedRibbonBasis()
{
    var segment = new RiverSegment
    {
        SystemId = 10,
        Centerline =
        [
            new Vector3(0, 0, 0),
            new Vector3(10, 2, 0),
        ],
        WorldLength = MathF.Sqrt(104.0f),
        AvgHalfWidth = 0.5f,
    };

    var mesh = new RiverMeshService(null!).BuildRiverMesh(segment, 1.0f);
    var left = mesh.Vertices[0];
    var right = mesh.Vertices[1];
    Vector3 side = Vector3.Normalize(right.Position.XYZ() - left.Position.XYZ());
    Vector3 expectedNormal = Vector3.Normalize(Vector3.Cross(side, left.Tangent));

    Assert(left.Tangent.Y > 0.0f, "river tangent should preserve centerline slope for target bottom lighting");
    AssertNearlyEqual(0.0f, Vector3.Dot(left.Tangent, left.Normal), 0.0001f, "river normal should be perpendicular to sloped tangent");
    AssertNearlyEqual(0.0f, Vector3.Dot(side, left.Normal), 0.0001f, "river normal should be perpendicular to ribbon side");
    AssertNearlyEqual(expectedNormal.X, left.Normal.X, 0.0001f, "river normal x should come from ribbon basis");
    AssertNearlyEqual(expectedNormal.Y, left.Normal.Y, 0.0001f, "river normal y should come from ribbon basis");
    AssertNearlyEqual(expectedNormal.Z, left.Normal.Z, 0.0001f, "river normal z should come from ribbon basis");
}

void RiverRenderResourcesComputeHalfResolution()
{
    AssertEqual((960, 540), Terrain.Rendering.River.RiverRenderResources.ComputeHalfResolutionSize(1920, 1080), "RiverRenderResources halves even viewport dimensions");
    AssertEqual((961, 541), Terrain.Rendering.River.RiverRenderResources.ComputeHalfResolutionSize(1921, 1081), "RiverRenderResources rounds odd viewport dimensions up");
    AssertEqual((1, 1), Terrain.Rendering.River.RiverRenderResources.ComputeHalfResolutionSize(0, 0), "RiverRenderResources keeps minimum render target size");
    AssertEqual(Stride.Graphics.PixelFormat.R16G16B16A16_Float, Terrain.Rendering.River.RiverRenderResources.RefractionFormat, "RiverRenderResources uses half-float refraction format");
}

void RiverComponentUsesRiverProcessor()
{
    var attributes = typeof(Terrain.Rendering.River.RiverComponent).GetCustomAttributes(false);
    var rendererAttribute = attributes.OfType<Stride.Engine.Design.DefaultEntityComponentRendererAttribute>().FirstOrDefault();

    Assert(rendererAttribute != null, "RiverComponent should declare a default processor");
    AssertEqual(typeof(Terrain.Rendering.River.RiverProcessor).AssemblyQualifiedName, rendererAttribute!.TypeName, "RiverComponent should use RiverProcessor");
}

void TestRiverComponentVersioning()
{
    var component = new Terrain.Rendering.River.RiverComponent();
    int initial = component.Version;
    var vertices = new[]
    {
        new Terrain.Rendering.River.RiverVertex(new Vector3(1, 2, 3), 1.0f, Vector2.Zero, Vector3.UnitX, Vector3.UnitY, 0.5f, 1.0f)
    };
    var indices = new[] { 0 };
    var meshes = new List<Terrain.Rendering.River.RiverMeshData>
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
    Assert(component.MeshCount == 1, "RiverComponent exposes mesh count without snapshot access");
    Assert(component.Meshes.Count == 1, "RiverComponent stores meshes");

    meshes.Clear();
    vertices[0] = new Terrain.Rendering.River.RiverVertex(new Vector3(9, 9, 9), 0.0f, Vector2.One, Vector3.UnitZ, Vector3.UnitY, 0.0f, 0.0f);
    indices[0] = 99;

    Assert(component.Meshes.Count == 1, "RiverComponent snapshots the mesh list");
    AssertEqual(new Vector4(1, 2, 3, 1), component.Meshes[0].Vertices[0].Position, "RiverComponent snapshots vertex arrays");
    AssertEqual(0, component.Meshes[0].Indices[0], "RiverComponent snapshots index arrays");

    component.Clear();

    Assert(component.Version == initial + 2, "RiverComponent.Clear increments Version");
    Assert(component.MeshCount == 0, "RiverComponent.Clear updates mesh count");
    Assert(component.Meshes.Count == 0, "RiverComponent.Clear removes meshes");
}

void RiverComponentRecordsRuntimeLoadFailureConfig()
{
    var component = new Terrain.Rendering.River.RiverComponent();
    var config = new Terrain.Rendering.River.RiverRuntimeLoadConfig("map/rivers.png", 1.0f, 4.0f, 1000.0f, 200.0f, 4096, 2048);

    component.MarkRuntimeLoadFailure(config);

    AssertEqual(Terrain.Rendering.River.RiverRuntimeLoadState.Failed, component.RuntimeLoadState, "runtime river state");
    Assert(!component.ShouldAttemptRuntimeLoad(config), "same failed config should not retry");

    var changed = new Terrain.Rendering.River.RiverRuntimeLoadConfig("map/rivers.png", 2.0f, 4.0f, 1000.0f, 200.0f, 4096, 2048);
    Assert(component.ShouldAttemptRuntimeLoad(changed), "changed failed config should retry");
}

void RiverComponentExposesMeshSnapshots()
{
    var component = new Terrain.Rendering.River.RiverComponent();
    component.SetMeshes(
    [
        new Terrain.Rendering.River.RiverMeshData
        {
            SegmentId = 7,
            Vertices =
            [
                new Terrain.Rendering.River.RiverVertex(new Vector3(1, 2, 3), 1.0f, Vector2.Zero, Vector3.UnitX, Vector3.UnitY, 0.5f, 1.0f)
            ],
            Indices = [0],
        }
    ]);

    var publishedMeshes = component.Meshes;
    publishedMeshes[0].Vertices[0] = new Terrain.Rendering.River.RiverVertex(new Vector3(9, 9, 9), 0.0f, Vector2.One, Vector3.UnitZ, Vector3.UnitY, 0.0f, 0.0f);
    publishedMeshes[0].Indices[0] = 99;

    AssertEqual(new Vector4(1, 2, 3, 1), component.Meshes[0].Vertices[0].Position, "RiverComponent.Meshes returns vertex snapshots");
    AssertEqual(0, component.Meshes[0].Indices[0], "RiverComponent.Meshes returns index snapshots");
}

void RiverRenderingServiceUpdatesComponentVisibility()
{
    var component = new Terrain.Rendering.River.RiverComponent();
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
    var component = new Terrain.Rendering.River.RiverComponent();
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

static float MaxHorizontalTurnAngle(IReadOnlyList<Vector3> points)
{
    float maxAngle = 0.0f;
    for (int i = 1; i < points.Count - 1; i++)
    {
        Vector3 incoming = points[i] - points[i - 1];
        Vector3 outgoing = points[i + 1] - points[i];
        incoming.Y = 0.0f;
        outgoing.Y = 0.0f;

        if (incoming.LengthSquared() <= 0.000001f || outgoing.LengthSquared() <= 0.000001f)
            continue;

        incoming.Normalize();
        outgoing.Normalize();
        float dot = Math.Clamp(Vector3.Dot(incoming, outgoing), -1.0f, 1.0f);
        maxAngle = MathF.Max(maxAngle, MathF.Acos(dot) * 180.0f / MathF.PI);
    }

    return maxAngle;
}

static float MaxHorizontalDistanceToPolyline(IReadOnlyList<Vector3> points, IReadOnlyList<Vector3> polyline)
{
    float maxDistance = 0.0f;
    foreach (Vector3 point in points)
    {
        float minDistance = float.MaxValue;
        for (int i = 0; i < polyline.Count - 1; i++)
            minDistance = MathF.Min(minDistance, HorizontalDistanceToSegment(point, polyline[i], polyline[i + 1]));

        maxDistance = MathF.Max(maxDistance, minDistance);
    }

    return maxDistance;
}

static float HorizontalDistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
{
    Vector2 p = new(point.X, point.Z);
    Vector2 a = new(start.X, start.Z);
    Vector2 b = new(end.X, end.Z);
    Vector2 segment = b - a;
    float lengthSquared = segment.LengthSquared();
    if (lengthSquared <= 0.000001f)
        return Vector2.Distance(p, a);

    float t = Math.Clamp(Vector2.Dot(p - a, segment) / lengthSquared, 0.0f, 1.0f);
    return Vector2.Distance(p, a + segment * t);
}

static float MaxRiverBoundaryTurnAngle(IReadOnlyList<Terrain.Rendering.River.RiverVertex> vertices)
{
    var left = new List<Vector3>();
    var right = new List<Vector3>();
    for (int i = 0; i < vertices.Count - 1; i += 2)
    {
        left.Add(vertices[i].Position.XYZ());
        right.Add(vertices[i + 1].Position.XYZ());
    }

    return MathF.Max(MaxHorizontalTurnAngle(left), MaxHorizontalTurnAngle(right));
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static TException AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}, got {ex.GetType().Name}");
    }

    throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}");
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
