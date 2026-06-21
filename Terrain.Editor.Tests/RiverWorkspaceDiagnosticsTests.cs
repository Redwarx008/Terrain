using Terrain.Editor.Models;
using Terrain.Editor.Rendering.River;
using Terrain.Editor.Services;
using Stride.Core.Mathematics;
using System.Reflection;
using System.Runtime.Serialization;
using SixLabors.ImageSharp.Formats.Png;

namespace Terrain.Editor.Tests;

internal static class RiverWorkspaceDiagnosticsTests
{
    public static void RunAll()
    {
        TestHarness.Run("temporary river map parses into river segments", TemporaryRiverMapParsesIntoRiverSegments);
        TestHarness.Run("temporary river map publishes river meshes through generator", TemporaryRiverMapPublishesRiverMeshesThroughGenerator);
        TestHarness.Run("curved river map publishes smooth mesh boundaries", CurvedRiverMapPublishesSmoothMeshBoundaries);
        TestHarness.Run("long straight river keeps coarse centerline sample budget", LongStraightRiverKeepsCoarseCenterlineSampleBudget);
        TestHarness.Run("curved river centerline resamples terrain height after smoothing", CurvedRiverCenterlineResamplesTerrainHeightAfterSmoothing);
        TestHarness.Run("river mesh map extent uses world coordinate span", RiverMeshMapExtentUsesWorldCoordinateSpan);
    }

    private static void TemporaryRiverMapParsesIntoRiverSegments()
    {
        string riverMapPath = CreateTemporaryRiverMap();

        var service = new RiverMapService();
        bool loaded = service.Load(riverMapPath);
        var segments = service.ExtractSegments();

        int riverCount = 0;
        int sourceCount = 0;
        int confluenceCount = 0;
        int bifurcationCount = 0;
        int oceanCount = 0;

        if (service.Cells is { } cells)
        {
            int width = cells.GetLength(0);
            int height = cells.GetLength(1);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    switch (cells[x, y].Type)
                    {
                        case RiverPixelType.River:
                            riverCount++;
                            break;
                        case RiverPixelType.Source:
                            sourceCount++;
                            break;
                        case RiverPixelType.Confluence:
                            confluenceCount++;
                            break;
                        case RiverPixelType.Bifurcation:
                            bifurcationCount++;
                            break;
                        case RiverPixelType.Ocean:
                            oceanCount++;
                            break;
                    }
                }
            }
        }

        Console.WriteLine(
            $"DIAG temp rivers.png: loaded={loaded}, size={service.Width}x{service.Height}, " +
            $"rivers={riverCount}, sources={sourceCount}, confluences={confluenceCount}, " +
            $"bifurcations={bifurcationCount}, oceans={oceanCount}, errors={service.Errors.Count}, " +
            $"systems={service.SystemCount}, segments={segments.Count}");

        if (service.Errors.Count > 0)
            Console.WriteLine($"DIAG temp rivers.png errors: {string.Join(" | ", service.Errors)}");

        TestHarness.Assert(loaded, "Temporary river map should load successfully");
        TestHarness.AssertEqual(2, segments.Count, "Temporary river map should extract both river branches");
        TestHarness.AssertEqual(1, service.SystemCount, "Temporary river map should keep both branches in one river system");
    }

    private static void TemporaryRiverMapPublishesRiverMeshesThroughGenerator()
    {
        string riverMapPath = CreateTemporaryRiverMap();
        var mapService = new RiverMapService();
        TestHarness.Assert(mapService.Load(riverMapPath), "Temporary river map should load successfully for generator diagnostics");
        TestHarness.Assert(mapService.Cells != null, "Temporary river map should expose cells for generator diagnostics");

        var component = new RiverComponent();
        var renderingService = new RiverRenderingService(component);
        var terrainManager = CreateFlatTerrainManagerStub();
        var meshService = new RiverMeshService(terrainManager);
        var generator = new RiverMeshGenerator(renderingService, meshService);

        RiverGenerationResult? result = generator.Generate(mapService.Cells!, 1.0f);
        RiverGenerationResult generated = result ?? throw new InvalidOperationException("Generator should return a non-null result for temporary river map");

        int meshCount = component.Meshes.Count;
        int vertexCount = component.Meshes.Sum(static mesh => mesh.Vertices.Length);
        int indexCount = component.Meshes.Sum(static mesh => mesh.Indices.Length);
        Console.WriteLine(
            $"DIAG temp river mesh publish: resultSystems={generated.SystemCount}, " +
            $"resultSegments={generated.SegmentCount}, resultVertices={generated.VertexCount}, " +
            $"componentMeshes={meshCount}, componentVertices={vertexCount}, componentIndices={indexCount}");

        TestHarness.AssertEqual(2, generated.SegmentCount, "Generator should emit one mesh per temporary river branch");
        TestHarness.AssertEqual(2, meshCount, "Generator should publish both temporary river branch meshes to RiverComponent");
        TestHarness.Assert(vertexCount > 0, "Published river meshes should contain vertices");
        TestHarness.Assert(indexCount > 0, "Published river meshes should contain indices");
    }

    private static string CreateTemporaryRiverMap()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "terrain-river-workspace-diagnostics");
        Directory.CreateDirectory(tempDirectory);
        string path = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.png");

        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(5, 5))
        {
            image[0, 2] = new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 255, 0);
            image[1, 2] = new SixLabors.ImageSharp.PixelFormats.Rgba32(0x00, 0xe1, 0xff);
            image[2, 2] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 0, 0);
            image[2, 1] = new SixLabors.ImageSharp.PixelFormats.Rgba32(0x00, 0xe1, 0xff);
            image[2, 0] = new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 255, 0);
            using FileStream stream = File.Create(path);
            image.Save(stream, new PngEncoder());
        }

        return path;
    }

    private static void CurvedRiverMapPublishesSmoothMeshBoundaries()
    {
        var cells = new RiverCell[10, 7];
        SetRiver(cells, 0, 2, RiverPixelType.Source);
        SetRiver(cells, 1, 2);
        SetRiver(cells, 2, 2);
        SetRiver(cells, 3, 2);
        SetRiver(cells, 3, 3);
        SetRiver(cells, 3, 4);
        SetRiver(cells, 4, 4);
        SetRiver(cells, 5, 4);
        SetRiver(cells, 6, 4);
        SetRiver(cells, 6, 3);
        SetRiver(cells, 6, 2);
        SetRiver(cells, 7, 2);
        SetRiver(cells, 8, 2);
        SetRiver(cells, 9, 2, RiverPixelType.Confluence);

        var component = new RiverComponent();
        var renderingService = new RiverRenderingService(component);
        var terrainManager = CreateFlatTerrainManagerStub(20, 14);
        var meshService = new RiverMeshService(terrainManager);
        var generator = new RiverMeshGenerator(renderingService, meshService);

        RiverGenerationResult? result = generator.Generate(cells, 1.0f);

        TestHarness.Assert(result != null, "Curved river map should generate a mesh");
        TestHarness.AssertEqual(1, component.Meshes.Count, "Curved river map should publish one segment mesh");
        float maxBoundaryAngle = MaxRiverBoundaryTurnAngle(component.Meshes[0].Vertices);

        TestHarness.Assert(maxBoundaryAngle <= 12.0f, $"Curved river map mesh boundaries should stay smooth, actual max angle {maxBoundaryAngle:0.00}");
    }

    private static void LongStraightRiverKeepsCoarseCenterlineSampleBudget()
    {
        var segment = new RiverSegment();
        for (int x = 0; x <= 40; x++)
            segment.Cells.Add((x, 2));

        var meshService = new RiverMeshService(CreateFlatTerrainManagerStub(84, 8));

        meshService.BuildCenterlines([segment], 41, 5);

        TestHarness.Assert(
            segment.Centerline.Count <= 100,
            $"Long straight river should not use bend-level sampling density everywhere. Actual samples: {segment.Centerline.Count}");
    }

    private static void CurvedRiverCenterlineResamplesTerrainHeightAfterSmoothing()
    {
        var segment = new RiverSegment
        {
            Cells =
            [
                (0, 2),
                (1, 2),
                (2, 2),
                (3, 2),
                (3, 3),
                (3, 4),
                (4, 4),
                (5, 4),
                (6, 4),
                (6, 3),
                (6, 2),
                (7, 2),
                (8, 2),
                (9, 2),
            ],
        };
        const float heightScale = 1000.0f;
        var terrainManager = CreateTerrainManagerStub(24, 16, HeightAtSample, heightScale);
        var meshService = new RiverMeshService(terrainManager);

        meshService.BuildCenterlines([segment], 10, 7);

        float maxError = 0.0f;
        foreach (Vector3 point in segment.Centerline)
        {
            float expected = SampleBilinearEncodedTerrainHeight(point.X, point.Z, 24, 16, HeightAtSample, heightScale) + 0.02f;
            maxError = MathF.Max(maxError, MathF.Abs(point.Y - expected));
        }

        TestHarness.Assert(
            maxError <= 0.03f,
            $"Curved river centerline should bilinearly resample terrain height at smoothed XZ positions. Max height error: {maxError:0.000}");

        static float HeightAtSample(int x, int y) => (x * x + y * y) * 0.5f;
    }

    private static void SetRiver(RiverCell[,] cells, int x, int y, RiverPixelType type = RiverPixelType.River)
    {
        cells[x, y] = new RiverCell(type, 1);
    }

    private static TerrainManager CreateFlatTerrainManagerStub()
        => CreateFlatTerrainManagerStub(1, 1);

    private static TerrainManager CreateFlatTerrainManagerStub(int width, int height)
        => CreateTerrainManagerStub(width, height, static (_, _) => 0.0f, 200.0f);

    private static TerrainManager CreateTerrainManagerStub(int width, int height, Func<int, int, float> heightAtSample, float heightScale)
    {
#pragma warning disable SYSLIB0050
        var terrainManager = (TerrainManager)FormatterServices.GetUninitializedObject(typeof(TerrainManager));
#pragma warning restore SYSLIB0050

        var heightData = new ushort[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float normalizedHeight = Math.Clamp(heightAtSample(x, y) / heightScale, 0.0f, 1.0f);
                heightData[y * width + x] = (ushort)MathF.Round(normalizedHeight * ushort.MaxValue);
            }
        }

        SetInstanceField(terrainManager, "heightDataCache", heightData);
        SetInstanceField(terrainManager, "heightDataWidth", width);
        SetInstanceField(terrainManager, "heightDataHeight", height);
        SetInstanceField(terrainManager, "<HeightScale>k__BackingField", heightScale);
        return terrainManager;
    }

    private static float SampleBilinearEncodedTerrainHeight(
        float wx,
        float wz,
        int width,
        int height,
        Func<int, int, float> heightAtSample,
        float heightScale)
    {
        float x = Math.Clamp(wx, 0.0f, width - 1);
        float y = Math.Clamp(wz, 0.0f, height - 1);
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);
        float tx = x - x0;
        float ty = y - y0;

        float h00 = SampleEncodedTerrainHeight(x0, y0, heightAtSample, heightScale);
        float h10 = SampleEncodedTerrainHeight(x1, y0, heightAtSample, heightScale);
        float h01 = SampleEncodedTerrainHeight(x0, y1, heightAtSample, heightScale);
        float h11 = SampleEncodedTerrainHeight(x1, y1, heightAtSample, heightScale);
        float hx0 = Lerp(h00, h10, tx);
        float hx1 = Lerp(h01, h11, tx);
        return Lerp(hx0, hx1, ty);
    }

    private static float SampleEncodedTerrainHeight(
        int x,
        int y,
        Func<int, int, float> heightAtSample,
        float heightScale)
    {
        float normalizedHeight = Math.Clamp(heightAtSample(x, y) / heightScale, 0.0f, 1.0f);
        ushort encoded = (ushort)MathF.Round(normalizedHeight * ushort.MaxValue);
        return encoded * (1.0f / ushort.MaxValue) * heightScale;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static void RiverMeshMapExtentUsesWorldCoordinateSpan()
    {
        var terrainManager = CreateFlatTerrainManagerStub(5, 9);
        var meshService = new RiverMeshService(terrainManager);
        var segment = new RiverSegment
        {
            SystemId = 7,
            Centerline =
            [
                new Stride.Core.Mathematics.Vector3(0, 0, 0),
                new Stride.Core.Mathematics.Vector3(2, 0, 0),
            ],
            WorldLength = 2,
            AvgHalfWidth = 2.0f,
        };

        RiverMeshData mesh = meshService.BuildRiverMesh(segment, 1.0f);

        TestHarness.Assert(MathF.Abs(mesh.MapExtent - 4.0f) <= 0.0001f, "River mesh map extent should use map-unit span matching river shader MapSize");
        TestHarness.Assert(MathF.Abs(mesh.MapWorldSize.X - 2.0f) <= 0.0001f, "River mesh should keep the X-axis map-unit span separately for rectangular map UV normalization");
        TestHarness.Assert(MathF.Abs(mesh.MapWorldSize.Y - 4.0f) <= 0.0001f, "River mesh should keep the Y-axis map-unit span separately for rectangular map UV normalization");
        TestHarness.Assert(MathF.Abs(mesh.Vertices[0].Width - 0.5f) <= 0.0001f, "River vertex width should be normalized by map-unit extent, not heightmap sample count");
    }

    private static void SetInstanceField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        where TTarget : class
    {
        FieldInfo? field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException($"Could not find field '{fieldName}' on {typeof(TTarget).FullName}.");

        field.SetValue(target, value);
    }

    private static float MaxRiverBoundaryTurnAngle(IReadOnlyList<RiverVertex> vertices)
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

    private static float MaxHorizontalTurnAngle(IReadOnlyList<Vector3> points)
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
}
