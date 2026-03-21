using Avalonia.Controls;
using TerrainPreProcessor.ViewModels;

namespace TerrainPreProcessor.Views;

public partial class ProgressWindow : Window
{
    private readonly ProgressWindowViewModel _viewModel = new();

    public ProgressWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public void UpdateProgress(int current, int total, string message)
    {
        _viewModel.UpdateProgress(current, total, message);
    }
}
