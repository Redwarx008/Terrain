using Terrain.Editor.Models;
using Terrain.Editor.Rendering.River;
using Terrain.Editor.Services;
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

    private static TerrainManager CreateFlatTerrainManagerStub()
        => CreateFlatTerrainManagerStub(1, 1);

    private static TerrainManager CreateFlatTerrainManagerStub(int width, int height)
    {
#pragma warning disable SYSLIB0050
        var terrainManager = (TerrainManager)FormatterServices.GetUninitializedObject(typeof(TerrainManager));
#pragma warning restore SYSLIB0050

        SetInstanceField(terrainManager, "heightDataCache", new ushort[width * height]);
        SetInstanceField(terrainManager, "heightDataWidth", width);
        SetInstanceField(terrainManager, "heightDataHeight", height);
        SetInstanceField(terrainManager, "<HeightScale>k__BackingField", 200.0f);
        return terrainManager;
    }

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

        TestHarness.Assert(MathF.Abs(mesh.MapExtent - 8.0f) <= 0.0001f, "River mesh map extent should use max(heightmap dimensions - 1), matching terrain world coordinates");
        TestHarness.Assert(MathF.Abs(mesh.Vertices[0].Width - 0.25f) <= 0.0001f, "River vertex width should be normalized by world coordinate extent, not sample count");
    }

    private static void SetInstanceField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        where TTarget : class
    {
        FieldInfo? field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException($"Could not find field '{fieldName}' on {typeof(TTarget).FullName}.");

        field.SetValue(target, value);
    }
}
