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
        TestHarness.Run("authoring save persists river max visible camera height", AuthoringSavePersistsRiverMaxVisibleCameraHeight);
        TestHarness.Run("authoring save preserves loaded river max visible camera height by default", AuthoringSavePreservesLoadedRiverMaxVisibleCameraHeightByDefault);
        TestHarness.Run("authoring save persists sea level", AuthoringSavePersistsSeaLevel);
        TestHarness.Run("authoring save preserves loaded sea level by default", AuthoringSavePreservesLoadedSeaLevelByDefault);
        TestHarness.Run("authoring save writes only dirty heightmap resource", AuthoringSaveWritesOnlyDirtyHeightmapResource);
        TestHarness.Run("authoring save writes only dirty map definition resource", AuthoringSaveWritesOnlyDirtyMapDefinitionResource);
        TestHarness.Run("authoring save writes only dirty biome mask resource", AuthoringSaveWritesOnlyDirtyBiomeMaskResource);
        TestHarness.Run("authoring save writes only dirty material descriptor resource", AuthoringSaveWritesOnlyDirtyMaterialDescriptorResource);
        TestHarness.Run("authoring save writes only dirty biome settings resource", AuthoringSaveWritesOnlyDirtyBiomeSettingsResource);
        TestHarness.Run("authoring save writes descriptor and biome settings together when material ids may change", AuthoringSaveWritesDescriptorAndBiomeSettingsTogetherWhenMaterialIdsMayChange);
        TestHarness.Run("authoring partial map save does not require unrelated resource payloads", AuthoringPartialMapSaveDoesNotRequireUnrelatedResourcePayloads);
        TestHarness.Run("authoring save detects missing generated biome mask resource", AuthoringSaveDetectsMissingGeneratedBiomeMaskResource);
        TestHarness.Run("authoring save skips commit when no authoring resources are dirty", AuthoringSaveSkipsCommitWhenNoAuthoringResourcesAreDirty);
        TestHarness.Run("authoring save reports failing writer before rollback", AuthoringSaveReportsFailingWriterBeforeRollback);
        TestHarness.Run("authoring save rolls back earlier files when a later writer fails", AuthoringSaveRollsBackEarlierFilesWhenLaterWriterFails);
        TestHarness.Run("authoring save rolls back dirty files only when partial dirty save fails", AuthoringSaveRollsBackDirtyFilesOnlyWhenPartialDirtySaveFails);
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

    private static void AuthoringSavePersistsRiverMaxVisibleCameraHeight()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);

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
            riverMaxVisibleCameraHeight: 875.0f);

        RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(fixture.MapDefinitionPath);

        TestHarness.AssertEqual(875.0f, saved.RiverMaxVisibleCameraHeight, "save should persist river max visible camera height");
    }

    private static void AuthoringSavePreservesLoadedRiverMaxVisibleCameraHeightByDefault()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture(riverMaxVisibleCameraHeight: 875.0f);
        var biomeMask = new BiomeMask(2, 2);

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
            biomeSnapshot: new EditorBiomeSettingsSnapshot([], [], []));

        RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(fixture.MapDefinitionPath);

        TestHarness.AssertEqual(875.0f, saved.RiverMaxVisibleCameraHeight, "save should preserve loaded river max visible camera height when caller omits the parameter");
    }

    private static void AuthoringSavePreservesLoadedSeaLevelByDefault()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture(seaLevel: 9.25f);
        var biomeMask = new BiomeMask(2, 2);

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
            biomeSnapshot: new EditorBiomeSettingsSnapshot([], [], []));

        RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(fixture.MapDefinitionPath);

        TestHarness.AssertEqual(9.25f, saved.SeaLevel, "save should preserve loaded sea level when caller omits the parameter");
    }

    private static void AuthoringSavePersistsSeaLevel()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);

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
            seaLevel: 9.25f);

        RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(fixture.MapDefinitionPath);

        TestHarness.AssertEqual(9.25f, saved.SeaLevel, "save should persist sea level");
    }

    private static void AuthoringSaveWritesOnlyDirtyHeightmapResource()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);

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
            dirtyResources: EditorDirtyResource.Heightmap);

        TestHarness.Assert(File.ReadAllText(fixture.HeightmapPath) != "original-heightmap", "heightmap should be rewritten");
        TestHarness.AssertEqual("original-default", File.ReadAllText(fixture.MapDefinitionPath), "map definition should not be rewritten");
        TestHarness.AssertEqual("original-biome-mask", File.ReadAllText(fixture.BiomeMaskPath), "biome mask should not be rewritten");
        TestHarness.AssertEqual("original-biome-settings", File.ReadAllText(fixture.BiomeSettingsPath), "biome settings should not be rewritten");
        TestHarness.AssertEqual("original-material-descriptor", File.ReadAllText(fixture.MaterialDescriptorPath), "material descriptor should not be rewritten");
    }

    private static void AuthoringSaveWritesOnlyDirtyMapDefinitionResource()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);

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
            dirtyResources: EditorDirtyResource.MapDefinition,
            riverMaxVisibleCameraHeight: 875.0f);

        RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(fixture.MapDefinitionPath);

        TestHarness.AssertEqual(222.0f, saved.HeightScale, "map definition should be rewritten");
        TestHarness.AssertEqual(875.0f, saved.RiverMaxVisibleCameraHeight, "map definition should persist dirty settings");
        TestHarness.AssertEqual("original-heightmap", File.ReadAllText(fixture.HeightmapPath), "heightmap should not be rewritten");
        TestHarness.AssertEqual("original-biome-mask", File.ReadAllText(fixture.BiomeMaskPath), "biome mask should not be rewritten");
        TestHarness.AssertEqual("original-biome-settings", File.ReadAllText(fixture.BiomeSettingsPath), "biome settings should not be rewritten");
        TestHarness.AssertEqual("original-material-descriptor", File.ReadAllText(fixture.MaterialDescriptorPath), "material descriptor should not be rewritten");
    }

    private static void AuthoringSaveWritesOnlyDirtyBiomeMaskResource()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);
        biomeMask.SetValue(0, 0, 4);
        biomeMask.SetValue(1, 1, 5);

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
            dirtyResources: EditorDirtyResource.BiomeMask);

        TestHarness.Assert(File.ReadAllText(fixture.BiomeMaskPath) != "original-biome-mask", "biome mask should be rewritten");
        AssertAllExcept(fixture, except: EditorDirtyResource.BiomeMask);
    }

    private static void AuthoringSaveWritesOnlyDirtyMaterialDescriptorResource()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);

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
            dirtyResources: EditorDirtyResource.MaterialDescriptor);

        TestHarness.Assert(File.ReadAllText(fixture.MaterialDescriptorPath) != "original-material-descriptor", "material descriptor should be rewritten");
        AssertAllExcept(fixture, except: EditorDirtyResource.MaterialDescriptor);
    }

    private static void AuthoringSaveWritesOnlyDirtyBiomeSettingsResource()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);

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
            biomeSnapshot: new EditorBiomeSettingsSnapshot(
                [new EditorBiomeDefinition(1, "Default")],
                [new EditorBiomeLayerDefinition(1, 1, "Base", "grass", 0, Enabled: true, Visible: true)],
                []),
            dirtyResources: EditorDirtyResource.BiomeSettings);

        TestHarness.Assert(File.ReadAllText(fixture.BiomeSettingsPath) != "original-biome-settings", "biome settings should be rewritten");
        AssertAllExcept(fixture, except: EditorDirtyResource.BiomeSettings);
    }

    private static void AuthoringSaveWritesDescriptorAndBiomeSettingsTogetherWhenMaterialIdsMayChange()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();

        EditorResourceSaveService.Save(
            fixture.Session,
            heightData: null,
            width: 0,
            height: 0,
            biomeMask: null,
            heightScale: 222.0f,
            descriptorSlots:
            [
                new EditorMaterialDescriptorSlot("forest", 4, "Forest", "forest.png", null, null),
            ],
            biomeSnapshot: new EditorBiomeSettingsSnapshot(
                [new EditorBiomeDefinition(1, "Default")],
                [new EditorBiomeLayerDefinition(1, 1, "Base", "forest", 0, Enabled: true, Visible: true)],
                []),
            dirtyResources: EditorDirtyResource.MaterialDescriptor | EditorDirtyResource.BiomeSettings);

        TestHarness.Assert(File.ReadAllText(fixture.MaterialDescriptorPath).Contains("forest", StringComparison.Ordinal), "material descriptor should contain the new material id");
        TestHarness.Assert(File.ReadAllText(fixture.BiomeSettingsPath).Contains("forest", StringComparison.Ordinal), "biome settings should reference the new material id");
        AssertAllExcept(fixture, except: EditorDirtyResource.MaterialDescriptor | EditorDirtyResource.BiomeSettings);
    }

    private static void AuthoringPartialMapSaveDoesNotRequireUnrelatedResourcePayloads()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();

        EditorResourceSaveService.Save(
            fixture.Session,
            heightData: null,
            width: 0,
            height: 0,
            biomeMask: null,
            heightScale: 222.0f,
            descriptorSlots: null,
            biomeSnapshot: null,
            dirtyResources: EditorDirtyResource.MapDefinition,
            riverMaxVisibleCameraHeight: 875.0f);

        RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(fixture.MapDefinitionPath);

        TestHarness.AssertEqual(222.0f, saved.HeightScale, "map definition should be rewritten without height payload");
        TestHarness.AssertEqual(875.0f, saved.RiverMaxVisibleCameraHeight, "map definition should persist dirty camera height without unrelated payloads");
        AssertAllExcept(fixture, except: EditorDirtyResource.MapDefinition);
    }

    private static void AuthoringSaveDetectsMissingGeneratedBiomeMaskResource()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();

        TestHarness.AssertEqual(
            EditorDirtyResource.None,
            EditorGeneratedAuthoringResourceDetector.DetectMissingGeneratedResources(fixture.Session, hasBiomeMaskData: true),
            "existing biome mask should not be generated");

        File.Delete(fixture.BiomeMaskPath);

        TestHarness.AssertEqual(
            EditorDirtyResource.BiomeMask,
            EditorGeneratedAuthoringResourceDetector.DetectMissingGeneratedResources(fixture.Session, hasBiomeMaskData: true),
            "missing biome mask with in-memory data should be generated on save");
        TestHarness.AssertEqual(
            EditorDirtyResource.None,
            EditorGeneratedAuthoringResourceDetector.DetectMissingGeneratedResources(fixture.Session, hasBiomeMaskData: false),
            "missing biome mask without in-memory data should not be generated");
    }

    private static void AuthoringSaveSkipsCommitWhenNoAuthoringResourcesAreDirty()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var progress = new CapturingSaveProgress();

        EditorResourceSaveService.Save(
            fixture.Session,
            heightData: null,
            width: 0,
            height: 0,
            biomeMask: null,
            heightScale: 222.0f,
            descriptorSlots: null,
            biomeSnapshot: null,
            progress: progress,
            dirtyResources: EditorDirtyResource.None);

        AssertOriginalFilesRestored(fixture);
        TestHarness.Assert(!progress.Reports.Any(report => report.Current == 8), "commit progress should not be reported when nothing is dirty");
        TestHarness.AssertEqual(1, progress.Reports.Count, "no-op save should report one terminal progress event");
        TestHarness.Assert(progress.Reports[0].IsCompleted, "no-op save progress should be terminal");
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

    private static void AuthoringSaveRollsBackDirtyFilesOnlyWhenPartialDirtySaveFails()
    {
        SaveFixture fixture = CreatePopulatedSaveFixture();
        var biomeMask = new BiomeMask(2, 2);

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
                dirtyResources: EditorDirtyResource.MapDefinition | EditorDirtyResource.MaterialDescriptor),
            "invalid material descriptor path should fail the partial save");

        AssertOriginalFilesRestored(fixture);
    }

    private static SaveFixture CreatePopulatedSaveFixture(
        float riverMaxVisibleCameraHeight = 3000.0f,
        float seaLevel = 3.8f)
    {
        string root = CreateWorkspace();
        string mapDefinitionPath = Path.Combine(root, "mod", "map", "default.toml");
        string heightmapPath = Path.Combine(root, "mod", "map", "heightmap.png");
        string biomeMaskPath = Path.Combine(root, "mod", "map", "biome_mask.png");
        string biomeSettingsPath = Path.Combine(root, "mod", "map", "biome_settings.toml");
        string materialDescriptorPath = Path.Combine(root, "mod", "map", "materials", "descriptor.toml");
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
            materialDescriptorPath,
            riverMaxVisibleCameraHeight,
            seaLevel);

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

    private static void AssertAllExcept(SaveFixture fixture, EditorDirtyResource except)
    {
        if ((except & EditorDirtyResource.MapDefinition) == 0)
            TestHarness.AssertEqual("original-default", File.ReadAllText(fixture.MapDefinitionPath), "map definition should not be rewritten");
        if ((except & EditorDirtyResource.Heightmap) == 0)
            TestHarness.AssertEqual("original-heightmap", File.ReadAllText(fixture.HeightmapPath), "heightmap should not be rewritten");
        if ((except & EditorDirtyResource.BiomeMask) == 0)
            TestHarness.AssertEqual("original-biome-mask", File.ReadAllText(fixture.BiomeMaskPath), "biome mask should not be rewritten");
        if ((except & EditorDirtyResource.BiomeSettings) == 0)
            TestHarness.AssertEqual("original-biome-settings", File.ReadAllText(fixture.BiomeSettingsPath), "biome settings should not be rewritten");
        if ((except & EditorDirtyResource.MaterialDescriptor) == 0)
            TestHarness.AssertEqual("original-material-descriptor", File.ReadAllText(fixture.MaterialDescriptorPath), "material descriptor should not be rewritten");
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
        string materialDescriptorPath,
        float riverMaxVisibleCameraHeight = 3000.0f,
        float seaLevel = 3.8f)
    {
        static ResolvedGameResource Resource(string virtualPath, string path)
        {
            return new ResolvedGameResource(virtualPath, path, "mod", IsWritable: true, HasLowerPriorityFallback: true);
        }

        return new EditorResourceSession(
            Resource("map/default.toml", mapDefinitionPath),
            Resource("map/heightmap.png", heightmapPath),
            Resource("map/terrain.terrain", Path.Combine(root, "mod", "map", "terrain.terrain")),
            Resource("map/biome_mask.png", biomeMaskPath),
            Resource("map/biome_settings.toml", biomeSettingsPath),
            Resource("map/materials/descriptor.toml", materialDescriptorPath),
            new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 100.0f,
                RiverMaxVisibleCameraHeight = riverMaxVisibleCameraHeight,
                SeaLevel = seaLevel,
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
