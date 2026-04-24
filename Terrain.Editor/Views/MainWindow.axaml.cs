#nullable enable

using Avalonia.Controls;
using Terrain.Editor.ViewModels;

namespace Terrain.Editor.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        if (DataContext is EditorShellViewModel viewModel)
        {
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }
}
