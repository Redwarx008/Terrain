#nullable enable

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Terrain.Editor.ViewModels;

namespace Terrain.Editor.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyStartupSizeForPhysicalPixels();
    }

    private void ApplyStartupSizeForPhysicalPixels()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        uint dpi = GetDpiForSystem();
        if (dpi == 0)
        {
            dpi = 96;
        }

        double scale = dpi / 96d;
        Width = 1920d / scale;
        Height = 1080d / scale;
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        if (DataContext is EditorShellViewModel viewModel)
        {
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
