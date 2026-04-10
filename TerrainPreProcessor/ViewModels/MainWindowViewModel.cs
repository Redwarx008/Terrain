using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using TerrainPreProcessor.Models;
using TerrainPreProcessor.Resources;
using TerrainPreProcessor.Services;
using TerrainPreProcessor.Views;

namespace TerrainPreProcessor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _heightMapPath;

    [ObservableProperty]
    private string? _splatMapPath;

    [ObservableProperty]
    private string? _outputPath;

    [ObservableProperty]
    private int _selectedLeafNodeSizeIndex = 1; // 默认 16

    [ObservableProperty]
    private int _selectedTileSizeIndex = 0; // 默认 129

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = Strings.Ready;

    public ObservableCollection<int> LeafNodeSizeOptions { get; } = new() { 8, 16, 32, 64 };
    public ObservableCollection<int> TileSizeOptions { get; } = new() { 129, 257, 513 };

    private readonly WindowService _windowService;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public MainWindowViewModel(WindowService windowService)
    {
        _windowService = windowService;
    }

    public int LeafNodeSize => LeafNodeSizeOptions[SelectedLeafNodeSizeIndex];
    public int TileSize => TileSizeOptions[SelectedTileSizeIndex];

    [RelayCommand]
    private async Task BrowseHeightMap()
    {
        var result = await _windowService.OpenFilePickerAsync(_loc["SelectHeightMap"], new[] { "*.png", "*.raw", "*.r16" });
        if (result != null)
        {
            HeightMapPath = result;
            // 自动更新输出路径
            var directory = Path.GetDirectoryName(result);
            var fileName = Path.GetFileNameWithoutExtension(result);
            OutputPath = Path.Combine(directory ?? "", $"{fileName}.terrain");
        }
    }

    [RelayCommand]
    private async Task BrowseSplatMap()
    {
        var result = await _windowService.OpenFilePickerAsync(_loc["SelectSplatMap"], new[] { "*.png", "*.tga", "*.jpg" });
        if (result != null)
        {
            SplatMapPath = result;
        }
    }

    [RelayCommand]
    private async Task BrowseOutputPath()
    {
        var result = await _windowService.SaveFilePickerAsync(_loc["SaveTerrainFile"], "*.terrain");
        if (result != null)
        {
            OutputPath = result;
        }
    }

    [RelayCommand]
    private async Task StartProcessing()
    {
        if (IsProcessing) return;

        var config = new ProcessingConfig
        {
            HeightMapPath = HeightMapPath,
            SplatMapPath = SplatMapPath,
            OutputPath = OutputPath,
            LeafNodeSize = LeafNodeSize,
            TileSize = TileSize
        };

        // 使用配置验证
        if (!config.Validate(out string errorMessage))
        {
            await _windowService.ShowErrorDialogAsync(_loc["ConfigurationError"], errorMessage);
            return;
        }

        IsProcessing = true;
        StatusMessage = Strings.Processing;

        ProgressWindow? progressWindow = null;

        try
        {
            progressWindow = _windowService.ShowProgressWindow(_loc["ProcessingTerrainData"]);

            // 进度更新节流
            DateTime lastProgressUpdate = DateTime.MinValue;
            IProgress<(int current, int total, string message)> progressReporter = 
                new Progress<(int current, int total, string message)>(progress =>
                {
                    // 节流：每 50ms 最多更新一次 UI
                    var now = DateTime.Now;
                    if ((now - lastProgressUpdate).TotalMilliseconds < 50 && progress.current < progress.total)
                        return;
                    
                    lastProgressUpdate = now;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        progressWindow?.UpdateProgress(progress.current, progress.total, progress.message);
                    });
                });

            // 使用 Result 模式处理
            var result = await TerrainProcessor.ProcessAsync(config, progressReporter);

            progressWindow.Close();
            progressWindow = null;

            if (result.IsSuccess)
            {
                StatusMessage = Strings.ProcessingComplete;
                await _windowService.ShowInfoDialogAsync(_loc["Complete"], _loc["TerrainProcessingComplete"]);
            }
            else
            {
                StatusMessage = $"{Strings.ProcessingError}: {result.ErrorMessage}";
                await _windowService.ShowErrorDialogAsync(_loc["ProcessingError"], result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // 记录详细错误信息
            Console.Error.WriteLine($"处理错误: {ex}");
            
            progressWindow?.Close();
            StatusMessage = $"{Strings.ProcessingError}: {ex.Message}";
            await _windowService.ShowErrorDialogAsync(_loc["ProcessingError"], ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void ClearSplatMap()
    {
        SplatMapPath = null;
    }
}
