using Terrain.Editor.Models;
using Terrain.Editor.Services;
using Terrain.Editor.ViewModels;
using Terrain.Editor.Tests;

namespace Terrain.Editor.Tests;

internal static class RiverViewModelAutoGenerationTests
{
    public static void RunAll()
    {
        TestHarness.Run("river view model generates when generator attaches after river map is already loaded", GeneratesWhenGeneratorAttachesAfterRiverMapLoad);
        TestHarness.Run("river view model generates when river map changes after generator is ready", GeneratesWhenRiverMapChangesAfterGeneratorIsReady);
        TestHarness.Run("river view model regenerates when width scale changes", RegeneratesWhenWidthScaleChanges);
        TestHarness.Run("river view model clears generated meshes when river map is cleared", ClearsGeneratedMeshesWhenRiverMapIsCleared);
        TestHarness.Run("river view model passes map width range to generator", PassesMapWidthRangeToGenerator);
    }

    private static void GeneratesWhenGeneratorAttachesAfterRiverMapLoad()
    {
        var source = new FakeRiverMapSource(new RiverCell[4, 3], "map/rivers.png");
        using var viewModel = new RiverViewModel(source);
        var generator = new FakeRiverMeshGenerator(new RiverGenerationResult(2, 3, 8));

        TestHarness.AssertEqual("River map loaded: 4x3", viewModel.StatusText, "river status should reflect the loaded river map before generator wiring");

        viewModel.SetGenerator(generator);

        TestHarness.AssertEqual(1, generator.GenerateCalls, "generator should run as soon as services attach");
        TestHarness.AssertEqual("✓ 2 systems, 3 segments, 8 vertices", viewModel.StatusText, "river status should switch to generated summary after generator wiring");
    }

    private static void GeneratesWhenRiverMapChangesAfterGeneratorIsReady()
    {
        var source = new FakeRiverMapSource();
        using var viewModel = new RiverViewModel(source);
        var generator = new FakeRiverMeshGenerator(new RiverGenerationResult(1, 2, 6));

        viewModel.SetGenerator(generator);
        source.SetRiverMap(new RiverCell[5, 6], "map/rivers.png");

        TestHarness.AssertEqual(1, generator.GenerateCalls, "generator should run when a river map is loaded after generator wiring");
        TestHarness.AssertEqual(5, generator.LastWidth, "generator should receive the loaded river map width");
        TestHarness.AssertEqual(6, generator.LastHeight, "generator should receive the loaded river map height");
        TestHarness.AssertEqual(1.0f, generator.LastWidthScale, "generator should use the default width scale");
    }

    private static void RegeneratesWhenWidthScaleChanges()
    {
        var source = new FakeRiverMapSource(new RiverCell[2, 2], "map/rivers.png");
        using var viewModel = new RiverViewModel(source);
        var generator = new FakeRiverMeshGenerator(new RiverGenerationResult(1, 1, 4));

        viewModel.SetGenerator(generator);
        viewModel.WidthScale = 1.75;

        TestHarness.AssertEqual(2, generator.GenerateCalls, "generator should rerun after width scale changes");
        TestHarness.AssertEqual(1.75f, generator.LastWidthScale, "generator should receive the updated width scale");
    }

    private static void ClearsGeneratedMeshesWhenRiverMapIsCleared()
    {
        var source = new FakeRiverMapSource(new RiverCell[2, 2], "map/rivers.png");
        using var viewModel = new RiverViewModel(source);
        var generator = new FakeRiverMeshGenerator(new RiverGenerationResult(1, 1, 4));

        viewModel.SetGenerator(generator);
        source.ClearRiverMap();

        TestHarness.AssertEqual(1, generator.ClearCalls, "generator should clear generated meshes when river map is removed");
        TestHarness.AssertEqual("No river map loaded", viewModel.StatusText, "river status should revert when the river map is removed");
        TestHarness.AssertEqual(false, viewModel.HasRiverMap, "view model should report that no river map is loaded");
    }

    private static void PassesMapWidthRangeToGenerator()
    {
        var source = new FakeRiverMapSource(new RiverCell[2, 2], "map/rivers.png")
        {
            RiverMinWidth = 2.0f,
            RiverMaxWidth = 6.0f,
        };
        using var viewModel = new RiverViewModel(source);
        var generator = new FakeRiverMeshGenerator(new RiverGenerationResult(1, 1, 4));

        viewModel.SetGenerator(generator);

        TestHarness.AssertEqual(2.0f, generator.LastRiverMinWidth, "generator min full width");
        TestHarness.AssertEqual(6.0f, generator.LastRiverMaxWidth, "generator max full width");
    }

    private sealed class FakeRiverMapSource : IRiverMapSource
    {
        public FakeRiverMapSource(RiverCell[,]? riverMap = null, string? riverMapPath = null)
        {
            RiverMap = riverMap;
            CurrentRiverMapPath = riverMapPath;
        }

        public event EventHandler? RiverMapChanged;

        public RiverCell[,]? RiverMap { get; private set; }

        public string? CurrentRiverMapPath { get; private set; }

        public float RiverMinWidth { get; set; } = 1.0f;

        public float RiverMaxWidth { get; set; } = 4.0f;

        public void SetRiverMap(RiverCell[,] riverMap, string path)
        {
            RiverMap = riverMap;
            CurrentRiverMapPath = path;
            RiverMapChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearRiverMap()
        {
            RiverMap = null;
            CurrentRiverMapPath = null;
            RiverMapChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeRiverMeshGenerator(RiverGenerationResult? result) : IRiverMeshGenerator
    {
        public int GenerateCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public int LastWidth { get; private set; }

        public int LastHeight { get; private set; }

        public float LastWidthScale { get; private set; }

        public float LastRiverMinWidth { get; private set; }

        public float LastRiverMaxWidth { get; private set; }

        public RiverGenerationResult? Generate(RiverCell[,] cells, float widthScale, float riverMinWidth, float riverMaxWidth)
        {
            GenerateCalls++;
            LastWidth = cells.GetLength(0);
            LastHeight = cells.GetLength(1);
            LastWidthScale = widthScale;
            LastRiverMinWidth = riverMinWidth;
            LastRiverMaxWidth = riverMaxWidth;
            return result;
        }

        public void Clear()
        {
            ClearCalls++;
        }
    }
}
