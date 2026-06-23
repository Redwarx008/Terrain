#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering.NativeViewport;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Commands;
using Terrain.Editor.Services.Export;
using Terrain.Editor.Services.Export.Exporters;
using Terrain.Editor.Services.Resources;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace Terrain.Editor.ViewModels;

public sealed partial class EditorShellViewModel : ObservableObject, IDisposable
{
    private readonly EditorState _editorState = EditorState.Instance;
    private readonly EditorDirtyState _dirtyState = EditorDirtyState.Instance;
    private readonly HistoryManager _historyManager = HistoryManager.Instance;
    private readonly MaterialSlotManager _materialSlotManager = MaterialSlotManager.Instance;
    private readonly EditorBootstrapService _bootstrapService;
    private readonly NativeStrideViewportHost _viewportHost;
    private readonly TerrainExporter _terrainExporter = new();
    private EditorResourceSession? _resourceSession;
    private bool _isDisposed;
    private TerrainManager? _subscribedTerrainManager;
    private readonly HashSet<string> _thumbnailDiagnostics = new(StringComparer.Ordinal);

    [ObservableProperty]
    private string _title = "Terrain Editor";

    [ObservableProperty]
    private string _projectName = "Terrain";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSculptMode))]
    [NotifyPropertyChangedFor(nameof(IsPaintMode))]
    [NotifyPropertyChangedFor(nameof(IsFoliageMode))]
    [NotifyPropertyChangedFor(nameof(IsSettingsMode))]
    [NotifyPropertyChangedFor(nameof(IsRiverMode))]
    private EditorMode _selectedMode;

    [ObservableProperty]
    private ModeOptionViewModel? _selectedModeOption;

    [ObservableProperty]
    private SceneViewMode _selectedSceneViewMode = SceneViewMode.Perspective;


    [ObservableProperty]
    private SceneLightingMode _selectedSceneLightingMode = SceneLightingMode.Lit;

    [ObservableProperty]
    private string _selectedToolName = "Sculpt";

    [ObservableProperty]
    private ToolOptionViewModel? _selectedTool;

    [ObservableProperty]
    private MaterialSlotOptionViewModel? _selectedMaterialSlot;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private string _undoLabel = "Undo";

    [ObservableProperty]
    private string _redoLabel = "Redo";

    [ObservableProperty]
    private bool _showMaskOverlay = false;

    [ObservableProperty]
    private bool _heatmapEnabled;

    [ObservableProperty]
    private int _selectedMaterialSlotIndex;

    [ObservableProperty]
    private string _selectedAssetCategory = "Textures";

    [ObservableProperty]
    private string _assetSearchText = string.Empty;

    [ObservableProperty]
    private AssetBrowserItemViewModel? _selectedAssetItem;

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private bool _isAssetBrowserVisible = true;

    [ObservableProperty]
    private bool _isSideNavVisible = true;

    [ObservableProperty]
    private bool _isInspectorVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorInteractionEnabled))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorInteractionEnabled))]
    private bool _isExporting;

    [ObservableProperty]
    private int _saveProgressCurrent;

    [ObservableProperty]
    private int _saveProgressTotal = AuthoringSaveProgress.TotalSteps;

    [ObservableProperty]
    private double _saveProgressPercent;

    [ObservableProperty]
    private string _saveProgressMessage = string.Empty;

    [ObservableProperty]
    private int _exportProgressCurrent;

    [ObservableProperty]
    private int _exportProgressTotal = 1;

    [ObservableProperty]
    private double _exportProgressPercent;

    [ObservableProperty]
    private string _exportProgressMessage = string.Empty;

    public NativeStrideViewportViewModel Viewport { get; }

    public BrushParametersViewModel BrushParams { get; }


    public BiomeViewModel Biome { get; }

    [ObservableProperty]
    private RiverViewModel? _river;

    public SettingsViewModel Settings { get; }

    public ObservableCollection<ModeOptionViewModel> Modes { get; } = new();

    public ObservableCollection<ToolOptionViewModel> Tools { get; } = new();

    public ObservableCollection<string> AssetCategories { get; } = new();

    public ObservableCollection<AssetBrowserItemViewModel> AssetItems { get; } = new();

    public ObservableCollection<ConsoleEntryViewModel> ConsoleEntries { get; } = new();

    public ObservableCollection<MaterialSlotOptionViewModel> MaterialSlots { get; } = new();

    public string SelectedMaterialSlotName => SelectedMaterialSlot?.Name ?? "No material selected";

    public string SelectedMaterialSlotDetail => SelectedMaterialSlot?.Detail ?? "Import albedo first";

    public Bitmap? SelectedMaterialSlotPreviewImage
    {
        get
        {
            if (SelectedMaterialSlot == null)
                return null;

            return LoadTextureThumbnail(_materialSlotManager[SelectedMaterialSlot.Index]);
        }
    }

    public Array EditorModes { get; } = Enum.GetValues<EditorMode>();

    public Array SceneViewModes { get; } = Enum.GetValues<SceneViewMode>();

    public Array SceneLightingModes { get; } = Enum.GetValues<SceneLightingMode>();

    public bool IsSculptMode => SelectedMode == EditorMode.Sculpt;

    public bool IsPaintMode => SelectedMode == EditorMode.Paint;


    public bool IsFoliageMode => SelectedMode == EditorMode.Foliage;

    public bool IsSettingsMode => SelectedMode == EditorMode.Settings;

    public bool IsRiverMode => SelectedMode == EditorMode.River;

    public bool HasTools => SelectedMode != EditorMode.Settings;

    public bool IsEditorInteractionEnabled => !IsSaving && !IsExporting;

    public bool IsBiomeVisible => IsPaintMode;

    public string SelectedModeDisplayName => SelectedMode switch
    {
        EditorMode.Paint => "Biome",
        _ => SelectedMode.ToString(),
    };

    public bool IsListView => !IsGridView;

    public EditorShellViewModel()
        : this(new EditorBootstrapService())
    {
    }

    public EditorShellViewModel(EditorBootstrapService bootstrapService)
    {
        _bootstrapService = bootstrapService ?? throw new ArgumentNullException(nameof(bootstrapService));
        _viewportHost = new NativeStrideViewportHost();
        Viewport = new NativeStrideViewportViewModel(_viewportHost);
        BrushParams = new BrushParametersViewModel();
        Biome = new BiomeViewModel(() => _resourceSession);
        Settings = new SettingsViewModel();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        SelectedSceneViewMode = _viewportHost.SceneViewMode;
        ExportManager.Instance.Register(_terrainExporter);

        InitializeModes();
        InitializeAssetBrowser();
        RefreshMaterialSlots();

        SelectedMode = NormalizeEditorMode(_editorState.CurrentEditorMode);
        ShowMaskOverlay = _editorState.ShowMaskOverlay;
        HeatmapEnabled = _editorState.HeatmapEnabled;
        SelectedMaterialSlotIndex = _editorState.SelectedMaterialSlotIndex;
        RefreshTools();

        if (_viewportHost.TerrainManager != null)
        {
            River = new RiverViewModel(_viewportHost.TerrainManager);
        }

        SyncSelectedModeOption();
        RefreshProjectState();
        RefreshHistoryState();

        _editorState.EditorModeChanged += OnEditorModeChanged;
        _editorState.HeightToolChanged += OnToolChanged;
        _editorState.PaintToolChanged += OnToolChanged;
        _editorState.OverlayChanged += OnOverlayChanged;
        _editorState.HeatmapChanged += OnHeatmapChanged;
        _editorState.MaterialSlotSelectionChanged += OnMaterialSlotSelectionChanged;
        _materialSlotManager.SlotsChanged += OnMaterialSlotsChanged;
        _materialSlotManager.SelectedSlotChanged += OnMaterialSlotManagerSelectedSlotChanged;
        _dirtyState.DirtyChanged += OnEditorDirtyChanged;
        _historyManager.HistoryChanged += OnHistoryChanged;
        _viewportHost.ShortcutRequested += OnViewportShortcutRequested;
        _viewportHost.RuntimeStateChanged += OnViewportRuntimeStateChanged;

        AddConsole("Info", "Avalonia shell initialized with SimpleTheme.");
        AddConsole("Info", "Stride viewport is now hosted through a native child HWND with SDL.");
        AddConsole("Info", _viewportHost.Status);

        if (_viewportHost.HasSceneRuntime)
        {
            AddConsole("Info", "Stride SDL viewport host now owns a Scene and TerrainManager.");
            EnsureTerrainManagerSubscriptions(_viewportHost.TerrainManager);
            TryWireRiverServices();
            _ = LoadEditorResourceSessionAsync();
        }
        else
        {
            AddConsole("Warning", _viewportHost.Status);
        }
    }

    private void OnTerrainManagerProjectNotificationRaised(object? sender, EventArgs e)
    {
        if (sender is TerrainManager terrainManager)
        {
            string? projectNotification = terrainManager.ConsumePendingProjectNotification();
            if (!string.IsNullOrWhiteSpace(projectNotification))
                AddConsole("Warning", projectNotification);
        }
    }

    private void OnViewportRuntimeStateChanged(object? sender, EventArgs e)
    {
        EnsureTerrainManagerSubscriptions(_viewportHost.TerrainManager);
        TryWireRiverServices();
        if (_viewportHost.TerrainManager != null)
        {
            _viewportHost.TerrainManager.SetTerrainVisible(Settings.ShowTerrain);
            _ = LoadEditorResourceSessionAsync();
        }
    }

    private async Task LoadEditorResourceSessionAsync()
    {
        if (_resourceSession != null)
            return;
        if (!TryGetTerrainManager(out var terrainManager))
            return;

        try
        {
            EditorResourceSession session = _bootstrapService.LoadCurrentSession();
            var entities = await terrainManager.LoadFromResourceSession(session);

            if (session.HasPendingHeightmap)
            {
                _resourceSession = session;
                SyncSettingsFromTerrainManager();
                EditorDirtyState.Instance.ClearDirty();
                RefreshAssetItems();
                Biome.NotifyMaterialPreviewsChanged();
                RefreshProjectState();
                ReportMaterialLoadIssues(session);
                AddConsole("Error", $"Terrain workspace heightmap is missing: {session.PendingHeightmapPath}");
                AddConsole("Warning", "Terrain workspace loaded with pending resources. Add the missing heightmap before save/export.");
                return;
            }

            if (entities.Count == 0)
            {
                AddConsole("Error", $"Failed to load Terrain workspace heightmap: {session.Heightmap.ResolvedPath}");
                return;
            }

            _resourceSession = session;
            SyncSettingsFromTerrainManager();
            EditorDirtyState.Instance.ClearDirty();
            RefreshAssetItems();
            Biome.NotifyMaterialPreviewsChanged();
            RefreshProjectState();
            ReportMaterialLoadIssues(session);
            AddConsole("Info", $"Loaded Terrain workspace from {_resourceSession.MapDefinition.ResolvedPath}.");
        }
        catch (Exception exception)
        {
            AddConsole("Error", $"Failed to load Terrain workspace: {exception.Message}");
        }
    }

    private void ReportMaterialLoadIssues(EditorResourceSession session)
    {
        foreach (EditorMaterialLoadIssue issue in session.MaterialLoadState.Issues)
        {
            AddConsole("Error", issue.Message);
        }

        if (!session.MaterialLoadState.HasIssues)
            return;

        string summary = session.MaterialLoadState.HasBlockingMissingMaterialIds
            ? "Terrain workspace loaded with degraded materials. Fix descriptor material ids before save/export."
            : "Terrain workspace loaded with degraded materials. Missing texture files are using default fallback visuals.";
        AddConsole("Warning", summary);
    }

    private void TryWireRiverServices()
    {
        if (River == null && _viewportHost.TerrainManager != null)
        {
            River = new RiverViewModel(_viewportHost.TerrainManager);
        }
        if (River != null && _viewportHost.RiverRenderingService != null && _viewportHost.RiverMeshService != null)
        {
            River.SetServices(_viewportHost.RiverRenderingService, _viewportHost.RiverMeshService);
            _viewportHost.RiverRenderingService.SetVisible(Settings.ShowRivers);
            _viewportHost.RiverRenderingService.SetMaxVisibleCameraHeight(Settings.RiverMaxVisibleCameraHeight);
        }
    }

    private bool CanRunMutatingCommand()
    {
        return !IsSaving && !IsExporting;
    }

    private void BeginSaveProgress()
    {
        SaveProgressCurrent = 0;
        SaveProgressTotal = AuthoringSaveProgress.TotalSteps;
        SaveProgressPercent = 0.0;
        SaveProgressMessage = "Preparing authoring save...";
        IsSaving = true;
    }

    private void EndSaveProgress()
    {
        IsSaving = false;
    }

    private void UpdateSaveProgress(AuthoringSaveProgress report)
    {
        SaveProgressCurrent = Math.Clamp(report.Current, 0, report.Total);
        SaveProgressTotal = report.Total > 0 ? report.Total : AuthoringSaveProgress.TotalSteps;
        SaveProgressPercent = SaveProgressTotal <= 0
            ? 0.0
            : Math.Clamp((double)SaveProgressCurrent / SaveProgressTotal * 100.0, 0.0, 100.0);

        if (!string.IsNullOrWhiteSpace(report.Message))
        {
            SaveProgressMessage = report.Message;
        }
    }

    private void BeginExportProgress()
    {
        ExportProgressCurrent = 0;
        ExportProgressTotal = 1;
        ExportProgressPercent = 0.0;
        ExportProgressMessage = "Preparing terrain export...";
        IsExporting = true;
    }

    private void EndExportProgress()
    {
        IsExporting = false;
    }

    private void UpdateExportProgress(ExportProgress report)
    {
        int total = report.Total > 0 ? report.Total : ExportProgressTotal;
        if (total <= 0)
        {
            total = 1;
        }

        int current = report.IsCompleted && report.ErrorMessage == null
            ? total
            : report.Current;

        ExportProgressCurrent = Math.Clamp(current, 0, total);
        ExportProgressTotal = total;
        ExportProgressPercent = Math.Clamp((double)ExportProgressCurrent / ExportProgressTotal * 100.0, 0.0, 100.0);

        if (!string.IsNullOrWhiteSpace(report.Message))
        {
            ExportProgressMessage = report.Message;
        }

        if (!string.IsNullOrWhiteSpace(report.ErrorMessage))
        {
            ExportProgressMessage = report.ErrorMessage;
        }
    }

    private void NotifyMutatingCommandsCanExecuteChanged()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ExportTerrainCommand.NotifyCanExecuteChanged();
        ImportAssetsCommand.NotifyCanExecuteChanged();
        AddAssetForCategoryCommand.NotifyCanExecuteChanged();
        ImportSelectedNormalCommand.NotifyCanExecuteChanged();
        ClearSelectedMaterialSlotCommand.NotifyCanExecuteChanged();
        DeleteAssetItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingChanged(bool value)
    {
        _viewportHost.SetInputBlocked(IsSaving || IsExporting);
        OnPropertyChanged(nameof(IsEditorInteractionEnabled));
        RefreshHistoryState();
        NotifyMutatingCommandsCanExecuteChanged();
    }

    partial void OnIsExportingChanged(bool value)
    {
        _viewportHost.SetInputBlocked(IsSaving || IsExporting);
        OnPropertyChanged(nameof(IsEditorInteractionEnabled));
        RefreshHistoryState();
        NotifyMutatingCommandsCanExecuteChanged();
    }

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
            await Task.Yield();
            var progress = new Progress<AuthoringSaveProgress>(UpdateSaveProgress);
            EditorDirtySnapshot dirtySnapshot = _dirtyState.CaptureSnapshot();
            EditorDirtyResource generatedResources = EditorGeneratedAuthoringResourceDetector.DetectMissingGeneratedResources(
                session,
                terrainManager.BiomeMask != null);
            if (generatedResources != EditorDirtyResource.None)
                dirtySnapshot = dirtySnapshot.WithAdditionalResources(generatedResources);

            EditorDirtyResource dirtyResources = dirtySnapshot.Resources;
            if (dirtyResources == EditorDirtyResource.None)
            {
                UpdateSaveProgress(AuthoringSaveProgress.Running(2, AuthoringSaveProgress.TotalSteps, "No dirty authoring resources to save."));
                UpdateSaveProgress(AuthoringSaveProgress.Completed(AuthoringSaveProgress.TotalSteps, AuthoringSaveProgress.TotalSteps));
                AddConsole("Info", "No authoring resource changes to save.");
                return;
            }

            var snapshot = terrainManager.CreateAuthoringSaveSnapshot(Settings.RiverMaxVisibleCameraHeight, progress, dirtySnapshot);
            await Task.Run(() => terrainManager.SaveAuthoringResources(session, snapshot, progress));
            UpdateSaveProgress(AuthoringSaveProgress.Running(9, AuthoringSaveProgress.TotalSteps, "Refreshing editor state..."));
            if (snapshot.DescriptorSlots != null)
                _materialSlotManager.ApplyCommittedDescriptorIds(snapshot.DescriptorSlots);
            _resourceSession = _bootstrapService.LoadCurrentSession();
            EditorDirtyState.Instance.ClearDirty(snapshot.DirtySnapshot);
            RefreshAssetItems();
            RefreshMaterialSlots();
            Biome.NotifyMaterialPreviewsChanged();
            RefreshProjectState();
            UpdateSaveProgress(AuthoringSaveProgress.Completed(AuthoringSaveProgress.TotalSteps, AuthoringSaveProgress.TotalSteps));
            AddConsole("Info", "Saved authoring resources.");
        }
        catch (Exception exception)
        {
            UpdateSaveProgress(AuthoringSaveProgress.Failed(SaveProgressCurrent, SaveProgressTotal, $"Save failed: {exception.Message}"));
            AddConsole("Error", $"Save failed: {exception.Message}");
        }
        finally
        {
            EndSaveProgress();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunMutatingCommand))]
    private async Task ExportTerrain()
    {
        if (!TryGetTerrainManager(out var terrainManager))
        {
            return;
        }

        if (_resourceSession == null)
        {
            AddConsole("Warning", "Terrain workspace is not loaded.");
            return;
        }

        if (!_resourceSession.CanExportTerrainData)
        {
            if (_resourceSession.HasPendingHeightmap)
                AddConsole("Warning", "Terrain workspace is waiting for a heightmap before save/export.");
            else
                AddConsole("Warning", "Terrain workspace has missing material declarations. Fix descriptor material ids before save/export.");
            return;
        }

        if (!terrainManager.HasTerrainLoaded)
        {
            AddConsole("Warning", "No terrain loaded to export.");
            return;
        }

        string path = _resourceSession.TerrainData.ResolvedPath;
        _terrainExporter.TerrainManager = terrainManager;
        BeginExportProgress();

        try
        {
            var progress = new Progress<ExportProgress>(report =>
            {
                UpdateExportProgress(report);
                if (report.IsCompleted)
                {
                    AddConsole(report.ErrorMessage == null ? "Info" : "Error",
                        report.ErrorMessage ?? "Terrain export completed.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(report.Message))
                {
                    AddConsole("Info", report.Message);
                }
            });

            await Task.Yield();
            await ExportManager.Instance.ExecuteAsync("Terrain", path, progress, CancellationToken.None);
            UpdateExportProgress(ExportProgress.Completed());
            AddConsole("Info", $"Terrain exported to {path}.");
        }
        catch (Exception exception)
        {
            UpdateExportProgress(ExportProgress.Failed(exception.Message));
            AddConsole("Error", $"Terrain export failed: {exception.Message}");
        }
        finally
        {
            EndExportProgress();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunMutatingCommand))]
    private async Task ImportAssets()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var results = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Assets",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Image Files")
                {
                    Patterns = ["*.dds", "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp", "*.tiff", "*.tif"],
                },
            ],
        });

        if (results.Count == 0)
        {
            return;
        }

        if (!TryGetTerrainRuntime(out _, out var graphicsDevice, out var commandList))
        {
            return;
        }

        int preferredSlotIndex = _materialSlotManager.NextAvailableSlotIndex;
        int importedCount = 0;
        foreach (var file in results)
        {
            string? path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            int slotIndex = ResolveImportTargetSlot(preferredSlotIndex);
            if (slotIndex < 0)
            {
                AddConsole("Warning", "No available material slots.");
                break;
            }

            if (!ImportAlbedoTexture(slotIndex, path, graphicsDevice, commandList))
            {
                continue;
            }

            importedCount++;
            preferredSlotIndex = _materialSlotManager.NextAvailableSlotIndex;
        }

        if (importedCount > 0)
        {
            AddConsole("Info", $"Imported {importedCount} material texture(s).");
            if (SelectedMode != EditorMode.Paint)
            {
                SelectedMode = EditorMode.Paint;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_historyManager.Undo())
        {
            AddConsole("Info", "Undo applied.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_historyManager.Redo())
        {
            AddConsole("Info", "Redo applied.");
        }
    }


    [RelayCommand]
    private void SelectMode(string modeName)
    {
        if (Enum.TryParse<EditorMode>(modeName, out var mode))
        {
            _editorState.CurrentEditorMode = mode;
        }
    }

    [RelayCommand]
    private void ResetLayout()
    {
        AddConsole("Info", "Layout reset requested.");
    }

    [RelayCommand]
    private void OpenSettings()
    {
        AddConsole("Info", "Settings panel is not implemented yet.");
    }

    [RelayCommand]
    private void OpenHelp()
    {
        AddConsole("Info", "Help panel is not implemented yet.");
    }

    [RelayCommand]
    private void SetGridView()
    {
        IsGridView = true;
    }

    [RelayCommand]
    private void SelectAssetCategory(string category)
    {
        SelectedAssetCategory = category;
    }

    [RelayCommand(CanExecute = nameof(CanRunMutatingCommand))]
    private async Task AddAssetForCategory(string category)
    {
        if (category == "Textures")
        {
            await ImportAssets();
        }
    }

    private bool CanDeleteAssetItem(AssetBrowserItemViewModel item)
    {
        return CanRunMutatingCommand() && item is not null && item.MaterialSlotIndex >= 0 && !item.IsCreateItem;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteAssetItem))]
    private void DeleteAssetItem(AssetBrowserItemViewModel item)
    {
        if (item.MaterialSlotIndex >= 0 && !item.IsCreateItem)
        {
            if (!TryGetTerrainRuntime(out _, out var graphicsDevice, out var commandList))
            {
                return;
            }

            _materialSlotManager.ClearSlot(item.MaterialSlotIndex, graphicsDevice, commandList);
            MarkMaterialDescriptorDirty(mayChangeMaterialIds: true);
            AddConsole("Info", $"Cleared slot {item.MaterialSlotIndex}.");
        }
    }

    [RelayCommand]
    private void SetListView()
    {
        IsGridView = false;
    }

    [RelayCommand]
    private void ToggleAssetBrowser()
    {
        IsAssetBrowserVisible = !IsAssetBrowserVisible;
    }

    [RelayCommand(CanExecute = nameof(CanRunMutatingCommand))]
    private async Task ImportSelectedNormal()
    {
        if (SelectedMaterialSlot == null)
        {
            AddConsole("Warning", "Select a material slot first.");
            return;
        }

        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var results = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Normal Texture",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image Files")
                {
                    Patterns = ["*.dds", "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp", "*.tiff", "*.tif"],
                },
            ],
        });

        if (results.Count == 0)
        {
            return;
        }

        string? path = results[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!TryGetTerrainRuntime(out _, out var graphicsDevice, out var commandList))
        {
            return;
        }

        _ = ImportNormalTexture(SelectedMaterialSlot.Index, path, graphicsDevice, commandList);
    }

    [RelayCommand(CanExecute = nameof(CanRunMutatingCommand))]
    private void ClearSelectedMaterialSlot()
    {
        if (SelectedMaterialSlot == null)
        {
            AddConsole("Warning", "Select a material slot first.");
            return;
        }

        if (!TryGetTerrainRuntime(out _, out var graphicsDevice, out var commandList))
        {
            return;
        }

        _materialSlotManager.ClearSlot(SelectedMaterialSlot.Index, graphicsDevice, commandList);
        MarkMaterialDescriptorDirty(mayChangeMaterialIds: true);
        AddConsole("Info", $"Cleared slot {SelectedMaterialSlot.Index}.");
    }

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _editorState.EditorModeChanged -= OnEditorModeChanged;
        _editorState.HeightToolChanged -= OnToolChanged;
        _editorState.PaintToolChanged -= OnToolChanged;
        _editorState.OverlayChanged -= OnOverlayChanged;
        _editorState.HeatmapChanged -= OnHeatmapChanged;
        _editorState.MaterialSlotSelectionChanged -= OnMaterialSlotSelectionChanged;
        _materialSlotManager.SlotsChanged -= OnMaterialSlotsChanged;
        _materialSlotManager.SelectedSlotChanged -= OnMaterialSlotManagerSelectedSlotChanged;
        _dirtyState.DirtyChanged -= OnEditorDirtyChanged;
        _historyManager.HistoryChanged -= OnHistoryChanged;
        _viewportHost.ShortcutRequested -= OnViewportShortcutRequested;
        _viewportHost.RuntimeStateChanged -= OnViewportRuntimeStateChanged;
        EnsureTerrainManagerSubscriptions(null);
        BrushParams.Dispose();
        Biome.Dispose();
        River?.Dispose();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        Viewport.Dispose();
        _viewportHost.Dispose();
    }

    partial void OnSelectedModeChanged(EditorMode value)
    {
        EditorMode normalized = NormalizeEditorMode(value);
        if (value != normalized)
        {
            SelectedMode = normalized;
            return;
        }

        if (_editorState.CurrentEditorMode != normalized)
        {
            _editorState.CurrentEditorMode = normalized;
        }

        SyncSelectedModeOption();
        OnPropertyChanged(nameof(IsSculptMode));
        OnPropertyChanged(nameof(IsPaintMode));
        OnPropertyChanged(nameof(IsFoliageMode));
        OnPropertyChanged(nameof(IsSettingsMode));
        OnPropertyChanged(nameof(IsRiverMode));
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(IsBiomeVisible));
        OnPropertyChanged(nameof(SelectedModeDisplayName));
    }

    partial void OnCanUndoChanged(bool value)
    {
        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanRedoChanged(bool value)
    {
        RedoCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSceneViewModeChanged(SceneViewMode value)
    {
        if (_viewportHost.SceneViewMode != value)
        {
            _viewportHost.SetSceneViewMode(value);
        }

        AddConsole("Info", $"Scene view mode set to {value}.");
    }


    partial void OnShowMaskOverlayChanged(bool value)
    {
        if (_editorState.ShowMaskOverlay != value)
        {
            _editorState.ShowMaskOverlay = value;
        }
    }

    partial void OnHeatmapEnabledChanged(bool value)
    {
        if (_editorState.HeatmapEnabled != value)
        {
            _editorState.HeatmapEnabled = value;
        }
    }

    partial void OnSelectedMaterialSlotIndexChanged(int value)
    {
        if (_editorState.SelectedMaterialSlotIndex != value)
        {
            _editorState.SelectedMaterialSlotIndex = value;
        }

        if (_materialSlotManager.SelectedSlotIndex != value)
        {
            _materialSlotManager.SelectedSlotIndex = value;
        }
    }

    partial void OnSelectedMaterialSlotChanged(MaterialSlotOptionViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedMaterialSlotName));
        OnPropertyChanged(nameof(SelectedMaterialSlotDetail));
        OnPropertyChanged(nameof(SelectedMaterialSlotPreviewImage));

        if (value == null)
        {
            return;
        }

        if (SelectedMaterialSlotIndex != value.Index)
        {
            SelectedMaterialSlotIndex = value.Index;
        }
    }

    partial void OnSelectedModeOptionChanged(ModeOptionViewModel? value)
    {
        if (value is not null && SelectedMode != value.Mode)
        {
            SelectedMode = value.Mode;
        }
    }

    partial void OnSelectedToolChanged(ToolOptionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedToolName = value.Label;
        if (_editorState.CurrentEditorMode != value.Mode)
        {
            _editorState.CurrentEditorMode = value.Mode;
        }

        if (value.HeightTool.HasValue)
        {
            _editorState.CurrentHeightTool = value.HeightTool.Value;
            _editorState.HasSelectedTool = true;
        }
        else if (value.PaintTool.HasValue)
        {
            _editorState.CurrentPaintTool = value.PaintTool.Value;
            _editorState.HasSelectedTool = true;
        }
        else
        {
            // Only mark tool as selected if the current mode has actual brush
            // stroke handling in EmbeddedStrideViewportGame. Foliage has no
            // backend brush implementation yet, so its tools must not set
            // HasSelectedTool to true.
            if (value.Mode != EditorMode.Foliage)
            {
                _editorState.HasSelectedTool = true;
            }

            // 设置 CurrentToolKind（用于工具派发）
            if (value.ToolKind != EditorToolKind.None)
            {
                _editorState.CurrentToolKind = value.ToolKind;
            }
        }

        AddConsole("Info", $"Selected {value.Label}.");
    }

    partial void OnSelectedAssetCategoryChanged(string value)
    {
        RefreshAssetItems();
    }

    partial void OnIsGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListView));
    }

    private void OnEditorModeChanged(object? sender, EventArgs e)
    {
        SelectedMode = NormalizeEditorMode(_editorState.CurrentEditorMode);
        RefreshTools();
    }

    private void OnToolChanged(object? sender, EventArgs e)
    {
        SelectedToolName = SelectedMode switch
        {
            EditorMode.Sculpt => _editorState.CurrentHeightTool.ToString(),
            _ => GetDefaultToolLabel(SelectedMode),
        };
    }

    private void OnOverlayChanged(object? sender, EventArgs e)
    {
        ShowMaskOverlay = _editorState.ShowMaskOverlay;
    }

    private void OnHeatmapChanged(object? sender, EventArgs e)
    {
        HeatmapEnabled = _editorState.HeatmapEnabled;
    }

    private void OnMaterialSlotSelectionChanged(object? sender, EventArgs e)
    {
        SelectedMaterialSlotIndex = _editorState.SelectedMaterialSlotIndex;
        SyncSelectedMaterialSlot();
    }

    private void OnMaterialSlotsChanged(object? sender, EventArgs e)
    {
        RefreshMaterialSlots();
        if (SelectedAssetCategory == "Textures")
        {
            RefreshAssetItems();
        }

        OnPropertyChanged(nameof(SelectedMaterialSlotPreviewImage));
    }

    private void OnMaterialSlotManagerSelectedSlotChanged(object? sender, EventArgs e)
    {
        if (SelectedMaterialSlotIndex != _materialSlotManager.SelectedSlotIndex)
        {
            SelectedMaterialSlotIndex = _materialSlotManager.SelectedSlotIndex;
        }

        SyncSelectedMaterialSlot();
    }

    private void OnEditorDirtyChanged(object? sender, EventArgs e)
    {
        if (_isDisposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                    RefreshProjectState();
            });
            return;
        }

        RefreshProjectState();
    }

    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs e)
    {
        RefreshHistoryState();
    }

    private void OnViewportShortcutRequested(object? sender, ViewportShortcutRequestedEventArgs e)
    {
        switch (e.Shortcut)
        {
            case ViewportShortcut.Undo:
                if (UndoCommand.CanExecute(null))
                {
                    UndoCommand.Execute(null);
                }
                break;
            case ViewportShortcut.Redo:
                if (RedoCommand.CanExecute(null))
                {
                    RedoCommand.Execute(null);
                }
                break;
        }
    }

    private void RefreshProjectState()
    {
        IsDirty = _dirtyState.IsDirty;
        ProjectName = "Terrain";
        Title = IsDirty ? "Terrain Editor - Terrain *" : "Terrain Editor";
    }

    private void RefreshHistoryState()
    {
        CanUndo = !IsSaving && !IsExporting && _historyManager.CanUndo;
        CanRedo = !IsSaving && !IsExporting && _historyManager.CanRedo;
        UndoLabel = _historyManager.UndoDescription is { Length: > 0 } undo ? $"Undo {undo}" : "Undo";
        RedoLabel = _historyManager.RedoDescription is { Length: > 0 } redo ? $"Redo {redo}" : "Redo";
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void RefreshTools()
    {
        Tools.Clear();
        foreach (var tool in CreateToolsForMode(SelectedMode))
        {
            Tools.Add(tool);
        }

        string activeToolLabel = SelectedMode switch
        {
            EditorMode.Sculpt => _editorState.CurrentHeightTool.ToString(),
            _ => GetDefaultToolLabel(SelectedMode),
        };

        SelectedTool = Tools.FirstOrDefault(tool => string.Equals(tool.Label, activeToolLabel, StringComparison.Ordinal))
            ?? Tools.FirstOrDefault();
        SelectedToolName = SelectedTool?.Label ?? "None";
    }

    private void RefreshMaterialSlots()
    {
        var activeSlots = _materialSlotManager
            .GetActiveSlots()
            .Select(static slot => new MaterialSlotOptionViewModel(
                slot.Index,
                GetMaterialSlotDisplayName(slot),
                !string.IsNullOrWhiteSpace(slot.NormalTexturePath),
                !string.IsNullOrWhiteSpace(slot.PropertiesTexturePath)))
            .OrderBy(static slot => slot.Index)
            .ToArray();

        MaterialSlots.Clear();
        foreach (var slot in activeSlots)
        {
            MaterialSlots.Add(slot);
        }

        SyncSelectedMaterialSlot();
    }

    private void SyncSelectedMaterialSlot()
    {
        MaterialSlotOptionViewModel? selected = MaterialSlots.FirstOrDefault(slot => slot.Index == SelectedMaterialSlotIndex);
        if (SelectedMaterialSlot != selected)
        {
            SelectedMaterialSlot = selected;
        }
    }

    private static ToolOptionViewModel[] CreateToolsForMode(EditorMode mode)
    {
        return mode switch
        {
            EditorMode.Sculpt =>
            [
                new("Sculpt", "Raise terrain height", "\uE74A", mode, HeightTool.Raise),
                new("Lower", "Lower terrain height", "\uE74D", mode, HeightTool.Lower),
                new("Smooth", "Smooth terrain", "\uE790", mode, HeightTool.Smooth),
                new("Flatten", "Flatten terrain", "\uE81E", mode, HeightTool.Flatten),
            ],
            EditorMode.Paint =>
            [
                new("Biome Brush", "Paint biome mask", "\uE950", mode, ToolKind: EditorToolKind.BiomeBrush),
            ],
            EditorMode.Foliage =>
            [
                new("Place", "Place foliage", "\uE8BE", mode, ToolKind: EditorToolKind.FoliagePlace),
                new("Remove", "Remove foliage", "\uE74D", mode, ToolKind: EditorToolKind.FoliageRemove),
            ],
            EditorMode.River =>
            [
                new("River Tool", "Inspect loaded rivers", "\uE8B7", mode),
            ],
            _ => [],
        };
    }

    private void InitializeModes()
    {
        Modes.Add(new ModeOptionViewModel("Sculpt", "Terrain height editing", "", EditorMode.Sculpt));
        Modes.Add(new ModeOptionViewModel("Biome", "Biome mask painting", "\uE950", EditorMode.Paint));
        Modes.Add(new ModeOptionViewModel("Foliage", "Vegetation placement", "\uE8BE", EditorMode.Foliage));
        Modes.Add(new ModeOptionViewModel("River", "River system from color map", "", EditorMode.River));
        Modes.Add(new ModeOptionViewModel("Settings", "Project settings", "", EditorMode.Settings));
    }

    private void InitializeAssetBrowser()
    {
        foreach (var category in new[] { "Textures", "Meshes", "Foliage", "Prefabs" })
        {
            AssetCategories.Add(category);
        }

        RefreshAssetItems();
    }

    private void RefreshAssetItems()
    {
        AssetItems.Clear();

        var items = CreateAssetItemsForCategory(SelectedAssetCategory);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(AssetSearchText)
                || item.Name.Contains(AssetSearchText, StringComparison.OrdinalIgnoreCase)
                || item.IsCreateItem)
            {
                AssetItems.Add(item);
            }
        }
    }

    partial void OnAssetSearchTextChanged(string value)
    {
        RefreshAssetItems();
    }

    private void SyncSelectedModeOption()
    {
        SelectedModeOption = Modes.FirstOrDefault(option => option.Mode == SelectedMode);
    }

    private static string GetDefaultToolLabel(EditorMode mode)
    {
        return mode switch
        {
            EditorMode.Sculpt => "Sculpt",
            EditorMode.Paint => "Biome Brush",
            EditorMode.Foliage => "Place",
            _ => "None",
        };
    }

    private static EditorMode NormalizeEditorMode(EditorMode mode)
    {
        return mode == EditorMode.Landscape ? EditorMode.Paint : mode;
    }

    private void EnsureTerrainManagerSubscriptions(TerrainManager? terrainManager)
    {
        if (ReferenceEquals(_subscribedTerrainManager, terrainManager))
            return;

        if (_subscribedTerrainManager != null)
        {
            _subscribedTerrainManager.ProjectNotificationRaised -= OnTerrainManagerProjectNotificationRaised;
        }

        _subscribedTerrainManager = terrainManager;
        if (_subscribedTerrainManager == null)
            return;

        _subscribedTerrainManager.ProjectNotificationRaised += OnTerrainManagerProjectNotificationRaised;
        string? projectNotification = _subscribedTerrainManager.ConsumePendingProjectNotification();
        if (!string.IsNullOrWhiteSpace(projectNotification))
            AddConsole("Warning", projectNotification);
    }

    private bool TryGetTerrainManager(out TerrainManager terrainManager)
    {
        terrainManager = null!;
        if (_viewportHost.TerrainManager is { } manager)
        {
            EnsureTerrainManagerSubscriptions(manager);
            terrainManager = manager;
            return true;
        }

        AddConsole("Warning", "Viewport runtime is not ready yet.");
        return false;
    }

    private bool TryGetTerrainRuntime(out TerrainManager terrainManager, out Stride.Graphics.GraphicsDevice graphicsDevice, out Stride.Graphics.CommandList commandList)
    {
        terrainManager = null!;
        graphicsDevice = null!;
        commandList = null!;

        if (_viewportHost.TryGetRuntimeServices(out var manager, out var device, out var commands))
        {
            EnsureTerrainManagerSubscriptions(manager);
            terrainManager = manager!;
            graphicsDevice = device!;
            commandList = commands!;
            return true;
        }

        AddConsole("Warning", "Viewport runtime is not ready yet.");
        return false;
    }

    private int ResolveImportTargetSlot(int preferredSlotIndex)
    {
        if (preferredSlotIndex >= 0 && preferredSlotIndex < 256 && _materialSlotManager[preferredSlotIndex].IsEmpty)
        {
            return preferredSlotIndex;
        }

        int nextAvailable = _materialSlotManager.NextAvailableSlotIndex;
        if (nextAvailable >= 0)
        {
            return nextAvailable;
        }

        return -1;
    }

    private bool ImportAlbedoTexture(int slotIndex, string path, Stride.Graphics.GraphicsDevice graphicsDevice, Stride.Graphics.CommandList commandList)
    {
        string sourcePath = path;
        string? sourceNormalPath = TextureImporter.FindMatchingNormalMap(sourcePath);
        path = CopyTextureIntoMaterialDirectory(path);
        var texture = TextureImporter.ImportFromFile(path, graphicsDevice, commandList, TextureSize.Size512, isNormalMap: false);
        if (texture == null)
        {
            AddConsole("Error", $"Failed to import texture: {path}");
            return false;
        }

        if (!_materialSlotManager.TrySetAlbedoTexture(slotIndex, texture, path, TextureSize.Size512, graphicsDevice, commandList, out string? error))
        {
            AddConsole("Error", error ?? $"Failed to import albedo to slot {slotIndex}.");
            return false;
        }

        MarkMaterialDescriptorDirty(mayChangeMaterialIds: true);
        _materialSlotManager.SelectedSlotIndex = slotIndex;
        SelectedMaterialSlotIndex = slotIndex;
        SelectedMode = EditorMode.Paint;
        AddConsole("Info", $"Imported albedo to slot {slotIndex}: {path}");

        if (!string.IsNullOrWhiteSpace(sourceNormalPath))
        {
            _ = ImportNormalTexture(slotIndex, sourceNormalPath, graphicsDevice, commandList, logSuccess: false);
        }

        return true;
    }

    private bool ImportNormalTexture(int slotIndex, string path, Stride.Graphics.GraphicsDevice graphicsDevice, Stride.Graphics.CommandList commandList, bool logSuccess = true)
    {
        path = CopyTextureIntoMaterialDirectory(path);
        var texture = TextureImporter.ImportFromFile(path, graphicsDevice, commandList, TextureSize.Size512, isNormalMap: true);
        if (texture == null)
        {
            AddConsole("Error", $"Failed to import normal texture: {path}");
            return false;
        }

        if (!_materialSlotManager.TrySetNormalTexture(slotIndex, texture, path, graphicsDevice, commandList, out string? error))
        {
            AddConsole("Error", error ?? $"Failed to import normal map to slot {slotIndex}.");
            return false;
        }

        MarkMaterialDescriptorDirty(mayChangeMaterialIds: false);
        if (logSuccess)
        {
            AddConsole("Info", $"Imported normal map to slot {slotIndex}: {path}");
        }
        else
        {
            AddConsole("Info", $"Auto-imported normal map for slot {slotIndex}: {path}");
        }

        return true;
    }

    private static void MarkMaterialDescriptorDirty(bool mayChangeMaterialIds)
    {
        EditorDirtyResource dirtyResources = mayChangeMaterialIds
            ? EditorDirtyResource.MaterialDescriptor | EditorDirtyResource.BiomeSettings
            : EditorDirtyResource.MaterialDescriptor;
        EditorDirtyState.Instance.MarkDirty(dirtyResources);
    }

    private string CopyTextureIntoMaterialDirectory(string sourcePath)
    {
        if (_resourceSession == null || string.IsNullOrWhiteSpace(sourcePath))
            return sourcePath;

        string sourceFullPath = Path.GetFullPath(sourcePath);
        string? materialsDirectory = Path.GetDirectoryName(_resourceSession.MaterialDescriptor.ResolvedPath);
        if (string.IsNullOrWhiteSpace(materialsDirectory))
            return sourcePath;

        string targetDirectory = Path.GetFullPath(materialsDirectory);
        Directory.CreateDirectory(targetDirectory);
        string fileName = Path.GetFileName(sourceFullPath);
        string targetPath = Path.Combine(targetDirectory, fileName);
        if (string.Equals(sourceFullPath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            return targetPath;

        targetPath = GetAvailableImportPath(targetDirectory, fileName);
        File.Copy(sourceFullPath, targetPath);
        return targetPath;
    }

    private static string GetAvailableImportPath(string targetDirectory, string fileName)
    {
        string candidate = Path.Combine(targetDirectory, fileName);
        if (!File.Exists(candidate))
            return candidate;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int suffix = 2; suffix < 10_000; suffix++)
        {
            candidate = Path.Combine(targetDirectory, $"{stem}_{suffix}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException($"Could not find an available texture import path for {fileName}.");
    }

    private AssetBrowserItemViewModel[] CreateAssetItemsForCategory(string category)
    {
        if (category == "Textures")
        {
            var items = _materialSlotManager
                .GetActiveSlots()
                .OrderBy(static slot => slot.Index)
                .Select(slot => new AssetBrowserItemViewModel(
                    GetMaterialSlotDisplayName(slot),
                    category,
                    "Texture",
                    AssetColors.TexturePreviewBackground,
                    AssetColors.TexturePreviewForeground,
                    "\xE71B",
                    previewImage: LoadTextureThumbnail(slot),
                    materialSlotIndex: slot.Index))
                .ToList();

            items.Add(new("Add Texture", category, "Create",
                AssetColors.CreatePreviewBackground, AssetColors.CreatePreviewForeground,
                "\xE710", isCreateItem: true));
            return items.ToArray();
        }

        var label = category switch
        {
            "Meshes" => "Add Mesh",
            "Foliage" => "Add Foliage",
            "Prefabs" => "Add Prefab",
            _ => "Add Asset",
        };

        return [new(label, category, "Create",
            AssetColors.CreatePreviewBackground, AssetColors.CreatePreviewForeground,
            "\xE710", isCreateItem: true)];
    }

    private Bitmap? LoadTextureThumbnail(MaterialSlot slot)
    {
        Bitmap? fileThumbnail = TextureThumbnailProvider.LoadFromPath(slot.AlbedoTexturePath, _resourceSession, out string? fileError);
        if (fileThumbnail != null)
            return fileThumbnail;

        Bitmap? gpuThumbnail = TryCreateTextureThumbnailFromGpu(slot, out string? gpuError);
        if (gpuThumbnail != null)
            return gpuThumbnail;

        LogThumbnailDiagnostic(slot, $"{fileError ?? "File thumbnail unavailable."} {gpuError ?? "GPU thumbnail unavailable."}");
        return null;
    }

    private Bitmap? TryCreateTextureThumbnailFromGpu(MaterialSlot slot, out string? error)
    {
        var texture = slot.AlbedoTexture;
        if (texture == null)
        {
            error = "GPU albedo texture is not loaded.";
            return null;
        }

        if (!IsRgba8TextureFormat(texture.Format))
        {
            error = $"GPU texture format is not supported for thumbnail readback: {texture.Format}.";
            return null;
        }

        if (!_viewportHost.TryGetRuntimeServices(out _, out _, out var commandList) || commandList == null)
        {
            error = "Viewport runtime services are unavailable for GPU thumbnail readback.";
            return null;
        }

        try
        {
            byte[] pixels = texture.GetData<byte>(commandList, arrayIndex: 0, mipLevel: 0);
            int expectedLength = texture.Width * texture.Height * 4;
            if (pixels.Length < expectedLength)
            {
                error = $"GPU readback returned too few bytes: {pixels.Length}/{expectedLength}.";
                return null;
            }

            if (IsBgra8TextureFormat(texture.Format))
            {
                ConvertBgraToRgba(pixels.AsSpan(0, expectedLength));
            }

            using var image = ImageSharpImage.LoadPixelData<Rgba32>(
                pixels.AsSpan(0, expectedLength),
                texture.Width,
                texture.Height);
            image.Mutate(static context => context.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(128, 128),
                Mode = ResizeMode.Crop
            }));

            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            stream.Position = 0;
            error = null;
            return new Bitmap(stream);
        }
        catch (Exception exception)
        {
            error = $"GPU thumbnail readback failed: {exception.Message}";
            return null;
        }
    }

    private void LogThumbnailDiagnostic(MaterialSlot slot, string reason)
    {
        string key = $"{slot.Index}:{slot.AlbedoTexturePath}:{reason}";
        if (!_thumbnailDiagnostics.Add(key))
            return;

        string path = string.IsNullOrWhiteSpace(slot.AlbedoTexturePath)
            ? "<empty>"
            : slot.AlbedoTexturePath;
        AddConsole("Warning", $"Texture thumbnail unavailable for slot {slot.Index} ({path}): {reason}");
    }

    private static bool IsRgba8TextureFormat(Stride.Graphics.PixelFormat format)
    {
        return format is Stride.Graphics.PixelFormat.R8G8B8A8_UNorm
            or Stride.Graphics.PixelFormat.R8G8B8A8_UNorm_SRgb
            or Stride.Graphics.PixelFormat.B8G8R8A8_UNorm
            or Stride.Graphics.PixelFormat.B8G8R8A8_UNorm_SRgb;
    }

    private static bool IsBgra8TextureFormat(Stride.Graphics.PixelFormat format)
    {
        return format is Stride.Graphics.PixelFormat.B8G8R8A8_UNorm
            or Stride.Graphics.PixelFormat.B8G8R8A8_UNorm_SRgb;
    }

    private static void ConvertBgraToRgba(Span<byte> pixels)
    {
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }

    private static string GetMaterialSlotDisplayName(MaterialSlot slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.Name)
            && !slot.Name.StartsWith("Texture ", StringComparison.Ordinal))
        {
            return slot.Name;
        }

        if (!string.IsNullOrWhiteSpace(slot.AlbedoTexturePath))
        {
            return Path.GetFileNameWithoutExtension(slot.AlbedoTexturePath);
        }

        return "未分配材质";
    }

    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window
            && window.StorageProvider is { } provider)
        {
            return provider;
        }

        return null;
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.HeightScale))
        {
            if (TryGetTerrainManager(out var terrainManager))
            {
                terrainManager.SetHeightScale(Settings.HeightScale);
            }
        }
        else if (e.PropertyName == nameof(SettingsViewModel.ShowTerrain))
        {
            if (TryGetTerrainManager(out var terrainManager))
            {
                terrainManager.SetTerrainVisible(Settings.ShowTerrain);
            }
        }
        else if (e.PropertyName == nameof(SettingsViewModel.ShowRivers))
        {
            _viewportHost.RiverRenderingService?.SetVisible(Settings.ShowRivers);
        }
        else if (e.PropertyName == nameof(SettingsViewModel.RiverMaxVisibleCameraHeight))
        {
            _viewportHost.RiverRenderingService?.SetMaxVisibleCameraHeight(Settings.RiverMaxVisibleCameraHeight);
            EditorDirtyState.Instance.MarkDirty(EditorDirtyResource.MapDefinition);
        }
    }

    private void SyncSettingsFromTerrainManager()
    {
        if (_viewportHost.TerrainManager is { } manager)
        {
            Settings.ShowTerrain = manager.TerrainVisible;
            Settings.HeightScale = manager.HeightScale;
            if (_resourceSession != null)
            {
                Settings.RiverMaxVisibleCameraHeight = _resourceSession.MapDefinitionModel.RiverMaxVisibleCameraHeight;
            }

            _viewportHost.RiverRenderingService?.SetMaxVisibleCameraHeight(Settings.RiverMaxVisibleCameraHeight);
        }
    }

    private void AddConsole(string level, string message)
    {
        ConsoleEntries.Add(new ConsoleEntryViewModel(level, message));
        while (ConsoleEntries.Count > 200)
        {
            ConsoleEntries.RemoveAt(0);
        }
    }

}
