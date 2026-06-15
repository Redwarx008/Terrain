# Editor Save Progress Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a responsive modal progress flow to Terrain Editor authoring Save so large resource writes no longer look like a frozen process.

**Architecture:** Save remains a transactional authoring-resource write, but the shell moves the expensive file work off the Avalonia UI thread and reports staged progress back to the UI. The modal state disables mutating commands and blocks Stride viewport input through a small host-to-game flag instead of relying on Avalonia visual occlusion over the native HWND.

**Tech Stack:** C#/.NET, Avalonia, CommunityToolkit.Mvvm source-generated relay commands, Stride SDL native viewport, ImageSharp PNG writers, existing `Terrain.Editor.Tests` console test harness.

---

## File Structure

- Create: `Terrain.Editor/Services/Resources/AuthoringSaveProgress.cs`  
  Owns save-specific progress payloads and factory helpers.
- Modify: `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`  
  Reports validation, staged writer, and commit phases while preserving `AtomicResourceWriteTransaction`.
- Modify: `Terrain.Editor/Services/TerrainManager.cs`  
  Accepts optional save progress and reports the authoring snapshot preparation phase before delegating to the save service.
- Modify: `Terrain.Editor/ViewModels/EditorShellViewModel.cs`  
  Converts Save to async, exposes modal progress properties, gates mutating commands, and toggles viewport input blocking.
- Modify: `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`  
  Exposes `SetInputBlocked(bool)` for the shell.
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`  
  Ignores camera and brush input while modal saving is active.
- Modify: `Terrain.Editor/Views/MainWindow.axaml`  
  Wraps the editor in an interaction-disabled root and adds the save progress modal overlay.
- Modify: `Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs`  
  Adds progress-order tests for successful and failing saves.
- Modify: `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`  
  Adds workflow tests for async save state, progress UI bindings, and viewport input blocking.
- Modify: `docs/ARCHITECTURE_OVERVIEW.md` and `docs/CURRENT_FEATURES.md`  
  Records the modal async Save behavior.
- Create: `docs/log/2026/06/15/2026-06-15-editor-save-progress.md`  
  Records the session outcome and verification commands.

---

### Task 1: Add Save Progress Tests

**Files:**
- Modify: `Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs`

- [ ] **Step 1: Add failing progress tests**

Replace `RunAll()` and add the helper/progress/test methods below inside `EditorResourceSaveServiceTests`.

```csharp
public static void RunAll()
{
    TestHarness.Run("authoring save reports progress in expected order", AuthoringSaveReportsProgressInExpectedOrder);
    TestHarness.Run("authoring save reports failing writer before rollback", AuthoringSaveReportsFailingWriterBeforeRollback);
    TestHarness.Run("authoring save rolls back earlier files when a later writer fails", AuthoringSaveRollsBackEarlierFilesWhenLaterWriterFails);
}

private static void AuthoringSaveReportsProgressInExpectedOrder()
{
    string root = CreateWorkspace();
    string mapDefinitionPath = Path.Combine(root, "mod", "map_data", "default.toml");
    string heightmapPath = Path.Combine(root, "mod", "map_data", "heightmap.png");
    string biomeMaskPath = Path.Combine(root, "mod", "map_data", "biome_mask.png");
    string biomeSettingsPath = Path.Combine(root, "mod", "map_data", "biome_settings.toml");
    string materialDescriptorPath = Path.Combine(root, "mod", "map_data", "materials", "descriptor.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(materialDescriptorPath)!);

    EditorResourceSession session = CreateSession(
        root,
        mapDefinitionPath,
        heightmapPath,
        biomeMaskPath,
        biomeSettingsPath,
        materialDescriptorPath);

    var biomeMask = new BiomeMask(2, 2);
    biomeMask.SetValue(0, 0, 1);
    biomeMask.SetValue(1, 1, 2);
    var progress = new CapturingSaveProgress();

    EditorResourceSaveService.Save(
        session,
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

    AssertProgressMessages(
        progress.Messages,
        [
            "Validating save targets...",
            "Writing map definition...",
            "Writing heightmap PNG...",
            "Writing biome mask PNG...",
            "Writing material descriptor...",
            "Writing biome settings...",
            "Committing staged resources...",
        ]);
}

private static void AuthoringSaveReportsFailingWriterBeforeRollback()
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

    var biomeMask = new BiomeMask(2, 2);
    var progress = new CapturingSaveProgress();

    TestHarness.AssertThrows<InvalidDataException>(
        () => EditorResourceSaveService.Save(
            session,
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

    TestHarness.Assert(
        progress.Messages.Contains("Writing material descriptor..."),
        "progress should report the failing material descriptor writer");
    TestHarness.Assert(
        !progress.Messages.Contains("Committing staged resources..."),
        "progress should not report commit after a writer fails");
    TestHarness.AssertEqual("original-default", File.ReadAllText(mapDefinitionPath), "map definition should roll back");
    TestHarness.AssertEqual("original-heightmap", File.ReadAllText(heightmapPath), "heightmap should roll back");
    TestHarness.AssertEqual("original-biome-mask", File.ReadAllText(biomeMaskPath), "biome mask should roll back");
    TestHarness.AssertEqual("original-biome-settings", File.ReadAllText(biomeSettingsPath), "biome settings should roll back");
    TestHarness.AssertEqual("original-material-descriptor", File.ReadAllText(materialDescriptorPath), "material descriptor should roll back");
}

private static void AssertProgressMessages(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
{
    TestHarness.AssertEqual(expected.Count, actual.Count, "progress message count");
    for (int i = 0; i < expected.Count; i++)
        TestHarness.AssertEqual(expected[i], actual[i], $"progress message {i}");
}

private sealed class CapturingSaveProgress : IProgress<AuthoringSaveProgress>
{
    public List<string> Messages { get; } = [];

    public void Report(AuthoringSaveProgress value)
    {
        if (!string.IsNullOrWhiteSpace(value.Message))
            Messages.Add(value.Message);
    }
}
```

- [ ] **Step 2: Run the test build to verify the red state**

Run:

```powershell
dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj
```

Expected: build fails with `CS0246` for `AuthoringSaveProgress` and `CS1739` or `CS1501` because `EditorResourceSaveService.Save` does not accept `progress`.

- [ ] **Step 3: Commit the red test**

```powershell
git add Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs
git commit -m "test: cover authoring save progress reports"
```

---

### Task 2: Implement Save Progress Reporting

**Files:**
- Create: `Terrain.Editor/Services/Resources/AuthoringSaveProgress.cs`
- Modify: `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`
- Modify: `Terrain.Editor/Services/TerrainManager.cs`

- [ ] **Step 1: Add `AuthoringSaveProgress`**

Create `Terrain.Editor/Services/Resources/AuthoringSaveProgress.cs`:

```csharp
#nullable enable

namespace Terrain.Editor.Services.Resources;

public struct AuthoringSaveProgress
{
    public int Current;
    public int Total;
    public string Message;
    public bool IsCompleted;
    public string? ErrorMessage;

    public static AuthoringSaveProgress Running(int current, int total, string message) => new()
    {
        Current = current,
        Total = total,
        Message = message,
        IsCompleted = false,
        ErrorMessage = null,
    };

    public static AuthoringSaveProgress Completed(int current, int total, string message = "Save completed.") => new()
    {
        Current = current,
        Total = total,
        Message = message,
        IsCompleted = true,
        ErrorMessage = null,
    };

    public static AuthoringSaveProgress Failed(int current, int total, string error) => new()
    {
        Current = current,
        Total = total,
        Message = error,
        IsCompleted = true,
        ErrorMessage = error,
    };
}
```

- [ ] **Step 2: Add progress reports to `EditorResourceSaveService.Save`**

Change the method signature and body in `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs` to this shape:

```csharp
public static void Save(
    EditorResourceSession session,
    ushort[] heightData,
    int width,
    int height,
    BiomeMask biomeMask,
    float heightScale,
    IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots,
    EditorBiomeSettingsSnapshot biomeSnapshot,
    IProgress<AuthoringSaveProgress>? progress = null)
{
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(heightData);
    ArgumentNullException.ThrowIfNull(biomeMask);
    ArgumentNullException.ThrowIfNull(descriptorSlots);
    ArgumentNullException.ThrowIfNull(biomeSnapshot);

    const int total = 9;
    progress?.Report(AuthoringSaveProgress.Running(2, total, "Validating save targets..."));

    EnsureWritable(session.MapDefinition, "Map definition");
    EnsureWritable(session.Heightmap, "Heightmap");
    EnsureWritable(session.BiomeMask, "Biome mask");
    EnsureWritable(session.MaterialDescriptor, "Material descriptor");
    EnsureWritable(session.BiomeSettings, "Biome settings");

    var mapDefinition = new RuntimeMapDefinition
    {
        HeightmapPath = session.MapDefinitionModel.HeightmapPath,
        TerrainDataPath = session.MapDefinitionModel.TerrainDataPath,
        RiversPath = session.MapDefinitionModel.RiversPath,
        ProvincesPath = session.MapDefinitionModel.ProvincesPath,
        HeightScale = heightScale,
    };

    var mapDefinitionWriter = new MapDefinitionWriter();
    var heightmapWriter = new HeightmapWriter();
    var biomeMaskWriter = new BiomeMaskWriter();
    var materialDescriptorWriter = new MaterialDescriptorWriter();
    var biomeSettingsWriter = new BiomeSettingsWriter();

    using var transaction = new AtomicResourceWriteTransaction();
    string stagedMapDefinition = transaction.CreateStagingPath(session.MapDefinition.ResolvedPath);
    string stagedHeightmap = transaction.CreateStagingPath(session.Heightmap.ResolvedPath);
    string stagedBiomeMask = transaction.CreateStagingPath(session.BiomeMask.ResolvedPath);
    string stagedMaterialDescriptor = transaction.CreateStagingPath(session.MaterialDescriptor.ResolvedPath);
    string stagedBiomeSettings = transaction.CreateStagingPath(session.BiomeSettings.ResolvedPath);

    progress?.Report(AuthoringSaveProgress.Running(3, total, "Writing map definition..."));
    mapDefinitionWriter.Write(stagedMapDefinition, mapDefinition);

    progress?.Report(AuthoringSaveProgress.Running(4, total, "Writing heightmap PNG..."));
    heightmapWriter.Write(stagedHeightmap, heightData, width, height);

    progress?.Report(AuthoringSaveProgress.Running(5, total, "Writing biome mask PNG..."));
    biomeMaskWriter.Write(stagedBiomeMask, biomeMask);

    progress?.Report(AuthoringSaveProgress.Running(6, total, "Writing material descriptor..."));
    materialDescriptorWriter.Write(stagedMaterialDescriptor, descriptorSlots);

    progress?.Report(AuthoringSaveProgress.Running(7, total, "Writing biome settings..."));
    biomeSettingsWriter.Write(stagedBiomeSettings, biomeSnapshot.Biomes, biomeSnapshot.Layers, biomeSnapshot.Modifiers);

    progress?.Report(AuthoringSaveProgress.Running(8, total, "Committing staged resources..."));
    transaction.Commit();
}
```

- [ ] **Step 3: Add the optional progress argument to `TerrainManager.SaveAuthoringResources`**

Replace the method in `Terrain.Editor/Services/TerrainManager.cs` with:

```csharp
public void SaveAuthoringResources(
    EditorResourceSession session,
    IProgress<AuthoringSaveProgress>? progress = null)
{
    if (session == null)
        throw new ArgumentNullException(nameof(session));
    if (heightDataCache == null || heightDataWidth <= 0 || heightDataHeight <= 0)
        throw new InvalidOperationException("Heightmap data is not loaded.");
    if (BiomeMask == null)
        throw new InvalidOperationException("Biome mask data is not loaded.");

    const int total = 9;
    progress?.Report(AuthoringSaveProgress.Running(1, total, "Preparing authoring data..."));

    IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots =
        EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots(MaterialSlotManager.Instance.GetActiveSlots().ToArray());
    var materialIdsByIndex = descriptorSlots.ToDictionary(
        static slot => slot.Index,
        static slot => slot.Id);
    EditorBiomeSettingsSnapshot biomeSnapshot =
        EditorAuthoringResourceMapper.CreateBiomeSettingsSnapshot(BiomeRuleService.Instance, materialIdsByIndex);
    EditorResourceSaveService.Save(
        session,
        heightDataCache,
        heightDataWidth,
        heightDataHeight,
        BiomeMask,
        HeightScale,
        descriptorSlots,
        biomeSnapshot,
        progress);
}
```

- [ ] **Step 4: Run the service tests**

Run:

```powershell
dotnet build Terrain.sln
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build
```

Expected: build succeeds; the two new authoring save progress tests pass; existing rollback test still passes.

- [ ] **Step 5: Commit the green implementation**

```powershell
git add Terrain.Editor/Services/Resources/AuthoringSaveProgress.cs Terrain.Editor/Services/Resources/EditorResourceSaveService.cs Terrain.Editor/Services/TerrainManager.cs
git commit -m "feat: report authoring save progress"
```

---

### Task 3: Add Workflow Red Tests for Modal Save

**Files:**
- Modify: `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`

- [ ] **Step 1: Add text tests for modal save and viewport input blocking**

Add these `RunAll()` entries:

```csharp
TestHarness.Run("editor save exposes async modal progress state", SaveExposesAsyncModalProgressState);
TestHarness.Run("main window exposes save progress overlay", MainWindowExposesSaveProgressOverlay);
TestHarness.Run("viewport input can be blocked during modal save", ViewportInputCanBeBlockedDuringModalSave);
```

Add these methods inside `EditorWorkflowTextTests`:

```csharp
private static void SaveExposesAsyncModalProgressState()
{
    string viewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs"));
    TestHarness.Assert(viewModel.Contains("private async Task Save()", StringComparison.Ordinal), "Save command should use an async method");
    TestHarness.Assert(viewModel.Contains("IsSaving", StringComparison.Ordinal), "EditorShellViewModel should expose IsSaving");
    TestHarness.Assert(viewModel.Contains("SaveProgressMessage", StringComparison.Ordinal), "EditorShellViewModel should expose save progress text");
    TestHarness.Assert(viewModel.Contains("SaveProgressPercent", StringComparison.Ordinal), "EditorShellViewModel should expose save progress percent");
    TestHarness.Assert(viewModel.Contains("CanRunMutatingCommand", StringComparison.Ordinal), "mutating commands should share a save gate");
    TestHarness.Assert(viewModel.Contains("Task.Run(() => terrainManager.SaveAuthoringResources(session, progress))", StringComparison.Ordinal), "Save should run authoring writes off the UI thread");
    TestHarness.Assert(viewModel.Contains("_viewportHost.SetInputBlocked(value)", StringComparison.Ordinal), "Save state should block viewport input");
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
    TestHarness.Assert(game.Contains("public bool IsInputBlocked", StringComparison.Ordinal), "EmbeddedStrideViewportGame should expose input blocking");
    TestHarness.Assert(game.Contains("if (IsInputBlocked)", StringComparison.Ordinal), "viewport update paths should check input blocking");
}
```

- [ ] **Step 2: Run tests to verify the red state**

Run:

```powershell
dotnet build Terrain.sln
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build
```

Expected: build succeeds; the new workflow tests fail because the async Save state, XAML overlay, and viewport input block are not implemented yet.

- [ ] **Step 3: Commit the red workflow tests**

```powershell
git add Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs
git commit -m "test: cover modal save workflow"
```

---

### Task 4: Implement Async Modal Save and Input Lock

**Files:**
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- Modify: `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`
- Modify: `Terrain.Editor/ViewModels/EditorShellViewModel.cs`
- Modify: `Terrain.Editor/Views/MainWindow.axaml`

- [ ] **Step 1: Add input blocking to `EmbeddedStrideViewportGame`**

Add this property near the other public host-facing properties:

```csharp
public bool IsInputBlocked { get; set; }
```

Add this helper near `UpdateCamera`:

```csharp
private void ReleaseCameraControl()
{
    if (!_isControllingCamera)
        return;

    if (Input.HasMouse && Input.IsMousePositionLocked)
        Input.UnlockMousePosition();
    if (Window != null)
        Window.IsMouseVisible = true;
    SetChildWindowStyle?.Invoke(true);
    _preferPhysicalKeyboardState = false;
    _isControllingCamera = false;
}
```

At the start of `UpdateCamera(float deltaTime)`, before reading mouse state, insert:

```csharp
if (IsInputBlocked)
{
    ReleaseCameraControl();
    return;
}
```

At the start of `UpdateBrush(float deltaTime)`, before terrain/tool checks, insert:

```csharp
if (IsInputBlocked)
{
    EndBrushStrokeIfNeeded();
    _wasLeftMouseDown = false;
    UpdateBrushDecalVisibility(visible: false);
    return;
}
```

Replace the camera cleanup block in `EndRun()` with:

```csharp
ReleaseCameraControl();
```

- [ ] **Step 2: Expose input blocking through `NativeStrideViewportHost`**

Add this method near `FocusRuntimeWindow()` in `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`:

```csharp
public void SetInputBlocked(bool blocked)
{
    if (_game == null)
        return;

    _game.IsInputBlocked = blocked;
}
```

- [ ] **Step 3: Add save modal state to `EditorShellViewModel`**

Add these observable fields after `_isInspectorVisible`:

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsEditorInteractionEnabled))]
private bool _isSaving;

[ObservableProperty]
private int _saveProgressCurrent;

[ObservableProperty]
private int _saveProgressTotal = 9;

[ObservableProperty]
private double _saveProgressPercent;

[ObservableProperty]
private string _saveProgressMessage = string.Empty;
```

Add this computed property near `IsListView`:

```csharp
public bool IsEditorInteractionEnabled => !IsSaving;
```

Add these helpers near the existing command methods:

```csharp
private bool CanRunMutatingCommand()
{
    return !IsSaving;
}

private void BeginSaveProgress()
{
    SaveProgressCurrent = 0;
    SaveProgressTotal = 9;
    SaveProgressPercent = 0;
    SaveProgressMessage = "Starting save...";
    IsSaving = true;
}

private void EndSaveProgress()
{
    IsSaving = false;
}

private void UpdateSaveProgress(AuthoringSaveProgress report)
{
    int total = Math.Max(1, report.Total);
    int current = Math.Clamp(report.Current, 0, total);
    SaveProgressCurrent = current;
    SaveProgressTotal = total;
    SaveProgressPercent = current * 100.0 / total;

    if (!string.IsNullOrWhiteSpace(report.Message))
        SaveProgressMessage = report.Message;
}

private void NotifyMutatingCommandsCanExecuteChanged()
{
    SaveCommand.NotifyCanExecuteChanged();
    ExportTerrainCommand.NotifyCanExecuteChanged();
    ImportAssetsCommand.NotifyCanExecuteChanged();
    AddAssetForCategoryCommand.NotifyCanExecuteChanged();
    DeleteAssetItemCommand.NotifyCanExecuteChanged();
    ImportSelectedNormalCommand.NotifyCanExecuteChanged();
    ClearSelectedMaterialSlotCommand.NotifyCanExecuteChanged();
}
```

Add this generated-property hook near the existing partial property hooks:

```csharp
partial void OnIsSavingChanged(bool value)
{
    _viewportHost.SetInputBlocked(value);
    RefreshHistoryState();
    NotifyMutatingCommandsCanExecuteChanged();
}
```

- [ ] **Step 4: Convert `Save` to async and gate mutating commands**

Change these command attributes:

```csharp
[RelayCommand(CanExecute = nameof(CanRunMutatingCommand))]
```

Apply that attribute to `Save`, `ExportTerrain`, `ImportAssets`, `AddAssetForCategory`, `ImportSelectedNormal`, and `ClearSelectedMaterialSlot`.

Replace `Save()` with:

```csharp
[RelayCommand(CanExecute = nameof(CanRunMutatingCommand))]
private async Task Save()
{
    if (!TryGetTerrainManager(out var terrainManager))
        return;

    if (_resourceSession == null)
    {
        AddConsole("Warning", "Terrain workspace is not loaded.");
        return;
    }

    if (!_resourceSession.CanSaveAuthoringResources)
    {
        if (_resourceSession.HasPendingHeightmap)
            AddConsole("Warning", "Terrain workspace is waiting for a heightmap before save/export.");
        else
            AddConsole("Warning", "Terrain workspace has missing material declarations. Fix descriptor material ids before save/export.");
        return;
    }

    if (!terrainManager.HasTerrainLoaded)
    {
        AddConsole("Warning", "No terrain loaded to save.");
        return;
    }

    EditorResourceSession session = _resourceSession;
    BeginSaveProgress();

    try
    {
        var progress = new Progress<AuthoringSaveProgress>(UpdateSaveProgress);
        await Task.Run(() => terrainManager.SaveAuthoringResources(session, progress));

        UpdateSaveProgress(AuthoringSaveProgress.Running(9, 9, "Refreshing editor state..."));
        _resourceSession = _bootstrapService.LoadCurrentSession();
        EditorDirtyState.Instance.ClearDirty();
        RefreshAssetItems();
        Biome.NotifyMaterialPreviewsChanged();
        RefreshProjectState();
        UpdateSaveProgress(AuthoringSaveProgress.Completed(9, 9));
        AddConsole("Info", "Saved authoring resources.");
    }
    catch (Exception exception)
    {
        AddConsole("Error", $"Save failed: {exception.Message}");
    }
    finally
    {
        EndSaveProgress();
    }
}
```

Change `CanDeleteAssetItem` and `RefreshHistoryState` to:

```csharp
private bool CanDeleteAssetItem(AssetBrowserItemViewModel? item)
{
    return !IsSaving && item is not null && item.MaterialSlotIndex >= 0 && !item.IsCreateItem;
}

private void RefreshHistoryState()
{
    CanUndo = !IsSaving && _historyManager.CanUndo;
    CanRedo = !IsSaving && _historyManager.CanRedo;
    UndoLabel = _historyManager.UndoDescription is { Length: > 0 } undo ? $"Undo {undo}" : "Undo";
    RedoLabel = _historyManager.RedoDescription is { Length: > 0 } redo ? $"Redo {redo}" : "Redo";
}
```

- [ ] **Step 5: Add the modal progress overlay to `MainWindow.axaml`**

Replace the root content opener:

```xml
<DockPanel LastChildFill="True">
```

with:

```xml
<Panel>
  <DockPanel LastChildFill="True" IsEnabled="{Binding IsEditorInteractionEnabled}">
```

Before the final closing content tag, close the `DockPanel` and add this overlay:

```xml
  </DockPanel>

  <Border
    IsVisible="{Binding IsSaving}"
    Background="#CCF5F5F5"
    HorizontalAlignment="Stretch"
    VerticalAlignment="Stretch"
    IsHitTestVisible="True">
    <Border
      Width="380"
      MinHeight="138"
      Padding="18"
      CornerRadius="4"
      Background="{DynamicResource EditorPanelBackgroundBrush}"
      BorderBrush="{DynamicResource EditorPanelBorderStrongBrush}"
      BorderThickness="1"
      HorizontalAlignment="Center"
      VerticalAlignment="Center">
      <StackPanel Spacing="10">
        <TextBlock
          Text="Saving authoring resources"
          FontSize="15"
          FontWeight="SemiBold"
          Foreground="{DynamicResource EditorTextPrimaryBrush}" />
        <TextBlock
          Text="{Binding SaveProgressMessage}"
          FontSize="12"
          TextWrapping="Wrap"
          Foreground="{DynamicResource EditorTextSecondaryBrush}" />
        <ProgressBar
          Minimum="0"
          Maximum="100"
          Value="{Binding SaveProgressPercent}"
          Height="8" />
        <Grid ColumnDefinitions="*,Auto">
          <TextBlock
            Text="{Binding SaveProgressCurrent, StringFormat='{}{0}'}"
            FontSize="11"
            Foreground="{DynamicResource EditorTextSecondaryBrush}" />
          <TextBlock
            Grid.Column="1"
            Text="{Binding SaveProgressPercent, StringFormat='{}{0:F0}%'}"
            FontSize="11"
            Foreground="{DynamicResource EditorTextSecondaryBrush}" />
        </Grid>
      </StackPanel>
    </Border>
  </Border>
</Panel>
```

The final file should have `<Panel>` as the single root content child under `<Window>`, with the original editor `DockPanel` as its first child and the progress overlay as its second child.

- [ ] **Step 6: Run modal workflow tests**

Run:

```powershell
dotnet build Terrain.sln
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build
```

Expected: build succeeds; the modal save workflow tests pass; existing save, export, river, and resource tests still pass.

- [ ] **Step 7: Commit the modal save implementation**

```powershell
git add Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs Terrain.Editor/ViewModels/EditorShellViewModel.cs Terrain.Editor/Views/MainWindow.axaml
git commit -m "feat: add modal authoring save progress"
```

---

### Task 5: Update Docs and Session Log

**Files:**
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Create: `docs/log/2026/06/15/2026-06-15-editor-save-progress.md`

- [ ] **Step 1: Update feature docs**

In `docs/CURRENT_FEATURES.md`, update the `Save 作者态资源` row so its description includes:

```markdown
Save 通过异步模态进度写回作者态资源；保存期间禁用可变更命令并阻止 Stride 视口输入，避免大图 PNG 编码时误判为进程卡死。
```

In `docs/ARCHITECTURE_OVERVIEW.md`, update the Editor key-file table or Editor state summary for `Terrain.Editor/ViewModels/EditorShellViewModel.cs` so it includes:

```markdown
`Save` 使用 `AuthoringSaveProgress` 显示模态进度，并在后台执行作者态资源写回；保存期间通过 `NativeStrideViewportHost.SetInputBlocked` 阻断视口输入。
```

- [ ] **Step 2: Add the session log**

Create `docs/log/2026/06/15/2026-06-15-editor-save-progress.md`:

```markdown
# Editor Save Progress
**Date**: 2026-06-15
**Session**: Save Progress
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 为 Editor 作者态 Save 添加模态进度反馈，避免大图保存时看起来像进程卡住。

**Secondary Objectives:**
- 保存期间禁用可变更命令。
- 保存期间阻断 Stride 原生视口输入。
- 保留作者态资源事务写回和失败回滚语义。

**Success Criteria:**
- Save 运行在异步 UI 流程中，文件写入不阻塞 Avalonia 进度刷新。
- 用户能看到保存阶段、进度条和百分比。
- 保存失败仍保留 dirty 状态并记录错误。
- 测试覆盖保存进度报告和模态工作流绑定。

---

## Context & Background

**Previous Work:**
- Related: [2026-06-15-editor-save-progress-design.md](../../../superpowers/specs/2026-06-15-editor-save-progress-design.md)
- Related: [2026-06-15-editor-save-progress.md](../../../superpowers/plans/2026-06-15-editor-save-progress.md)

**Current State:**
- Save 写回 `default.toml`、`heightmap.png`、`biome_mask.png`、`materials/descriptor.toml`、`biome_settings.toml`。
- `heightmap.png` 和 `biome_mask.png` 的 PNG 编码可能耗时较长。

**Why Now:**
- 同步 Save 会让 UI 看起来卡死，用户需要明确进度反馈。

---

## What We Did

### 1. 保存进度模型与服务报告
**Files Changed:** `Terrain.Editor/Services/Resources/AuthoringSaveProgress.cs`, `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`, `Terrain.Editor/Services/TerrainManager.cs`

**Implementation:**
- 新增 `AuthoringSaveProgress`。
- `TerrainManager.SaveAuthoringResources` 报告作者态数据准备阶段。
- `EditorResourceSaveService.Save` 报告校验、各 writer 写入和事务提交阶段。

**Rationale:**
- 使用保存专用进度类型，避免污染导出语义。

### 2. 异步模态 Save 与输入锁定
**Files Changed:** `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Views/MainWindow.axaml`, `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`, `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

**Implementation:**
- `SaveCommand` 改为异步路径并通过 `Task.Run` 执行作者态资源写回。
- Shell 暴露 `IsSaving`、保存进度文本和百分比。
- 保存期间禁用可变更命令。
- `NativeStrideViewportHost.SetInputBlocked` 将输入锁定传递到 Stride game，camera/brush 更新在保存期间退出。

**Rationale:**
- Avalonia overlay 不可靠覆盖 native HWND，因此用 UI 禁用和 viewport 输入锁双层保障。

### 3. 测试与文档
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs`, `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- 增加保存进度顺序测试。
- 增加模态 Save 工作流文本测试。
- 同步架构与功能状态文档。

---

## Decisions Made

### Decision 1: 第一版不提供取消
**Context:** PNG 编码和事务提交中途取消需要更明确的回滚契约。
**Options Considered:**
1. 立即支持取消
2. 第一版只显示进度并锁定输入

**Decision:** Chose Option 2
**Rationale:** 当前问题是保存看起来卡死；锁定输入和进度反馈能解决该问题，取消可以在独立变更中设计。
**Trade-offs:** 长保存不能中途取消。

### Decision 2: viewport 输入锁不依赖 Avalonia 遮罩
**Context:** 编辑器视口是 native HWND，Avalonia overlay 不一定能真正覆盖它。
**Options Considered:**
1. 只显示 Avalonia overlay
2. Overlay + 禁用命令 + Stride 输入锁

**Decision:** Chose Option 2
**Rationale:** 保证保存期间 camera 和 brush 输入不会继续修改状态。
**Trade-offs:** 多改两个 viewport 文件。

---

## Code Quality Notes

### Testing
- **Tests Written:** 保存进度顺序、失败 writer progress、模态 Save 绑定、viewport 输入锁文本测试
- **Verification:**
  - `dotnet build Terrain.sln`
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build`

### Technical Debt
- 保存进度目前是文件级阶段，不含 PNG 编码内部逐行进度。
- 保存取消仍需独立设计。

---

## Next Session

### Immediate Next Steps
1. 手动验证大图保存时进度条刷新。
2. 如用户需要，单独设计 Save cancel 行为。

### Docs to Read Before Next Session
- [2026-06-15-editor-save-progress-design.md](../../../superpowers/specs/2026-06-15-editor-save-progress-design.md)
- [2026-06-15-editor-save-progress.md](../../../superpowers/plans/2026-06-15-editor-save-progress.md)

---

## Session Statistics

**Files Changed:** 10
**Commits:** 4

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Save progress uses `AuthoringSaveProgress`, not `ExportProgress`.
- Save is modal: mutating commands are disabled and viewport input is blocked.
- `AtomicResourceWriteTransaction` remains the rollback boundary.

**What Changed Since Last Doc Read:**
- Editor Save no longer runs as a synchronous UI-thread command.

**Gotchas for Next Session:**
- Native HWND content may remain visually visible under Avalonia overlay; input blocking is the functional safeguard.
- Fresh `dotnet run` may still hit existing Stride assembly processor behavior; build first, then run tests with `--no-build`.
```

- [ ] **Step 3: Run full verification**

Run:

```powershell
dotnet build Terrain.sln
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build
git status --short
```

Expected: build succeeds; tests pass; `git status --short` lists only intended docs before commit.

- [ ] **Step 4: Commit docs and session log**

```powershell
git add docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md docs/log/2026/06/15/2026-06-15-editor-save-progress.md
git commit -m "docs: record editor save progress workflow"
```

---

## Self-Review Notes

- Spec coverage: Tasks cover progress model, async Save, modal UI, mutating command gating, viewport input blocking, rollback preservation, tests, and session documentation.
- Type consistency: The plan consistently uses `AuthoringSaveProgress`, `IsSaving`, `SaveProgressMessage`, `SaveProgressPercent`, `CanRunMutatingCommand`, `IsEditorInteractionEnabled`, and `SetInputBlocked`.
- Scope: The plan does not add cancellation or PNG row-level progress, matching the approved non-goals.
