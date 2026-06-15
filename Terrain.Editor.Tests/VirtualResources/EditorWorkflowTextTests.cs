using Terrain.Editor.Tests;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorWorkflowTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("editor workflow has automatic virtual resource bootstrap", HasAutomaticVirtualResourceBootstrap);
        TestHarness.Run("editor workflow exposes save authoring command", ExposesSaveAuthoringCommand);
        TestHarness.Run("editor terrain export uses resolved terrain resource target", TerrainExportUsesResolvedTerrainResourceTarget);
        TestHarness.Run("editor biome remove command does not dereference selected biome in XAML", BiomeRemoveCommandDoesNotDereferenceSelectedBiomeInXaml);
        TestHarness.Run("editor river inspector does not expose manual import or generate actions", RiverInspectorDoesNotExposeManualImportOrGenerateActions);
        TestHarness.Run("editor river inspector does not expose preview bindings", RiverInspectorDoesNotExposePreviewBindings);
        TestHarness.Run("editor wires river services before loading workspace session", WiresRiverServicesBeforeLoadingWorkspaceSession);
        TestHarness.Run("editor save exposes async modal progress state", SaveExposesAsyncModalProgressState);
        TestHarness.Run("main window exposes save progress overlay", MainWindowExposesSaveProgressOverlay);
        TestHarness.Run("viewport input can be blocked during modal save", ViewportInputCanBeBlockedDuringModalSave);
    }

    private static void HasAutomaticVirtualResourceBootstrap()
    {
        string sessionPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Services", "Resources", "EditorResourceSession.cs");
        string bootstrapPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "Services", "Resources", "EditorBootstrapService.cs");
        TestHarness.Assert(File.Exists(sessionPath), "EditorResourceSession should exist");
        TestHarness.Assert(File.Exists(bootstrapPath), "EditorBootstrapService should exist");

        string app = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "App.axaml.cs"));
        TestHarness.Assert(app.Contains("EditorBootstrapService", StringComparison.Ordinal), "App should construct the shell through EditorBootstrapService");

        string viewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs"));
        TestHarness.Assert(viewModel.Contains("EditorResourceSession", StringComparison.Ordinal), "EditorShellViewModel should keep the current resource session");
        TestHarness.Assert(viewModel.Contains("LoadFromResourceSession", StringComparison.Ordinal), "EditorShellViewModel should load terrain from the resource session");
    }

    private static void TerrainExportUsesResolvedTerrainResourceTarget()
    {
        string viewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs"));
        TestHarness.Assert(!viewModel.Contains("SaveFilePickerAsync", StringComparison.Ordinal), "Export Terrain should not prompt for an arbitrary output path");
        TestHarness.Assert(!viewModel.Contains("SuggestedFileName = \"terrain\"", StringComparison.Ordinal), "Export Terrain should not suggest an arbitrary terrain file name");
        TestHarness.Assert(viewModel.Contains("TerrainData", StringComparison.Ordinal), "Export Terrain should use the resolved terrain data resource");
        TestHarness.Assert(viewModel.Contains("ExecuteAsync(\"Terrain\"", StringComparison.Ordinal), "Export Terrain should still use the Terrain exporter");
    }

    private static void ExposesSaveAuthoringCommand()
    {
        string window = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "Views", "MainWindow.axaml"));
        TestHarness.Assert(window.Contains("Ctrl+S", StringComparison.Ordinal), "MainWindow should bind Ctrl+S to save authoring resources");
        TestHarness.Assert(window.Contains("SaveCommand", StringComparison.Ordinal), "MainWindow should expose a Save command binding");

        string viewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs"));
        TestHarness.Assert(viewModel.Contains("SaveAuthoringResources", StringComparison.Ordinal), "EditorShellViewModel should save authoring resources through TerrainManager");
        TestHarness.Assert(viewModel.Contains("Saved authoring resources.", StringComparison.Ordinal), "Save command should log a success message");
    }

    private static void BiomeRemoveCommandDoesNotDereferenceSelectedBiomeInXaml()
    {
        string window = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "Views", "MainWindow.axaml"));
        TestHarness.Assert(
            !window.Contains("CommandParameter=\"{Binding Biome.SelectedBiome.Id}\"", StringComparison.Ordinal),
            "Biome remove button should not dereference SelectedBiome.Id in XAML");

        string viewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "BiomeViewModel.cs"));
        TestHarness.Assert(viewModel.Contains("CanRemoveSelectedBiome", StringComparison.Ordinal), "BiomeViewModel should expose can-remove state for the selected biome");
    }

    private static void RiverInspectorDoesNotExposeManualImportOrGenerateActions()
    {
        string window = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "Views", "MainWindow.axaml"));
        TestHarness.Assert(!window.Contains("River.ImportPngCommand", StringComparison.Ordinal), "River inspector should not expose a manual import command");
        TestHarness.Assert(!window.Contains("River.GenerateCommand", StringComparison.Ordinal), "River inspector should not expose a manual generate command");
    }

    private static void RiverInspectorDoesNotExposePreviewBindings()
    {
        string window = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "Views", "MainWindow.axaml"));
        TestHarness.Assert(!window.Contains("River.PreviewImage", StringComparison.Ordinal), "River inspector should not bind River.PreviewImage");
        TestHarness.Assert(!window.Contains("Classes=\"brushPreviewFrame\" Height=\"120\"", StringComparison.Ordinal), "River inspector should not render the old river preview frame");

        string riverViewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "RiverViewModel.cs"));
        TestHarness.Assert(!riverViewModel.Contains("EnsurePreviewLoaded", StringComparison.Ordinal), "RiverViewModel should not expose preview loading helpers");
        TestHarness.Assert(!riverViewModel.Contains("PreviewImage", StringComparison.Ordinal), "RiverViewModel should not keep preview bitmap state");

        string shellViewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs"));
        TestHarness.Assert(!shellViewModel.Contains("\"Preview generated rivers\"", StringComparison.Ordinal), "River tool metadata should not describe preview behavior");
    }

    private static void WiresRiverServicesBeforeLoadingWorkspaceSession()
    {
        string viewModelPath = Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs");
        string viewModel = File.ReadAllText(viewModelPath);

        int constructorRuntimeCheck = viewModel.IndexOf("if (_viewportHost.HasSceneRuntime)", StringComparison.Ordinal);
        int constructorTryWire = viewModel.IndexOf("TryWireRiverServices();", constructorRuntimeCheck, StringComparison.Ordinal);
        int constructorLoad = viewModel.IndexOf("_ = LoadEditorResourceSessionAsync();", constructorRuntimeCheck, StringComparison.Ordinal);
        TestHarness.Assert(constructorRuntimeCheck >= 0, "EditorShellViewModel should handle the initial runtime-ready path");
        TestHarness.Assert(constructorTryWire >= 0, "Initial runtime-ready path should wire river services");
        TestHarness.Assert(constructorLoad >= 0, "Initial runtime-ready path should load the workspace session");
        TestHarness.Assert(constructorTryWire < constructorLoad, "Initial runtime-ready path should wire river services before loading the workspace session");

        int runtimeStateHandler = viewModel.IndexOf("private void OnViewportRuntimeStateChanged", StringComparison.Ordinal);
        int handlerTryWire = viewModel.IndexOf("TryWireRiverServices();", runtimeStateHandler, StringComparison.Ordinal);
        int handlerLoad = viewModel.IndexOf("_ = LoadEditorResourceSessionAsync();", runtimeStateHandler, StringComparison.Ordinal);
        TestHarness.Assert(runtimeStateHandler >= 0, "EditorShellViewModel should handle runtime state changes");
        TestHarness.Assert(handlerTryWire >= 0, "Runtime state change handler should wire river services");
        TestHarness.Assert(handlerLoad >= 0, "Runtime state change handler should load the workspace session");
        TestHarness.Assert(handlerTryWire < handlerLoad, "Runtime state change handler should wire river services before loading the workspace session");
    }

    private static void SaveExposesAsyncModalProgressState()
    {
        string viewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs"));
        TestHarness.Assert(viewModel.Contains("SaveCommand", StringComparison.Ordinal), "EditorShellViewModel should expose the save command");
        TestHarness.Assert(viewModel.Contains("IsSaving", StringComparison.Ordinal), "EditorShellViewModel should expose IsSaving");
        TestHarness.Assert(viewModel.Contains("SaveProgressMessage", StringComparison.Ordinal), "EditorShellViewModel should expose save progress text");
        TestHarness.Assert(viewModel.Contains("SaveProgressPercent", StringComparison.Ordinal), "EditorShellViewModel should expose save progress percent");
        TestHarness.Assert(viewModel.Contains("CanRunMutatingCommand", StringComparison.Ordinal), "mutating commands should share a save gate");

        string saveBody = ExtractMethodBody(viewModel, "private async Task Save");
        TestHarness.Assert(saveBody.Contains("Progress<AuthoringSaveProgress>", StringComparison.Ordinal), "Save should report authoring save progress");
        TestHarness.Assert(saveBody.Contains("Task.Run(", StringComparison.Ordinal), "Save should run authoring writes off the UI thread");
        TestHarness.Assert(saveBody.Contains("SaveAuthoringResources", StringComparison.Ordinal), "Save should write authoring resources");
        TestHarness.Assert(
            saveBody.Contains("IsSaving = true", StringComparison.Ordinal) || saveBody.Contains("BeginSaveProgress()", StringComparison.Ordinal),
            "Save should enter saving state before authoring writes");
        TestHarness.Assert(
            saveBody.Contains("IsSaving = false", StringComparison.Ordinal) || saveBody.Contains("EndSaveProgress()", StringComparison.Ordinal),
            "Save should leave saving state after authoring writes");

        string savingChangedBody = ExtractMethodBody(viewModel, "partial void OnIsSavingChanged(bool value)");
        TestHarness.Assert(savingChangedBody.Contains("_viewportHost.SetInputBlocked(value)", StringComparison.Ordinal), "Save state changes should block viewport input");
    }

    private static void MainWindowExposesSaveProgressOverlay()
    {
        string window = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "Views", "MainWindow.axaml"));
        TestHarness.Assert(window.Contains("IsEditorInteractionEnabled", StringComparison.Ordinal), "MainWindow should disable editor interaction while saving");
        TestHarness.Assert(window.Contains("Saving authoring resources", StringComparison.Ordinal), "MainWindow should show a save progress title");
        TestHarness.Assert(window.Contains("SaveProgressMessage", StringComparison.Ordinal), "MainWindow should bind save progress text");
        TestHarness.Assert(window.Contains("SaveProgressPercent", StringComparison.Ordinal), "MainWindow should bind save progress percent");
    }

    private static void ViewportInputCanBeBlockedDuringModalSave()
    {
        string host = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "Rendering", "NativeViewport", "NativeStrideViewportHost.cs"));
        string game = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "Rendering", "NativeViewport", "EmbeddedStrideViewportGame.cs"));
        TestHarness.Assert(host.Contains("SetInputBlocked(bool blocked)", StringComparison.Ordinal), "NativeStrideViewportHost should expose input blocking");
        string hostBlockBody = ExtractMethodBody(host, "public void SetInputBlocked(bool blocked)");
        TestHarness.Assert(hostBlockBody.Contains("_game.IsInputBlocked = blocked", StringComparison.Ordinal), "NativeStrideViewportHost should write the requested input blocking state to the game");
        TestHarness.Assert(game.Contains("public bool IsInputBlocked", StringComparison.Ordinal), "EmbeddedStrideViewportGame should expose input blocking");
        string cameraBody = ExtractMethodBody(game, "private void UpdateCamera");
        TestHarness.Assert(cameraBody.Contains("if (IsInputBlocked)", StringComparison.Ordinal), "UpdateCamera should branch on blocked input");
        TestHarness.Assert(cameraBody.Contains("ReleaseCameraControl()", StringComparison.Ordinal), "blocked viewport input should release camera control from UpdateCamera");
        string brushBody = ExtractMethodBody(game, "private void UpdateBrush");
        TestHarness.Assert(brushBody.Contains("if (IsInputBlocked)", StringComparison.Ordinal), "UpdateBrush should branch on blocked input");
        TestHarness.Assert(brushBody.Contains("EndBrushStrokeIfNeeded()", StringComparison.Ordinal), "blocked viewport input should end active brush strokes from UpdateBrush");
        TestHarness.Assert(brushBody.Contains("UpdateBrushDecalVisibility(visible: false)", StringComparison.Ordinal), "blocked viewport input should hide the brush decal from UpdateBrush");
    }

    private static string ExtractMethodBody(string source, string marker)
    {
        int searchIndex = 0;
        while (true)
        {
            int markerIndex = source.IndexOf(marker, searchIndex, StringComparison.Ordinal);
            TestHarness.Assert(markerIndex >= 0, $"marker should exist: {marker}");

            int openBrace = source.IndexOf('{', markerIndex);
            TestHarness.Assert(openBrace >= 0, $"opening brace should exist after marker: {marker}");

            int semicolon = source.IndexOf(';', markerIndex);
            if (semicolon >= 0 && semicolon < openBrace)
            {
                searchIndex = semicolon + 1;
                continue;
            }

            int depth = 0;
            for (int i = openBrace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(openBrace + 1, i - openBrace - 1);
                }
            }

            throw new InvalidOperationException($"closing brace should exist after marker: {marker}");
        }
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
