using CommunityToolkit.Mvvm.ComponentModel;

namespace TerrainPreProcessor.ViewModels;

public partial class ProgressWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMaximum = 100;

    [ObservableProperty]
    private string _statusText = "准备中...";

    public void UpdateProgress(int current, int total, string message)
    {
        ProgressValue = current;
        ProgressMaximum = total;
        StatusText = message;
    }
}
