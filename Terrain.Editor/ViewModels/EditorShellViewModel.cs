#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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
using Stride.TextureConverter;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace Terrain.Editor.ViewModels;

public sealed partial class EditorShellViewModel : ObservableObject, IDisposable
{
    private readonly EditorState _editorState = EditorState.Instance;
    private readonly ProjectManager _projectManager = ProjectManager.Instance;
    private readonly HistoryManager _historyManager = HistoryManager.Instance;
    private readonly MaterialSlotManager _materialSlotManager = MaterialSlotManager.Instance;
    private readonly NativeStrideViewportHost _viewportHost;
    private readonly TerrainExporter _terrainExporter = new();
    private readonly Dictionary<string, TextureThumbnailCacheEntry> _textureThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _thumbnailDiagnostics = new(StringComparer.Ordinal);

    [ObservableProperty]
    private string _title = "Terrain Editor";

    [ObservableProperty]
    private string _projectName = "No project";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
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
    private bool _showMaskOverlay = true;

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

    public NativeStrideViewportViewModel Viewport { get; }

    public BrushParametersViewModel BrushParams { get; }

    public ClimateViewModel Climate { get; }

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

    public bool IsWaterMode => SelectedMode == EditorMode.Water;

    public bool IsLandscapeMode => SelectedMode == EditorMode.Landscape;

    public bool IsClimateVisible => IsPaintMode || IsLandscapeMode;

    public bool IsListView => !IsGridView;

    public EditorShellViewModel()
    {
        _viewportHost = new NativeStrideViewportHost();
        Viewport = new NativeStrideViewportViewModel(_viewportHost);
        BrushParams = new BrushParametersViewModel();
        Climate = new ClimateViewModel();
        SelectedSceneViewMode = _viewportHost.SceneViewMode;
        ExportManager.Instance.Register(_terrainExporter);
        ExportManager.Instance.Register(new MaterialDescriptorExporter());

        InitializeModes();
        InitializeAssetBrowser();
        RefreshMaterialSlots();

        SelectedMode = _editorState.CurrentEditorMode;
        ShowMaskOverlay = _editorState.ShowMaskOverlay;
        HeatmapEnabled = _editorState.HeatmapEnabled;
        SelectedMaterialSlotIndex = _editorState.SelectedMaterialSlotIndex;
        RefreshTools();
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
        _projectManager.DirtyChanged += OnProjectDirtyChanged;
        _historyManager.HistoryChanged += OnHistoryChanged;

        AddConsole("Info", "Avalonia shell initialized with SimpleTheme.");
        AddConsole("Info", "Stride viewport is now hosted through a native child HWND with SDL.");
        AddConsole("Info", _viewportHost.Status);

        if (_viewportHost.HasSceneRuntime)
        {
            AddConsole("Info", "Stride SDL viewport host now owns a Scene and TerrainManager.");
        }
        else
        {
            AddConsole("Warning", _viewportHost.Status);
        }
    }

    [RelayCommand]
    private async Task NewProject()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var results = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Heightmap",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Heightmap PNG") { Patterns = ["*.png"] }],
        });

        if (results.Count == 0)
        {
            return;
        }

        if (!TryGetTerrainManager(out var terrainManager))
        {
            return;
        }

        string path = results[0].TryGetLocalPath() ?? results[0].Path.ToString();
        _projectManager.CloseProject();

        var entities = await terrainManager.LoadTerrainAsync(path);
        if (entities.Count == 0)
        {
            AddConsole("Error", $"Failed to create project from heightmap: {path}");
            RefreshProjectState();
            return;
        }

        RefreshProjectState();
        AddConsole("Info", $"Created unsaved project from heightmap: {path}");
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var results = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Terrain Project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Terrain Project") { Patterns = ["*.toml"] }],
        });

        if (results.Count == 0)
        {
            return;
        }

        string path = results[0].TryGetLocalPath() ?? results[0].Path.ToString();
        if (!TryGetTerrainManager(out var terrainManager))
        {
            return;
        }

        terrainManager.LoadProject(path);
        RefreshProjectState();
        AddConsole("Info", $"Opened project: {_projectManager.ProjectName}.");
    }

    [RelayCommand]
    private void SaveProject()
    {
        if (!_projectManager.IsProjectOpen)
        {
            SaveProjectAsCommand.Execute(null);
            return;
        }

        if (!TryGetTerrainManager(out var terrainManager))
        {
            return;
        }

        if (terrainManager.HasTerrainLoaded)
        {
            terrainManager.SaveProject();
            AddConsole("Info", $"Saved project '{_projectManager.ProjectName}'.");
            RefreshProjectState();
            return;
        }

        AddConsole("Warning", "Nothing to save.");
    }

    [RelayCommand]
    private async Task SaveProjectAs()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Project As",
            SuggestedFileName = _projectManager.ProjectName,
            FileTypeChoices = [new FilePickerFileType("Terrain Project") { Patterns = ["*.toml"] }],
        });

        if (result == null)
        {
            return;
        }

        string path = result.TryGetLocalPath() ?? result.Path.ToString();
        if (_projectManager.IsProjectOpen)
        {
            _projectManager.SaveProjectAs(path);
        }
        else
        {
            _projectManager.CreateProject(path, System.IO.Path.GetFileNameWithoutExtension(path));
        }

        if (TryGetTerrainManager(out var terrainManager) && terrainManager.HasTerrainLoaded)
        {
            terrainManager.SaveProject();
        }

        RefreshProjectState();
        AddConsole("Info", $"Project saved to {path}.");
    }

    [RelayCommand]
    private async Task ExportTerrain()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Terrain",
            SuggestedFileName = "terrain",
            FileTypeChoices = [new FilePickerFileType("Terrain") { Patterns = ["*.terrain"] }],
        });

        if (result == null)
        {
            return;
        }

        if (!TryGetTerrainManager(out var terrainManager))
        {
            return;
        }

        if (!terrainManager.HasTerrainLoaded)
        {
            AddConsole("Warning", "No terrain loaded to export.");
            return;
        }

        string path = result.TryGetLocalPath() ?? result.Path.ToString();
        _terrainExporter.TerrainManager = terrainManager;

        try
        {
            var progress = new Progress<ExportProgress>(report =>
            {
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

            await ExportManager.Instance.ExecuteAsync("Terrain", path, progress, CancellationToken.None);
            AddConsole("Info", $"Terrain exported to {path}.");
        }
        catch (Exception exception)
        {
            AddConsole("Error", $"Terrain export failed: {exception.Message}");
        }
    }

    [RelayCommand]
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

    [RelayCommand]
    private async Task ExportMaterialDescriptor()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Material Descriptor",
            SuggestedFileName = "materials",
            FileTypeChoices = [new FilePickerFileType("TOML") { Patterns = ["*.toml"] }],
        });

        if (result == null)
        {
            return;
        }

        string path = result.TryGetLocalPath() ?? result.Path.ToString();

        try
        {
            var progress = new Progress<ExportProgress>(report =>
            {
                if (report.IsCompleted)
                {
                    AddConsole(report.ErrorMessage == null ? "Info" : "Error",
                        report.ErrorMessage ?? "Material descriptor export completed.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(report.Message))
                {
                    AddConsole("Info", report.Message);
                }
            });

            await ExportManager.Instance.ExecuteAsync("Material Descriptor", path, progress, CancellationToken.None);
            AddConsole("Info", $"Material descriptor exported to {path}.");
        }
        catch (Exception exception)
        {
            AddConsole("Error", $"Material descriptor export failed: {exception.Message}");
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

    [RelayCommand]
    private async Task AddAssetForCategory(string category)
    {
        if (category == "Textures")
        {
            await ImportAssets();
        }
    }

    private bool CanDeleteAssetItem(AssetBrowserItemViewModel item)
    {
        return item is not null && item.MaterialSlotIndex >= 0 && !item.IsCreateItem;
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
            ProjectManager.Instance.MarkDirty();
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

    [RelayCommand]
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

    [RelayCommand]
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
        ProjectManager.Instance.MarkDirty();
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
        _editorState.EditorModeChanged -= OnEditorModeChanged;
        _editorState.HeightToolChanged -= OnToolChanged;
        _editorState.PaintToolChanged -= OnToolChanged;
        _editorState.OverlayChanged -= OnOverlayChanged;
        _editorState.HeatmapChanged -= OnHeatmapChanged;
        _editorState.MaterialSlotSelectionChanged -= OnMaterialSlotSelectionChanged;
        _materialSlotManager.SlotsChanged -= OnMaterialSlotsChanged;
        _materialSlotManager.SelectedSlotChanged -= OnMaterialSlotManagerSelectedSlotChanged;
        _projectManager.DirtyChanged -= OnProjectDirtyChanged;
        _historyManager.HistoryChanged -= OnHistoryChanged;
        BrushParams.Dispose();
        Climate.Dispose();
        Viewport.Dispose();
        foreach (var thumbnail in _textureThumbnailCache.Values)
        {
            thumbnail.Bitmap?.Dispose();
        }

        _textureThumbnailCache.Clear();
        _viewportHost.Dispose();
    }

    partial void OnSelectedModeChanged(EditorMode value)
    {
        if (_editorState.CurrentEditorMode != value)
        {
            _editorState.CurrentEditorMode = value;
        }

        SyncSelectedModeOption();
        OnPropertyChanged(nameof(IsSculptMode));
        OnPropertyChanged(nameof(IsPaintMode));
        OnPropertyChanged(nameof(IsFoliageMode));
        OnPropertyChanged(nameof(IsWaterMode));
        OnPropertyChanged(nameof(IsLandscapeMode));
        OnPropertyChanged(nameof(IsClimateVisible));
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
        SelectedMode = _editorState.CurrentEditorMode;
        RefreshTools();
    }

    private void OnToolChanged(object? sender, EventArgs e)
    {
        SelectedToolName = SelectedMode == EditorMode.Paint
            ? _editorState.CurrentPaintTool.ToString()
            : _editorState.CurrentHeightTool.ToString();
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

    private void OnProjectDirtyChanged(object? sender, EventArgs e)
    {
        RefreshProjectState();
    }

    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs e)
    {
        RefreshHistoryState();
    }

    private void RefreshProjectState()
    {
        IsDirty = _projectManager.IsDirty;
        bool hasUnsavedTerrain = !_projectManager.IsProjectOpen && _viewportHost.TerrainManager?.HasTerrainLoaded == true;
        ProjectName = _projectManager.IsProjectOpen ? _projectManager.ProjectName : hasUnsavedTerrain ? "Unsaved" : "No project";
        Title = _projectManager.IsProjectOpen
            ? $"Terrain Editor - {_projectManager.ProjectName}"
            : hasUnsavedTerrain
                ? "Terrain Editor - Unsaved *"
                : "Terrain Editor";
    }

    private void RefreshHistoryState()
    {
        CanUndo = _historyManager.CanUndo;
        CanRedo = _historyManager.CanRedo;
        UndoLabel = _historyManager.UndoDescription is { Length: > 0 } undo ? $"Undo {undo}" : "Undo";
        RedoLabel = _historyManager.RedoDescription is { Length: > 0 } redo ? $"Redo {redo}" : "Redo";
    }

    private void RefreshTools()
    {
        Tools.Clear();
        foreach (var tool in CreateToolsForMode(SelectedMode))
        {
            Tools.Add(tool);
        }

        SelectedTool = Tools.FirstOrDefault(static tool => tool.Label == GetDefaultToolLabel(tool.Mode))
            ?? Tools.FirstOrDefault();
        SelectedToolName = SelectedTool?.Label ?? "None";
    }

    private void RefreshMaterialSlots()
    {
        var activeSlots = _materialSlotManager
            .GetActiveSlots()
            .Select(static slot => new MaterialSlotOptionViewModel(
                slot.Index,
                string.IsNullOrWhiteSpace(slot.Name) ? $"Texture {slot.Index}" : slot.Name,
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
        if (selected == null && MaterialSlots.Count > 0)
        {
            selected = MaterialSlots[0];
            SelectedMaterialSlotIndex = selected.Index;
        }

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
                new("Paint", "Paint material", "\uE790", mode, null, PaintTool.Paint),
                new("Erase", "Erase material", "\uE74D", mode, null, PaintTool.Erase),
            ],
            EditorMode.Foliage =>
            [
                new("Place", "Place foliage", "\uE8BE", mode),
                new("Remove", "Remove foliage", "\uE74D", mode),
            ],
            EditorMode.Water =>
                [],
            EditorMode.Landscape =>
            [
                new("Climate Map", "Edit climate map", "\uE950", mode),
            ],
            _ => [],
        };
    }

    private void InitializeModes()
    {
        Modes.Add(new ModeOptionViewModel("Sculpt", "Terrain height editing", "", EditorMode.Sculpt));
        Modes.Add(new ModeOptionViewModel("Paint", "Surface material painting", "", EditorMode.Paint));
        Modes.Add(new ModeOptionViewModel("Foliage", "Vegetation placement", "\uE8BE", EditorMode.Foliage));
        Modes.Add(new ModeOptionViewModel("Water", "Water level editing", "", EditorMode.Water));
        Modes.Add(new ModeOptionViewModel("Landscape", "Landscape generation", "", EditorMode.Landscape));
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
            EditorMode.Paint => "Paint",
            EditorMode.Foliage => "Place",
            EditorMode.Landscape => "Climate Map",
            _ => "None",
        };
    }

    private bool TryGetTerrainManager(out TerrainManager terrainManager)
    {
        terrainManager = null!;
        if (_viewportHost.TerrainManager is { } manager)
        {
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

        ProjectManager.Instance.MarkDirty();
        _materialSlotManager.SelectedSlotIndex = slotIndex;
        SelectedMaterialSlotIndex = slotIndex;
        SelectedMode = EditorMode.Paint;
        AddConsole("Info", $"Imported albedo to slot {slotIndex}: {path}");

        string? normalPath = TextureImporter.FindMatchingNormalMap(path);
        if (!string.IsNullOrWhiteSpace(normalPath))
        {
            _ = ImportNormalTexture(slotIndex, normalPath, graphicsDevice, commandList, logSuccess: false);
        }

        return true;
    }

    private bool ImportNormalTexture(int slotIndex, string path, Stride.Graphics.GraphicsDevice graphicsDevice, Stride.Graphics.CommandList commandList, bool logSuccess = true)
    {
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

        ProjectManager.Instance.MarkDirty();
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

    private AssetBrowserItemViewModel[] CreateAssetItemsForCategory(string category)
    {
        if (category == "Textures")
        {
            var items = _materialSlotManager
                .GetActiveSlots()
                .OrderBy(static slot => slot.Index)
                .Select(slot => new AssetBrowserItemViewModel(
                    string.IsNullOrWhiteSpace(slot.Name) ? $"Texture {slot.Index}" : slot.Name,
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
        Bitmap? fileThumbnail = LoadTextureThumbnail(slot.AlbedoTexturePath, out string? fileError);
        if (fileThumbnail != null)
            return fileThumbnail;

        Bitmap? gpuThumbnail = TryCreateTextureThumbnailFromGpu(slot, out string? gpuError);
        if (gpuThumbnail != null)
            return gpuThumbnail;

        LogThumbnailDiagnostic(slot, $"{fileError ?? "File thumbnail unavailable."} {gpuError ?? "GPU thumbnail unavailable."}");
        return null;
    }

    private Bitmap? LoadTextureThumbnail(string? texturePath, out string? error)
    {
        string? resolvedPath = ResolveTextureThumbnailPath(texturePath);
        if (resolvedPath == null)
        {
            error = string.IsNullOrWhiteSpace(texturePath)
                ? "Texture path is empty."
                : $"Texture file was not found: {texturePath}.";
            return null;
        }

        string fullPath = Path.GetFullPath(resolvedPath);
        DateTime lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);

        if (_textureThumbnailCache.TryGetValue(fullPath, out var cached)
            && cached.LastWriteUtc == lastWriteUtc)
        {
            error = cached.Bitmap == null
                ? $"File thumbnail decode failed for cached path: {fullPath}."
                : null;
            return cached.Bitmap;
        }

        cached?.Bitmap?.Dispose();
        Bitmap? bitmap = TryCreateTextureThumbnail(fullPath, out error);
        _textureThumbnailCache[fullPath] = new TextureThumbnailCacheEntry(lastWriteUtc, bitmap);
        return bitmap;
    }

    private string? ResolveTextureThumbnailPath(string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        if (File.Exists(texturePath))
            return texturePath;

        if (Path.IsPathRooted(texturePath))
            return null;

        if (_projectManager.IsProjectOpen)
        {
            string projectRelative = Path.Combine(
                _projectManager.ProjectPath,
                texturePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(projectRelative))
                return projectRelative;

            string materialsRelative = Path.Combine(
                _projectManager.MaterialsPath,
                texturePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(materialsRelative))
                return materialsRelative;
        }

        return null;
    }

    private static Bitmap? TryCreateTextureThumbnail(string texturePath, out string? error)
    {
        Bitmap? textureToolBitmap = TryCreateTextureThumbnailWithTextureTool(texturePath, out string? textureToolError);
        if (textureToolBitmap != null)
        {
            error = null;
            return textureToolBitmap;
        }

        Bitmap? avaloniaBitmap = TryCreateTextureThumbnailWithAvalonia(texturePath, out string? avaloniaError);
        if (avaloniaBitmap != null)
        {
            error = null;
            return avaloniaBitmap;
        }

        Bitmap? strideBitmap = TryCreateTextureThumbnailWithStride(texturePath, out string? strideError);
        if (strideBitmap != null)
        {
            error = null;
            return strideBitmap;
        }

        try
        {
            using var image = ImageSharpImage.Load<Rgba32>(texturePath);
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
            error = $"File thumbnail decode failed. TextureTool: {textureToolError ?? "not attempted"} Avalonia: {avaloniaError ?? "not attempted"} Stride: {strideError ?? "not attempted"} ImageSharp: {exception.Message}";
            return null;
        }
    }

    private static Bitmap? TryCreateTextureThumbnailWithAvalonia(string texturePath, out string? error)
    {
        try
        {
            using var stream = File.OpenRead(texturePath);
            error = null;
            return new Bitmap(stream);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static Bitmap? TryCreateTextureThumbnailWithTextureTool(string texturePath, out string? error)
    {
        try
        {
            using var textureTool = new TextureTool();
            using var texImage = textureTool.Load(texturePath, isSRgb: true);

            if (IsCompressedTextureFormat(texImage.Format))
            {
                textureTool.Decompress(texImage, isSRgb: IsSrgbTextureFormat(texImage.Format));
            }

            textureTool.Resize(texImage, 128, 128, Filter.Rescaling.Lanczos3);

            using var image = textureTool.ConvertToStrideImage(texImage);
            using var pngStream = new MemoryStream();
            image.Save(pngStream, Stride.Graphics.ImageFileType.Png);

            error = null;
            return CreateOpaqueBitmapFromPngStream(pngStream);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static Bitmap? TryCreateTextureThumbnailWithStride(string texturePath, out string? error)
    {
        try
        {
            using var fileStream = File.OpenRead(texturePath);
            using var image = Stride.Graphics.Image.Load(fileStream, loadAsSRGB: true);
            using var pngStream = new MemoryStream();
            image.Save(pngStream, Stride.Graphics.ImageFileType.Png);
            error = null;
            return CreateOpaqueBitmapFromPngStream(pngStream);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static Bitmap CreateOpaqueBitmapFromPngStream(MemoryStream pngStream)
    {
        pngStream.Position = 0;
        using var image = ImageSharpImage.Load<Rgba32>(pngStream);
        image.ProcessPixelRows(static accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x].A = byte.MaxValue;
                }
            }
        });

        using var opaqueStream = new MemoryStream();
        image.Save(opaqueStream, new PngEncoder());
        opaqueStream.Position = 0;
        return new Bitmap(opaqueStream);
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

    private static bool IsCompressedTextureFormat(Stride.Graphics.PixelFormat format)
    {
        return format is Stride.Graphics.PixelFormat.BC1_Typeless
            or Stride.Graphics.PixelFormat.BC1_UNorm
            or Stride.Graphics.PixelFormat.BC1_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC2_Typeless
            or Stride.Graphics.PixelFormat.BC2_UNorm
            or Stride.Graphics.PixelFormat.BC2_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC3_Typeless
            or Stride.Graphics.PixelFormat.BC3_UNorm
            or Stride.Graphics.PixelFormat.BC3_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC4_Typeless
            or Stride.Graphics.PixelFormat.BC4_UNorm
            or Stride.Graphics.PixelFormat.BC4_SNorm
            or Stride.Graphics.PixelFormat.BC5_Typeless
            or Stride.Graphics.PixelFormat.BC5_UNorm
            or Stride.Graphics.PixelFormat.BC5_SNorm
            or Stride.Graphics.PixelFormat.BC6H_Typeless
            or Stride.Graphics.PixelFormat.BC6H_Uf16
            or Stride.Graphics.PixelFormat.BC6H_Sf16
            or Stride.Graphics.PixelFormat.BC7_Typeless
            or Stride.Graphics.PixelFormat.BC7_UNorm
            or Stride.Graphics.PixelFormat.BC7_UNorm_SRgb
            or Stride.Graphics.PixelFormat.ETC1
            or Stride.Graphics.PixelFormat.ETC2_RGB
            or Stride.Graphics.PixelFormat.ETC2_RGB_SRgb
            or Stride.Graphics.PixelFormat.ETC2_RGB_A1
            or Stride.Graphics.PixelFormat.ETC2_RGBA
            or Stride.Graphics.PixelFormat.ETC2_RGBA_SRgb
            or Stride.Graphics.PixelFormat.EAC_R11_Unsigned
            or Stride.Graphics.PixelFormat.EAC_R11_Signed
            or Stride.Graphics.PixelFormat.EAC_RG11_Unsigned
            or Stride.Graphics.PixelFormat.EAC_RG11_Signed;
    }

    private static bool IsSrgbTextureFormat(Stride.Graphics.PixelFormat format)
    {
        return format is Stride.Graphics.PixelFormat.R8G8B8A8_UNorm_SRgb
            or Stride.Graphics.PixelFormat.B8G8R8A8_UNorm_SRgb
            or Stride.Graphics.PixelFormat.B8G8R8X8_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC1_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC2_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC3_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC7_UNorm_SRgb
            or Stride.Graphics.PixelFormat.ETC2_RGB_SRgb
            or Stride.Graphics.PixelFormat.ETC2_RGBA_SRgb;
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

    private void AddConsole(string level, string message)
    {
        ConsoleEntries.Add(new ConsoleEntryViewModel(level, message));
        while (ConsoleEntries.Count > 200)
        {
            ConsoleEntries.RemoveAt(0);
        }
    }

    private sealed record TextureThumbnailCacheEntry(DateTime LastWriteUtc, Bitmap? Bitmap);
}
