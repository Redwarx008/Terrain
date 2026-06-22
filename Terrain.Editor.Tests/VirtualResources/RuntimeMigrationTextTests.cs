using Terrain.Editor.Tests;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class RuntimeMigrationTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("runtime migration removes old shared namespace usages", RemovesTerrainSharedUsages);
        TestHarness.Run("runtime migration removes Shared links from projects", RemovesSharedProjectLinks);
        TestHarness.Run("runtime migration deletes RuntimeBiomeConfig", DeletesRuntimeBiomeConfig);
        TestHarness.Run("runtime migration removes component resource path fields", RemovesComponentResourcePathFields);
        TestHarness.Run("runtime migration removes serialized scene resource paths", RemovesSerializedSceneResourcePaths);
        TestHarness.Run("runtime migration removes old material TOML entry points", RemovesOldMaterialTomlEntryPoints);
        TestHarness.Run("runtime migration removes old biome config export workflow", RemovesOldBiomeConfigExportWorkflow);
        TestHarness.Run("runtime migration removes new and open project entry points", RemovesNewAndOpenProjectEntryPoints);
        TestHarness.Run("runtime migration removes old project toml persistence", RemovesOldProjectTomlPersistence);
        TestHarness.Run("runtime bootstrap failures are logged as errors", RuntimeBootstrapFailuresAreLoggedAsErrors);
        TestHarness.Run("runtime resource bootstrap uses the game-scoped name", RuntimeResourceBootstrapUsesGameScopedName);
        TestHarness.Run("resource bootstrap production paths use Terrain assembly directory", ResourceBootstrapProductionPathsUseTerrainAssemblyDirectory);
        TestHarness.Run("runtime no longer requires biome authoring resources", RuntimeNoLongerRequiresBiomeAuthoringResources);
        TestHarness.Run("runtime migration removes runtime detail generation API", RuntimeMigrationRemovesRuntimeDetailGenerationApi);
        TestHarness.Run("runtime migration removes runtime biome mask reader API", RuntimeMigrationRemovesRuntimeBiomeMaskReaderApi);
    }

    private static void RemovesTerrainSharedUsages()
    {
        foreach (string filePath in EnumerateTextFiles("*.cs"))
        {
            string text = File.ReadAllText(filePath);
            string oldNamespace = "Terrain." + "Shared";
            TestHarness.Assert(!text.Contains(oldNamespace, StringComparison.Ordinal), $"{Relative(filePath)} should not reference the old shared namespace");
        }
    }

    private static void RemovesSharedProjectLinks()
    {
        foreach (string filePath in EnumerateTextFiles("*.csproj"))
        {
            string text = File.ReadAllText(filePath);
            TestHarness.Assert(!text.Contains(@"..\Shared\", StringComparison.Ordinal), $"{Relative(filePath)} should not link Shared sources");
        }
    }

    private static void DeletesRuntimeBiomeConfig()
    {
        string filePath = Path.Combine(RepositoryRoot, "Terrain", "Materials", "RuntimeBiomeConfig.cs");
        TestHarness.Assert(!File.Exists(filePath), "Terrain/Materials/RuntimeBiomeConfig.cs should not exist");
    }

    private static void RemovesComponentResourcePathFields()
    {
        string componentPath = Path.Combine(RepositoryRoot, "Terrain", "Core", "TerrainComponent.cs");
        string text = File.ReadAllText(componentPath);
        TestHarness.Assert(!text.Contains("TerrainDataPath", StringComparison.Ordinal), "TerrainComponent should not contain TerrainDataPath");
        TestHarness.Assert(!text.Contains("BiomeConfigPath", StringComparison.Ordinal), "TerrainComponent should not contain BiomeConfigPath");
    }

    private static void RemovesSerializedSceneResourcePaths()
    {
        string scenePath = Path.Combine(RepositoryRoot, "Terrain", "Assets", "MainScene.sdscene");
        string text = File.ReadAllText(scenePath);
        TestHarness.Assert(!text.Contains("TerrainDataPath", StringComparison.Ordinal), "MainScene should not serialize TerrainDataPath");
        TestHarness.Assert(!text.Contains("BiomeConfigPath", StringComparison.Ordinal), "MainScene should not serialize BiomeConfigPath");
    }

    private static void RemovesOldMaterialTomlEntryPoints()
    {
        string managerPath = Path.Combine(RepositoryRoot, "Terrain", "Materials", "RuntimeMaterialManager.cs");
        string text = File.ReadAllText(managerPath);
        TestHarness.Assert(!text.Contains("InitializeFromToml", StringComparison.Ordinal), "RuntimeMaterialManager should not expose InitializeFromToml");
        TestHarness.Assert(!text.Contains("ReadMaterialSlots(string tomlFilePath)", StringComparison.Ordinal), "RuntimeMaterialManager should not expose ReadMaterialSlots(string tomlFilePath)");
    }

    private static void RemovesOldBiomeConfigExportWorkflow()
    {
        string exporterPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Services", "Export", "Exporters", "BiomeConfigExporter.cs");
        TestHarness.Assert(!File.Exists(exporterPath), "BiomeConfigExporter should not exist");

        string mainWindowPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Views", "MainWindow.axaml");
        string mainWindow = File.ReadAllText(mainWindowPath);
        TestHarness.Assert(!mainWindow.Contains("ExportBiomeConfigCommand", StringComparison.Ordinal), "MainWindow should not expose ExportBiomeConfigCommand");
        TestHarness.Assert(!mainWindow.Contains("Biome Config", StringComparison.Ordinal), "MainWindow should not expose Biome Config export");

        string viewModelPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs");
        string viewModel = File.ReadAllText(viewModelPath);
        TestHarness.Assert(!viewModel.Contains("BiomeConfigExporter", StringComparison.Ordinal), "EditorShellViewModel should not register BiomeConfigExporter");
        TestHarness.Assert(!viewModel.Contains("ExportBiomeConfig", StringComparison.Ordinal), "EditorShellViewModel should not expose ExportBiomeConfig command");
    }

    private static void RemovesNewAndOpenProjectEntryPoints()
    {
        string mainWindowPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Views", "MainWindow.axaml");
        string mainWindow = File.ReadAllText(mainWindowPath);
        TestHarness.Assert(!mainWindow.Contains("NewProjectCommand", StringComparison.Ordinal), "MainWindow should not bind NewProjectCommand");
        TestHarness.Assert(!mainWindow.Contains("OpenProjectCommand", StringComparison.Ordinal), "MainWindow should not bind OpenProjectCommand");
        TestHarness.Assert(!mainWindow.Contains("Ctrl+N", StringComparison.Ordinal), "MainWindow should not expose Ctrl+N new project shortcut");
        TestHarness.Assert(!mainWindow.Contains("Ctrl+O", StringComparison.Ordinal), "MainWindow should not expose Ctrl+O open project shortcut");

        string viewModelPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs");
        string viewModel = File.ReadAllText(viewModelPath);
        TestHarness.Assert(!viewModel.Contains("private async Task NewProject", StringComparison.Ordinal), "EditorShellViewModel should not expose NewProject command");
        TestHarness.Assert(!viewModel.Contains("private async Task OpenProject", StringComparison.Ordinal), "EditorShellViewModel should not expose OpenProject command");
        TestHarness.Assert(!viewModel.Contains("No project", StringComparison.Ordinal), "EditorShellViewModel should not show old project-empty label");
    }

    private static void RemovesOldProjectTomlPersistence()
    {
        string projectManagerPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Services", "ProjectManager.cs");
        string tomlProjectConfigPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Services", "TomlProjectConfig.cs");
        TestHarness.Assert(!File.Exists(projectManagerPath), "ProjectManager should not exist");
        TestHarness.Assert(!File.Exists(tomlProjectConfigPath), "TomlProjectConfig should not exist");

        string mainWindowPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Views", "MainWindow.axaml");
        string mainWindow = File.ReadAllText(mainWindowPath);
        TestHarness.Assert(!mainWindow.Contains("SaveProjectCommand", StringComparison.Ordinal), "MainWindow should not bind SaveProjectCommand");
        TestHarness.Assert(!mainWindow.Contains("SaveProjectAsCommand", StringComparison.Ordinal), "MainWindow should not bind SaveProjectAsCommand");
        TestHarness.Assert(!mainWindow.Contains("SaveProjectCommand", StringComparison.Ordinal), "MainWindow should not expose the old project save command");
        TestHarness.Assert(!mainWindow.Contains("Ctrl+Shift+S", StringComparison.Ordinal), "MainWindow should not expose Ctrl+Shift+S project save shortcut");
        TestHarness.Assert(!mainWindow.Contains("Terrain Project", StringComparison.Ordinal), "MainWindow should not expose old Terrain Project TOML picker");

        string viewModelPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs");
        string viewModel = File.ReadAllText(viewModelPath);
        TestHarness.Assert(!viewModel.Contains("ProjectManager", StringComparison.Ordinal), "EditorShellViewModel should not reference ProjectManager");
        TestHarness.Assert(!viewModel.Contains("SaveProject", StringComparison.Ordinal), "EditorShellViewModel should not expose old project save commands");

        string terrainManagerPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Services", "TerrainManager.cs");
        string terrainManager = File.ReadAllText(terrainManagerPath);
        TestHarness.Assert(!terrainManager.Contains("TomlProjectConfig", StringComparison.Ordinal), "TerrainManager should not use old TOML project config");
        TestHarness.Assert(!terrainManager.Contains("heightmaps", StringComparison.Ordinal), "TerrainManager should not write old heightmaps project directory");
        TestHarness.Assert(!terrainManager.Contains("splatmaps", StringComparison.Ordinal), "TerrainManager should not write old splatmaps project directory");
    }

    private static void RuntimeBootstrapFailuresAreLoggedAsErrors()
    {
        string processorPath = Path.Combine(RepositoryRoot, "Terrain", "Core", "TerrainProcessor.cs");
        string processor = File.ReadAllText(processorPath);
        TestHarness.Assert(
            processor.Contains("Log.Error($\"Terrain runtime resources could not be read:", StringComparison.Ordinal),
            "TerrainProcessor should log runtime bootstrap failures as errors");
        TestHarness.Assert(
            !processor.Contains("Log.Warning($\"Terrain runtime resources could not be read: {exception.Message}\")", StringComparison.Ordinal),
            "TerrainProcessor should not log runtime bootstrap failures as warnings");
    }

    private static void RuntimeResourceBootstrapUsesGameScopedName()
    {
        string oldPath = Path.Combine(RepositoryRoot, "Terrain", "Resources", "TerrainRuntimeBootstrap.cs");
        string newPath = Path.Combine(RepositoryRoot, "Terrain", "Resources", "GameRuntimeResourceBootstrap.cs");

        TestHarness.Assert(!File.Exists(oldPath), "TerrainRuntimeBootstrap.cs should be renamed to GameRuntimeResourceBootstrap.cs");
        TestHarness.Assert(File.Exists(newPath), "GameRuntimeResourceBootstrap.cs should exist");

        string processorPath = Path.Combine(RepositoryRoot, "Terrain", "Core", "TerrainProcessor.cs");
        string processor = File.ReadAllText(processorPath);
        TestHarness.Assert(processor.Contains("new GameRuntimeResourceBootstrap(", StringComparison.Ordinal), "TerrainProcessor should use GameRuntimeResourceBootstrap");
        TestHarness.Assert(!processor.Contains("new TerrainRuntimeBootstrap(", StringComparison.Ordinal), "TerrainProcessor should not use TerrainRuntimeBootstrap");
    }

    private static void ResourceBootstrapProductionPathsUseTerrainAssemblyDirectory()
    {
        AssertContains("Terrain/Core/TerrainProcessor.cs", "CreateForTerrainAssemblyDirectory()");
        AssertContains("Terrain/Resources/GameResourceResolverBootstrap.cs", "FindFromTerrainAssembly()");
        AssertContains("Terrain.Editor/Services/TerrainManager.cs", "CreateForTerrainAssemblyDirectory()");
        AssertContains("Terrain.Editor/Services/Resources/EditorBootstrapService.cs", "CreateForTerrainAssemblyDirectory()");
        AssertContains("Terrain/Rendering/River/RiverResourceLoader.cs", "FindFromTerrainAssembly()");

        string hostBootstrapCall = "CreateForAppDirectory(" + "AppContext.BaseDirectory)";
        string hostRootLocatorCall = "FindFrom(" + "AppContext.BaseDirectory)";
        foreach (string filePath in EnumerateTextFiles("*.cs"))
        {
            string text = File.ReadAllText(filePath);
            TestHarness.Assert(
                !text.Contains(hostBootstrapCall, StringComparison.Ordinal),
                $"{Relative(filePath)} should not bootstrap resources from the host AppContext.BaseDirectory");
            TestHarness.Assert(
                !text.Contains(hostRootLocatorCall, StringComparison.Ordinal),
                $"{Relative(filePath)} should not locate game resources from the host AppContext.BaseDirectory");
        }
    }

    private static void RuntimeNoLongerRequiresBiomeAuthoringResources()
    {
        AssertContains("Terrain/Resources/GameRuntimeResourceBootstrap.cs", "MaterialDescriptorPath");
        string bootstrap = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain", "Resources", "GameRuntimeResourceBootstrap.cs"));
        TestHarness.Assert(!bootstrap.Contains("BiomeMaskPath", StringComparison.Ordinal), "runtime bootstrap should not resolve biome mask");
        TestHarness.Assert(!bootstrap.Contains("BiomeSettingsPath", StringComparison.Ordinal), "runtime bootstrap should not resolve biome settings");
        TestHarness.Assert(!bootstrap.Contains("RuntimeBiomeSettingsReader.ReadFrom", StringComparison.Ordinal), "runtime bootstrap should not validate biome settings");

        Type bundleType = typeof(TerrainRuntimeResourceBundle);
        TestHarness.Assert(bundleType.GetProperty("BiomeMaskPath") == null, "runtime bundle API should not expose biome mask as a required contract");
        TestHarness.Assert(bundleType.GetProperty("BiomeSettingsPath") == null, "runtime bundle API should not expose biome settings path as a required contract");
        TestHarness.Assert(bundleType.GetProperty("BiomeSettings") == null, "runtime bundle API should not expose biome settings data as a required contract");
    }

    private static void RuntimeMigrationRemovesRuntimeDetailGenerationApi()
    {
        TestHarness.Assert(!File.Exists(Path.Combine(RepositoryRoot, "Terrain", "Materials", "RuntimeDetailMapBuilder.cs")), "runtime detail map builder source should not exist");
        TestHarness.Assert(!File.Exists(Path.Combine(RepositoryRoot, "Terrain", "Materials", "TerrainDetailGeneration.cs")), "runtime terrain detail generation source should not exist");

        AssertTerrainProjectDoesNotContain("RuntimeDetailMapBuilder");
        AssertTerrainProjectDoesNotContain("RuntimeDetailMapData");
        AssertTerrainProjectDoesNotContain("TerrainDetailGenerationContext");
        AssertTerrainProjectDoesNotContain("TerrainDetailMapGenerator");
    }

    private static void RuntimeMigrationRemovesRuntimeBiomeMaskReaderApi()
    {
        TestHarness.Assert(!File.Exists(Path.Combine(RepositoryRoot, "Terrain", "Resources", "RuntimeBiomeMaskReader.cs")), "runtime biome mask reader source should not exist");

        AssertTerrainProjectDoesNotContain("RuntimeBiomeMaskReader");
        AssertTerrainProjectDoesNotContain("RuntimeBiomeMaskData");
    }

    private static void AssertContains(string relativePath, string expected)
    {
        string filePath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(filePath);
        TestHarness.Assert(
            text.Contains(expected, StringComparison.Ordinal),
            $"{relativePath} should contain {expected}");
    }

    private static void AssertTerrainProjectDoesNotContain(string forbidden)
    {
        foreach (string filePath in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "Terrain"), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(filePath);
            TestHarness.Assert(!text.Contains(forbidden, StringComparison.Ordinal), $"{Relative(filePath)} should not expose {forbidden}");
        }
    }

    private static IEnumerable<string> EnumerateTextFiles(string pattern)
    {
        return Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "Terrain"), pattern, SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "Terrain.Editor"), pattern, SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "Terrain.Editor.Tests"), pattern, SearchOption.AllDirectories));
    }

    private static string Relative(string filePath)
    {
        return Path.GetRelativePath(RepositoryRoot, filePath).Replace('\\', '/');
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Terrain.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
