using Terrain.Editor.Services;
using Terrain.Editor.Services.Resources;
using Terrain.Editor.Tests;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorResourceSaveServiceTests
{
    public static void RunAll()
    {
        TestHarness.Run("authoring save reports progress in expected order", AuthoringSaveReportsProgressInExpectedOrder);
        TestHarness.Run("authoring save reports failing writer before rollback", AuthoringSaveReportsFailingWriterBeforeRollback);
        TestHarness.Run("authoring save rolls back earlier files when a later writer fails", AuthoringSaveRollsBackEarlierFilesWhenLaterWriterFails);
        TestHarness.Run("authoring save progress failed factory reports completed error", AuthoringSaveProgressFailedFactoryReportsCompletedError);
    }

    private static void AuthoringSaveProgressFailedFactoryReportsCompletedError()
    {
        AuthoringSaveProgress progress = AuthoringSaveProgress.Failed(3, 9, "bad");

        TestHarness.AssertEqual(3, progress.Current, "failed progress current");
        TestHarness.AssertEqual(9, progress.Total, "failed progress total");
        TestHarness.Assert(progress.IsCompleted, "failed progress should be terminal");
        TestHarness.AssertEqual("bad", progress.ErrorMessage, "failed progress error message");
        TestHarness.AssertEqual("bad", progress.Message, "failed progress message");
    }

    private static void AuthoringSaveReportsProgressInExpectedOrder()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);
        biomeMask.SetValue(0, 0, 1);
        biomeMask.SetValue(1, 1, 2);
        var progress = new CapturingSaveProgress();

        EditorResourceSaveService.Save(
            fixture.Session,
            [1, 2, 3, 4],
            width: 2,
            height: 2,
            biomeMask,
            heightScale: 222.0f,
            descriptorSlots:
            [
                new EditorMaterialDescriptorSlot("grass", 0, "Grass", "grass.png", null, null),
            ],
            biomeSnapshot: new EditorBiomeSettingsSnapshot([], [], []),
            progress: progress);

        AssertProgressSequence(
            progress.Reports,
            [2, 3, 4, 5, 6, 7, 8],
            expectedTotal: AuthoringSaveProgress.TotalSteps);

        string[] messages = progress.Messages;
        TestHarness.Assert(
            messages.Contains("Writing heightmap PNG..."),
            "progress should report heightmap writer");
        TestHarness.Assert(
            messages.Contains("Committing staged resources..."),
            "progress should report commit");
    }

    private static void AuthoringSaveReportsFailingWriterBeforeRollback()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);
        var progress = new CapturingSaveProgress();

        TestHarness.AssertThrows<InvalidDataException>(
            () => EditorResourceSaveService.Save(
                fixture.Session,
                [1, 2, 3, 4],
                width: 2,
                height: 2,
                biomeMask,
                heightScale: 222.0f,
                descriptorSlots:
                [
                    new EditorMaterialDescriptorSlot("grass", 0, "Grass", "nested/grass.png", null, null),
                ],
                biomeSnapshot: new EditorBiomeSettingsSnapshot([], [], []),
                progress: progress),
            "invalid material descriptor path should fail the save");

        AssertProgressSequence(
            progress.Reports,
            [2, 3, 4, 5, 6],
            expectedTotal: AuthoringSaveProgress.TotalSteps);

        string[] messages = progress.Messages;
        TestHarness.Assert(
            messages.Contains("Writing material descriptor..."),
            "progress should report the failing material descriptor writer");
        TestHarness.Assert(
            !progress.Reports.Any(report => report.Current == 7),
            "progress should not report biome settings writer after a writer fails");
        TestHarness.Assert(
            !progress.Reports.Any(report => report.Current == 8),
            "progress should not report commit after a writer fails");
        TestHarness.Assert(
            !messages.Contains("Writing biome settings..."),
            "progress should not report biome settings after a writer fails");
        TestHarness.Assert(
            !messages.Contains("Committing staged resources..."),
            "progress should not report commit after a writer fails");

        AssertOriginalFilesRestored(fixture);
    }

    private static void AuthoringSaveRollsBackEarlierFilesWhenLaterWriterFails()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);
        biomeMask.SetValue(0, 0, 1);
        biomeMask.SetValue(1, 1, 2);

        TestHarness.AssertThrows<InvalidDataException>(
            () => EditorResourceSaveService.Save(
                fixture.Session,
                [1, 2, 3, 4],
                width: 2,
                height: 2,
                biomeMask,
                heightScale: 222.0f,
                descriptorSlots:
                [
                    new EditorMaterialDescriptorSlot("grass", 0, "Grass", "nested/grass.png", null, null),
                ],
                biomeSnapshot: new EditorBiomeSettingsSnapshot([], [], [])),
            "invalid material descriptor path should fail the save");

        AssertOriginalFilesRestored(fixture);
    }

    private static SaveFixture CreatePopulatedSaveFixture()
    {
        string root = CreateWorkspace();
        string mapDefinitionPath = Path.Combine(root, "mod", "map_data", "default.toml");
        string heightmapPath = Path.Combine(root, "mod", "map_data", "heightmap.png");
        string biomeMaskPath = Path.Combine(root, "mod", "map_data", "biome_mask.png");
        string biomeSettingsPath = Path.Combine(root, "mod", "map_data", "biome_settings.toml");
        string materialDescriptorPath = Path.Combine(root, "mod", "map_data", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(materialDescriptorPath)!);
        File.WriteAllText(mapDefinitionPath, "original-default");
        File.WriteAllText(heightmapPath, "original-heightmap");
        File.WriteAllText(biomeMaskPath, "original-biome-mask");
        File.WriteAllText(biomeSettingsPath, "original-biome-settings");
        File.WriteAllText(materialDescriptorPath, "original-material-descriptor");

        EditorResourceSession session = CreateSession(
            root,
            mapDefinitionPath,
            heightmapPath,
            biomeMaskPath,
            biomeSettingsPath,
            materialDescriptorPath);

        return new SaveFixture(
            session,
            mapDefinitionPath,
            heightmapPath,
            biomeMaskPath,
            biomeSettingsPath,
            materialDescriptorPath);
    }

    private static void AssertOriginalFilesRestored(SaveFixture fixture)
    {
        TestHarness.AssertEqual("original-default", File.ReadAllText(fixture.MapDefinitionPath), "map definition should roll back");
        TestHarness.AssertEqual("original-heightmap", File.ReadAllText(fixture.HeightmapPath), "heightmap should roll back");
        TestHarness.AssertEqual("original-biome-mask", File.ReadAllText(fixture.BiomeMaskPath), "biome mask should roll back");
        TestHarness.AssertEqual("original-biome-settings", File.ReadAllText(fixture.BiomeSettingsPath), "biome settings should roll back");
        TestHarness.AssertEqual("original-material-descriptor", File.ReadAllText(fixture.MaterialDescriptorPath), "material descriptor should roll back");
    }

    private static void AssertProgressSequence(
        IReadOnlyList<AuthoringSaveProgress> reports,
        IReadOnlyList<int> expectedCurrent,
        int expectedTotal)
    {
        TestHarness.AssertEqual(expectedCurrent.Count, reports.Count, "progress report count");
        for (int i = 0; i < expectedCurrent.Count; i++)
        {
            TestHarness.AssertEqual(expectedCurrent[i], reports[i].Current, $"progress current {i}");
            TestHarness.AssertEqual(expectedTotal, reports[i].Total, $"progress total {i}");
        }
    }

    private static EditorResourceSession CreateSession(
        string root,
        string mapDefinitionPath,
        string heightmapPath,
        string biomeMaskPath,
        string biomeSettingsPath,
        string materialDescriptorPath)
    {
        static ResolvedGameResource Resource(string virtualPath, string path)
        {
            return new ResolvedGameResource(virtualPath, path, "mod", IsWritable: true, HasLowerPriorityFallback: true);
        }

        return new EditorResourceSession(
            Resource("map_data/default.toml", mapDefinitionPath),
            Resource("map_data/heightmap.png", heightmapPath),
            Resource("map_data/terrain.terrain", Path.Combine(root, "mod", "map_data", "terrain.terrain")),
            Resource("map_data/biome_mask.png", biomeMaskPath),
            Resource("map_data/biome_settings.toml", biomeSettingsPath),
            Resource("map_data/materials/descriptor.toml", materialDescriptorPath),
            new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 100.0f,
            });
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-editor-save-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class SaveFixture(
        EditorResourceSession session,
        string mapDefinitionPath,
        string heightmapPath,
        string biomeMaskPath,
        string biomeSettingsPath,
        string materialDescriptorPath)
    {
        public EditorResourceSession Session { get; } = session;
        public string MapDefinitionPath { get; } = mapDefinitionPath;
        public string HeightmapPath { get; } = heightmapPath;
        public string BiomeMaskPath { get; } = biomeMaskPath;
        public string BiomeSettingsPath { get; } = biomeSettingsPath;
        public string MaterialDescriptorPath { get; } = materialDescriptorPath;
    }

    private sealed class CapturingSaveProgress : IProgress<AuthoringSaveProgress>
    {
        public List<AuthoringSaveProgress> Reports { get; } = [];

        public string[] Messages => Reports
            .Select(report => report.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        public void Report(AuthoringSaveProgress value)
        {
            Reports.Add(value);
        }
    }
}
